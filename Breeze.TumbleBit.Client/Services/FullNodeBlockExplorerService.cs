using NBitcoin.RPC;
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
        private FullNode fullNode;
        private IWatchOnlyWalletManager watchOnlyWalletManager;

        public FullNodeBlockExplorerService(FullNodeWalletCache cache, IRepository repo, FullNode fullNode, IWatchOnlyWalletManager watchOnlyWalletManager)
        {
            if (repo == null)
                throw new ArgumentNullException("repo");
            if (cache == null)
                throw new ArgumentNullException("cache");
            if (fullNode == null)
                throw new ArgumentNullException("fullNode");
            if (watchOnlyWalletManager == null)
                throw new ArgumentNullException("watchOnlyWalletManager");

            _Repo = repo;
            _Cache = cache;
            this.fullNode = fullNode;
            this.watchOnlyWalletManager = watchOnlyWalletManager;
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
                var h = this.fullNode.WalletManager.LastReceivedBlockHash();
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
            
            var address = scriptPubKey.GetDestinationAddress(this.fullNode.Network);
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
                    foreach (var wallet in this.fullNode.WalletManager.Wallets)
                    {
                        if (found)
                            break;

                        foreach (var account in wallet.GetAccountsByCoinType((CoinType)this.fullNode.Network.Consensus.CoinType))
                        {
                            var txData = account.GetTransactionsById(tx.Transaction.GetHash());
                            if (txData != null)
                            {
                                found = true;

                                // TODO: Is it possible for GetTransactionsById to return multiple results?
                                var trx = txData.First<TransactionData>();

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
            var watchOnlyWallet = this.watchOnlyWalletManager.GetWallet();

            // TODO: This seems highly inefficient, maybe we need a cache or quicker lookup mechanism
            foreach (var watchOnlyTx in watchOnlyWallet.Transactions)
            {
                // Looking for funds received by address only, so only examine transaction outputs
                foreach (var vout in watchOnlyTx.vout)
                {
                    // Look at each of the addresses contained in the scriptPubKey to see if they match
                    foreach (var addr in vout.scriptPubKey.addresses)
                    {
                        if (address.ToString() == addr)
                        {
                            txIdList.Add(new uint256(watchOnlyTx.txid));
                        }
                    }
                }
            }

            // Search transactions in regular wallet for matching address criteria

            foreach (var wallet in this.fullNode.WalletManager.Wallets)
            {
                foreach (var walletTx in wallet.GetAllTransactionsByCoinType((CoinType)this.fullNode.Network.Consensus.CoinType))
                {
                    if (address == walletTx.ScriptPubKey.GetDestinationAddress(this.fullNode.Network))
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
                Transaction trx = this.fullNode.MempoolManager?.InfoAsync(txId)?.Result.Trx;

                if (trx == null)
                    trx = this.fullNode.BlockStoreManager?.BlockRepository?.GetTrxAsync(txId).Result;

                // Need number of confirmations as well
                var blockHash = this.fullNode.BlockStoreManager?.BlockRepository?.GetTrxBlockIdAsync(txId).Result;
                var block = this.fullNode.Chain.GetBlock(blockHash);
                var blockHeight = block.Height;
                var tipHeight = this.fullNode.Chain.Tip.Height;
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
            this.watchOnlyWalletManager.Watch(scriptPubkey);
        }

        public int GetBlockConfirmations(uint256 blockId)
        {
            var block = this.fullNode.Chain.GetBlock(blockId);
            var tipHeight = this.fullNode.Chain.Tip.Height;
            var confirmations = tipHeight - block.Height;
            var confCount = Math.Max(0, confirmations);

            return confCount;
        }

        public bool TrackPrunedTransaction(Transaction transaction, MerkleBlock merkleProof)
        {
            var result = RPCClient.SendCommandNoThrows("importprunedfunds", transaction.ToHex(), Encoders.Hex.EncodeData(merkleProof.ToBytes()));
            var success = result != null && result.Error == null;
            if (success)
            {
                _Cache.ImportTransaction(transaction, GetBlockConfirmations(merkleProof.Header.GetHash()));
            }
            return success;
        }
    }
}
