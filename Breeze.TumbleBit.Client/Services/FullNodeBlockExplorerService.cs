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
                foreach (var tx in results.ToList())
                {
                    MerkleBlock proof = null;
                    var result = RPCClient.SendCommandNoThrows("gettxoutproof", new JArray(tx.Transaction.GetHash().ToString()));
                    if (result == null || result.Error != null)
                    {
                        results.Remove(tx);
                        continue;
                    }
                    proof = new MerkleBlock();
                    proof.ReadWrite(Encoders.Hex.DecodeData(result.ResultString));
                    tx.MerkleProof = proof;
                }
            }
            return results.ToArray();
        }

        private List<TransactionInformation> QueryWithListReceivedByAddress(bool withProof, BitcoinAddress address)
        {
            var result = RPCClient.SendCommand("listreceivedbyaddress", 0, false, true, address.ToString());
            var transactions = ((JArray)result.Result).OfType<JObject>().Select(o => o["txids"]).OfType<JArray>().SingleOrDefault();
            if (transactions == null)
                return null;

            HashSet<uint256> resultsSet = new HashSet<uint256>();
            List<TransactionInformation> results = new List<TransactionInformation>();
            foreach (var txIdObj in transactions)
            {
                var txId = new uint256(txIdObj.ToString());
                //May have duplicates
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
