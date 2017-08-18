﻿using NBitcoin.RPC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json.Linq;
using NBitcoin.DataEncoders;
using System.Threading;
using NTumbleBit.Services;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.WatchOnlyWallet;

namespace Breeze.TumbleBit.Client.Services
{
    public class FullNodeBlockExplorerService : IBlockExplorerService
    {
        FullNodeWalletCache _Cache;
        private TumblingState tumblingState;

        public FullNodeBlockExplorerService(FullNodeWalletCache cache, IRepository repo, TumblingState tumblingState)
        {
            if (repo == null)
                throw new ArgumentNullException("repo");
            if (cache == null)
                throw new ArgumentNullException("cache");
            if (tumblingState == null)
                throw new ArgumentNullException("tumblingState");

            _Repo = repo;
            _Cache = cache;
            this.tumblingState = tumblingState;
        }

        IRepository _Repo;

        public int GetCurrentHeight()
        {
            return _Cache.BlockCount;
        }

        public uint256 WaitBlock(uint256 currentBlock, CancellationToken cancellation = default(CancellationToken))
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                var h = this.tumblingState.walletManager.WalletTipHash;
                if (h != currentBlock)
                {
                    _Cache.Refresh(h);
                    return h;
                }
                cancellation.WaitHandle.WaitOne(5000);
            }
        }

        public TransactionInformation[] GetTransactions(Script scriptPubKey, bool withProof)
        {
            if (scriptPubKey == null)
                throw new ArgumentNullException(nameof(scriptPubKey));
            
            var address = scriptPubKey.GetDestinationAddress(this.tumblingState.TumblerNetwork);
            if (address == null)
                return new TransactionInformation[0];

            var walletTransactions = _Cache.GetEntries();
            List<TransactionInformation> results = Filter(walletTransactions, !withProof, address);

            if (withProof)
            {
                bool found;
                foreach (var tx in results.ToList())
                {
                    found = false;
                    MerkleBlock proof = null;

                    // TODO: Not efficient

                    foreach (var walletName in this.tumblingState.walletManager.GetWallets())
                    {
                        if (found)
                            break;

                        var wallet = this.tumblingState.walletManager.GetWallet(walletName);

                        foreach (var account in wallet.GetAccountsByCoinType(this.tumblingState.coinType))
                        {
                            var txData = account.GetTransactionsById(tx.Transaction.GetHash());
                            if (txData != null)
                            {
                                found = true;

                                // TODO: Is it possible for GetTransactionsById to return multiple results?
                                var trx = txData.First<Stratis.Bitcoin.Features.Wallet.TransactionData>();

                                proof = new MerkleBlock();
                                proof.ReadWrite(Encoders.Hex.DecodeData(trx.MerkleProof.ToHex()));

                                tx.MerkleProof = proof;

                                break;
                            }
                        }
                    }

                    if (!found)
                    {
                        results.Remove(tx);
                        continue;
                    }
                }
            }
            return results.ToArray();
        }

        private List<TransactionInformation> QueryWithListReceivedByAddress(bool withProof, BitcoinAddress address)
        {
            // List all transactions involving a particular address, including those in watch-only wallet
            // (zero confirmations are acceptable)

            List<uint256> txIdList = new List<uint256>();

            // First examine watch-only wallet
            var watchOnlyWallet = this.tumblingState.watchOnlyWalletManager.GetWatchOnlyWallet();

            // TODO: This seems highly inefficient, maybe we need a cache or quicker lookup mechanism
            foreach (var watchedAddressKeyValue in watchOnlyWallet.WatchedAddresses)
            {
                if (watchedAddressKeyValue.Value.Script != address.ScriptPubKey)
                    continue;

                var watchedAddress = watchedAddressKeyValue.Value;

                foreach (var watchOnlyTx in watchedAddress.Transactions)
                {
                    // Looking for funds received by address only, so only examine transaction outputs
                    foreach (var vout in watchOnlyTx.Value.Transaction.Outputs)
                    {
                        // Look at each of the addresses contained in the scriptPubKey to see if they match
                        if (address == vout.ScriptPubKey.GetDestinationAddress(this.tumblingState.TumblerNetwork))
                        {
                             txIdList.Add(watchOnlyTx.Value.Transaction.GetHash());
                        }
                    }
                }
            }

            // Search transactions in regular wallet for matching address criteria

            foreach (var walletName in this.tumblingState.walletManager.GetWallets())
            {
                var wallet = this.tumblingState.walletManager.GetWallet(walletName);
                foreach (var walletTx in wallet.GetAllTransactionsByCoinType(this.tumblingState.coinType))
                {
                    if (address == walletTx.ScriptPubKey.GetDestinationAddress(this.tumblingState.TumblerNetwork))
                    {
                        txIdList.Add(walletTx.Id);
                    }
                }
            }

            if (txIdList.Count == 0)
                return null;

            HashSet<uint256> resultsSet = new HashSet<uint256>();
            List<TransactionInformation> results = new List<TransactionInformation>();
            foreach (var txId in txIdList)
            {
                // May have duplicates
                if (!resultsSet.Contains(txId))
                {
                    var tx = GetTransaction(txId);
                    if (tx == null || (withProof && tx.Confirmations == 0))
                        continue;
                    resultsSet.Add(txId);
                    results.Add(tx);
                }
            }
            return results;
        }

        private List<TransactionInformation> Filter(FullNodeWalletEntry[] entries, bool includeUnconf, BitcoinAddress address)
        {
            List<TransactionInformation> results = new List<TransactionInformation>();
            HashSet<uint256> resultsSet = new HashSet<uint256>();
            foreach (var obj in entries)
            {
                //May have duplicates
                if (!resultsSet.Contains(obj.TransactionId))
                {
                    var confirmations = obj.Confirmations;
                    var tx = _Cache.GetTransaction(obj.TransactionId);

                    if (tx == null || (!includeUnconf && confirmations == 0))
                        continue;

                    if (tx.Outputs.Any(o => o.ScriptPubKey == address.ScriptPubKey) ||
                       tx.Inputs.Any(o => o.ScriptSig.GetSigner().ScriptPubKey == address.ScriptPubKey))
                    {

                        resultsSet.Add(obj.TransactionId);
                        results.Add(new TransactionInformation()
                        {
                            Transaction = tx,
                            Confirmations = confirmations
                        });
                    }
                }
            }
            return results;
        }

        public TransactionInformation GetTransaction(uint256 txId)
        {
            try
            {
                Transaction trx = this.tumblingState.mempoolManager.InfoAsync(txId)?.Result.Trx;

                if (trx == null)
                    trx = this.tumblingState.blockStoreManager.BlockRepository?.GetTrxAsync(txId).Result;
                
                // Need number of confirmations as well
                var blockHash = this.tumblingState.blockStoreManager.BlockRepository?.GetTrxBlockIdAsync(txId).Result;
                var block = this.tumblingState.chain.GetBlock(blockHash);
                var blockHeight = block.Height;
                var tipHeight = this.tumblingState.chain.Tip.Height;
                var confirmations = tipHeight - blockHeight;
                var confCount = Math.Max(0, confirmations);

                return new TransactionInformation
                {
                    Confirmations = confCount,
                    Transaction = trx
                };
            }
            // TODO: Replace this with the correct exception type
            catch (RPCException) { return null; }
        }

        public void Track(Script scriptPubkey)
        {
            this.tumblingState.watchOnlyWalletManager.WatchAddress(scriptPubkey.GetDestinationAddress(this.tumblingState.TumblerNetwork).ToString());
        }

        public int GetBlockConfirmations(uint256 blockId)
        {
            var block = this.tumblingState.chain.GetBlock(blockId);
            var tipHeight = this.tumblingState.chain.Tip.Height;
            var confirmations = tipHeight - block.Height;
            var confCount = Math.Max(0, confirmations);

            return confCount;
        }

        public bool TrackPrunedTransaction(Transaction transaction, MerkleBlock merkleProof)
        {
            var blockHash = this.tumblingState.blockStoreManager.BlockRepository?.GetTrxBlockIdAsync(transaction.GetHash()).Result;
            var chainBlock = this.tumblingState.chain.GetBlock(blockHash);
            var block = this.tumblingState.blockStoreManager.BlockRepository?.GetAsync(blockHash).Result;

            this.tumblingState.walletManager.ProcessTransaction(transaction, chainBlock.Height, block);

            _Cache.ImportTransaction(transaction, GetBlockConfirmations(merkleProof.Header.GetHash()));

            return true;
        }
    }
}
