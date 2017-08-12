using NBitcoin;
using NTumbleBit.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.Wallet;

namespace Breeze.TumbleBit.Client.Services
{
    public class FullNodeWalletEntry
    {
        public uint256 TransactionId
        {
            get; set;
        }
        public int Confirmations
        {
            get; set;
        }
    }

    /// <summary>
    /// Workaround around slow Bitcoin Core RPC. 
    /// We are refreshing the list of received transaction once per block.
    /// </summary>
    public class FullNodeWalletCache
    {
        private readonly IRepository _Repo;
        private FullNode fullNode;
        public FullNodeWalletCache(IRepository repository, FullNode fullNode)
        {
            if(repository == null)
                throw new ArgumentNullException("repository");
            if (fullNode == null)
                throw new ArgumentNullException("fullNode");
            
            _Repo = repository;
            this.fullNode = fullNode;
        }

        volatile uint256 _RefreshedAtBlock;

        public void Refresh(uint256 currentBlock)
        {
            var refreshedAt = _RefreshedAtBlock;
            if(refreshedAt != currentBlock)
            {
                lock(_Transactions)
                {
                    if(refreshedAt != currentBlock)
                    {
                        RefreshBlockCount();
                        _Transactions = ListTransactions(ref _KnownTransactions);
                        _RefreshedAtBlock = currentBlock;
                    }
                }
            }
        }

        int _BlockCount;
        public int BlockCount
        {
            get
            {
                if(_BlockCount == 0)
                {
                    RefreshBlockCount();
                }
                return _BlockCount;
            }
        }

        private void RefreshBlockCount()
        {
            Interlocked.Exchange(ref _BlockCount, this.fullNode.WalletManager.LastBlockHeight());
        }

        public Transaction GetTransaction(uint256 txId)
        {
            var cached = GetCachedTransaction(txId);
            if(cached != null)
                return cached;
            var tx = FetchTransaction(txId);
            if(tx == null)
                return null;
            PutCached(tx);
            return tx;
        }

        ConcurrentDictionary<uint256, Transaction> _TransactionsByTxId = new ConcurrentDictionary<uint256, Transaction>();


        private Transaction FetchTransaction(uint256 txId)
        {
            try
            {
                Transaction trx = this.fullNode.MempoolManager?.InfoAsync(txId)?.Result.Trx;

                if (trx == null)
                    trx = this.fullNode.BlockStoreManager?.BlockRepository?.GetTrxAsync(txId).Result;

                return trx;

                //check in the wallet tx
                /*foreach (var wallet in this.walletManager.Wallets)
                {
                    foreach (var account in wallet.GetAccountsByCoinType(this.coinType))
                    {
                        var txData = account.GetTransactionsById(txId);
                        if (txData != null)
                        {
                            // Look up Transaction object from the actual blockchain.
                            // A Transaction is not a wallet-level concept as there
                            // can be multiple recipients not contained with a
                            // wallet controlled by the node.

                            // TODO: Make helper function to retrive full Tranasction from chain by ID
                        }
                    }
                }*/

                //var result = _RPCClient.SendCommandNoThrows("gettransaction", txId.ToString(), true);
                //if(result == null || result.Error != null)
                //{
                //    //check in the txindex
                //    result = _RPCClient.SendCommandNoThrows("getrawtransaction", txId.ToString(), 1);
                //    if(result == null || result.Error != null)
                //        return null;
                //}
                //var tx = new Transaction((string)result.Result["hex"]);
            }
            catch(Exception) { return null; }
        }

        public FullNodeWalletEntry[] GetEntries()
        {
            lock(_Transactions)
            {
                return _Transactions.ToArray();
            }
        }

        private void PutCached(Transaction tx)
        {
            tx.CacheHashes();
            _Repo.UpdateOrInsert("CachedTransactions", tx.GetHash().ToString(), tx, (a, b) => b);
            lock(_TransactionsByTxId)
            {
                _TransactionsByTxId.TryAdd(tx.GetHash(), tx);
            }
        }

        private Transaction GetCachedTransaction(uint256 txId)
        {

            Transaction tx = null;
            if(_TransactionsByTxId.TryGetValue(txId, out tx))
            {
                return tx;
            }
            var cached = _Repo.Get<Transaction>("CachedTransactions", txId.ToString());
            if(cached != null)
                _TransactionsByTxId.TryAdd(txId, cached);
            return cached;
        }


        List<FullNodeWalletEntry> _Transactions = new List<FullNodeWalletEntry>();
        HashSet<uint256> _KnownTransactions = new HashSet<uint256>();
        List<FullNodeWalletEntry> ListTransactions(ref HashSet<uint256> knownTransactions)
        {
            List<FullNodeWalletEntry> array = new List<FullNodeWalletEntry>();
            knownTransactions = new HashSet<uint256>();
            var removeFromCache = new HashSet<uint256>(_TransactionsByTxId.Values.Select(tx => tx.GetHash()));
            int count = 100;
            int skip = 0;
            int highestConfirmation = 0;

            while(true)
            {
                var result = _RPCClient.SendCommandNoThrows("listtransactions", "*", count, skip, true);
                skip += count;
                if(result.Error != null)
                    return null;
                var transactions = (JArray)result.Result;
                foreach(var obj in transactions)
                {
                    var entry = new FullNodeWalletEntry();
                    entry.Confirmations = obj["confirmations"] == null ? 0 : (int)obj["confirmations"];
                    entry.TransactionId = new uint256((string)obj["txid"]);
                    removeFromCache.Remove(entry.TransactionId);
                    if(knownTransactions.Add(entry.TransactionId))
                    {
                        array.Add(entry);
                    }
                    if(obj["confirmations"] != null)
                    {
                        highestConfirmation = Math.Max(highestConfirmation, (int)obj["confirmations"]);
                    }
                }
                if(transactions.Count < count || highestConfirmation >= 1400)
                    break;
            }
            foreach(var remove in removeFromCache)
            {
                Transaction opt;
                _TransactionsByTxId.TryRemove(remove, out opt);
            }
            return array;
        }


        public void ImportTransaction(Transaction transaction, int confirmations)
        {
            PutCached(transaction);
            lock(_Transactions)
            {
                if(_KnownTransactions.Add(transaction.GetHash()))
                {
                    _Transactions.Insert(0,
                        new FullNodeWalletEntry()
                        {
                            Confirmations = confirmations,
                            TransactionId = transaction.GetHash()
                        });
                }
            }
        }
    }
}
