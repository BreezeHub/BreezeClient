﻿using NBitcoin.RPC;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NTumbleBit;
using NTumbleBit.Logging;
using NTumbleBit.Services;
using Stratis.Bitcoin;

namespace Breeze.TumbleBit.Client.Services
{
    public class FullNodeBroadcastService : IBroadcastService
    {
        public class Record
        {
            public int Expiration
            {
                get; set;
            }
            public Transaction Transaction
            {
                get; set;
            }
        }

        FullNodeWalletCache _Cache;
        private TumblingState tumblingState;

        public FullNodeBroadcastService(FullNodeWalletCache cache, IRepository repository, TumblingState tumblingState)
        {
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));
            if (tumblingState == null)
                throw new ArgumentNullException(nameof(tumblingState));
            
            _Repository = repository;
            _Cache = cache;
            _BlockExplorerService = new FullNodeBlockExplorerService(cache, repository, tumblingState);
        }

        private readonly FullNodeBlockExplorerService _BlockExplorerService;
        public FullNodeBlockExplorerService BlockExplorerService
        {
            get
            {
                return _BlockExplorerService;
            }
        }

        private readonly IRepository _Repository;
        public IRepository Repository
        {
            get
            {
                return _Repository;
            }
        }

        public Record[] GetTransactions()
        {
            var transactions = Repository.List<Record>("Broadcasts");
            foreach (var tx in transactions)
                tx.Transaction.CacheHashes();
            return transactions.TopologicalSort(tx => transactions.Where(tx2 => tx.Transaction.Inputs.Any<TxIn>(input => input.PrevOut.Hash == tx2.Transaction.GetHash()))).ToArray();
        }

        public Transaction[] TryBroadcast()
        {
            uint256[] r = null;
            return TryBroadcast(ref r);
        }

        public Transaction[] TryBroadcast(ref uint256[] knownBroadcasted)
        {
            var startTime = DateTimeOffset.UtcNow;
            int totalEntries = 0;
            List<Transaction> broadcasted = new List<Transaction>();

            HashSet<uint256> knownBroadcastedSet = new HashSet<uint256>(knownBroadcasted ?? new uint256[0]);
            int height = _Cache.BlockCount;
            foreach (var obj in _Cache.GetEntries())
            {
                if (obj.Confirmations > 0)
                    knownBroadcastedSet.Add(obj.TransactionId);
            }

            foreach (var tx in GetTransactions())
            {
                totalEntries++;
                if (!knownBroadcastedSet.Contains(tx.Transaction.GetHash()) &&
                    TryBroadcastCore(tx, height))
                {
                    broadcasted.Add(tx.Transaction);
                }
                knownBroadcastedSet.Add(tx.Transaction.GetHash());
            }
            knownBroadcasted = knownBroadcastedSet.ToArray();
            Logs.Broadcasters.LogInformation($"Broadcasted {broadcasted.Count} transaction(s), monitoring {totalEntries} entries in {(long)(DateTimeOffset.UtcNow - startTime).TotalSeconds} seconds");
            return broadcasted.ToArray();
        }

        private bool TryBroadcastCore(Record tx, int currentHeight)
        {
            bool remove;
            var result = TryBroadcastCore(tx, currentHeight, out remove);
            if (remove)
                RemoveRecord(tx);
            return result;
        }

        private bool TryBroadcastCore(Record tx, int currentHeight, out bool remove)
        {
            remove = currentHeight >= tx.Expiration;

            // Happens when the caller does not know the previous input yet
            if (tx.Transaction.Inputs.Count == 0 || tx.Transaction.Inputs[0].PrevOut.Hash == uint256.Zero)
                return false;

            bool isFinal = tx.Transaction.IsFinal(DateTimeOffset.UtcNow, currentHeight + 1);
            if (!isFinal || IsDoubleSpend(tx.Transaction))
                return false;

            try
            {
                if (!this.tumblingState.walletManager.SendTransaction(tx.Transaction.ToHex()))
                    return false;
                
                _Cache.ImportTransaction(tx.Transaction, 0);
                Logs.Broadcasters.LogInformation($"Broadcasted {tx.Transaction.GetHash()}");
                return true;
            }
            // TODO: Change exception type to better reflect use of full node
            catch (RPCException ex)
            {
                if (ex.RPCResult == null || ex.RPCResult.Error == null)
                {
                    return false;
                }
                var error = ex.RPCResult.Error.Message;
                if (ex.RPCResult.Error.Code != RPCErrorCode.RPC_TRANSACTION_ALREADY_IN_CHAIN &&
                   !error.EndsWith("bad-txns-inputs-spent", StringComparison.OrdinalIgnoreCase) &&
                   !error.EndsWith("txn-mempool-conflict", StringComparison.OrdinalIgnoreCase) &&
                   !error.EndsWith("Missing inputs", StringComparison.OrdinalIgnoreCase))
                {
                    remove = false;
                }
            }
            return false;
        }

        private bool IsDoubleSpend(Transaction tx)
        {
            var spentInputs = new HashSet<OutPoint>(tx.Inputs.Select(txin => txin.PrevOut));
            foreach (var entry in _Cache.GetEntries())
            {
                if (entry.Confirmations > 0)
                {
                    var walletTransaction = _Cache.GetTransaction(entry.TransactionId);
                    foreach (var input in walletTransaction.Inputs)
                    {
                        if (spentInputs.Contains(input.PrevOut))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private void RemoveRecord(Record tx)
        {
            Repository.Delete<Record>("Broadcasts", tx.Transaction.GetHash().ToString());
            Repository.UpdateOrInsert<Transaction>("CachedTransactions", tx.Transaction.GetHash().ToString(), tx.Transaction, (a, b) => a);
        }

        public bool Broadcast(Transaction transaction)
        {
            var record = new Record();
            record.Transaction = transaction;
            var height = _Cache.BlockCount;
            //3 days expiration
            record.Expiration = height + (int)(TimeSpan.FromDays(3).Ticks / Network.Main.Consensus.PowTargetSpacing.Ticks);
            Repository.UpdateOrInsert<Record>("Broadcasts", transaction.GetHash().ToString(), record, (o, n) => o);
            return TryBroadcastCore(record, height);
        }

        public Transaction GetKnownTransaction(uint256 txId)
        {
            return Repository.Get<Record>("Broadcasts", txId.ToString())?.Transaction ??
                   Repository.Get<Transaction>("CachedTransactions", txId.ToString());
        }
    }
}
