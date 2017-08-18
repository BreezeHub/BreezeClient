﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Newtonsoft.Json;
using NTumbleBit.ClassicTumbler;
using NTumbleBit.PuzzlePromise;
using NTumbleBit.PuzzleSolver;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.WatchOnlyWallet;

namespace Breeze.TumbleBit.Client
{
    public class TumblingState : IStateMachine
    {
        private const string StateFileName = "tumblebit_state.json";

        public ILogger logger;
        public ConcurrentChain chain;
        public WalletManager walletManager;
        public WalletSyncManager walletSyncManager;
        public WatchOnlyWalletManager watchOnlyWalletManager;
        public WalletTransactionHandler walletTransactionHandler;
        public BlockStoreManager blockStoreManager;
        public MempoolManager mempoolManager;

        // TODO: Does this need to be saved? Can be derived from network
        public CoinType coinType;

        // TODO: Remove or store the tumbler parameters for every used tumbler
        [JsonProperty("tumblerParameters")]
        public ClassicTumblerParameters TumblerParameters { get; set; }

        [JsonProperty("tumblerUri")]
        public Uri TumblerUri { get; set; }

        [JsonProperty("lastBlockReceivedHeight", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int LastBlockReceivedHeight { get; set; }

        [JsonProperty("originWalletName", NullValueHandling = NullValueHandling.Ignore)]
        public string OriginWalletName { get; set; }

        [JsonProperty("destinationWalletName", NullValueHandling = NullValueHandling.Ignore)]
        public string DestinationWalletName { get; set; }       

        [JsonProperty("network", NullValueHandling = NullValueHandling.Ignore)]
        public Network TumblerNetwork { get; set; }

        [JsonIgnore]
        public Wallet OriginWallet { get; set; }

        [JsonIgnore]
        public Wallet DestinationWallet { get; set; }
        
        [JsonConstructor]
        public TumblingState()
        {
        }

        public TumblingState(ILoggerFactory loggerFactory, 
            ConcurrentChain chain,
            WalletManager walletManager,
            WatchOnlyWalletManager  watchOnlyWalletManager,
            Network network, 
            WalletTransactionHandler walletTransactionHandler,
            BlockStoreManager blockStoreManager,
            MempoolManager mempoolManager,
            WalletSyncManager walletSyncManager)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.chain = chain;
            this.walletManager = walletManager;
            this.watchOnlyWalletManager = watchOnlyWalletManager;
            this.coinType = (CoinType)network.Consensus.CoinType;
            this.walletTransactionHandler = walletTransactionHandler;
            this.blockStoreManager = blockStoreManager;
            this.mempoolManager = mempoolManager;
            this.walletSyncManager = walletSyncManager;
        }

        /// <inheritdoc />
        public void Save()
        {
            File.WriteAllText(GetStateFilePath(), JsonConvert.SerializeObject(this));
        }

        /// <inheritdoc />
        public void LoadStateFromMemory()
        {
            var stateFilePath = GetStateFilePath();
            if (!File.Exists(stateFilePath))
            {
                return;
            }

            // load the file from the local system
            var savedState = JsonConvert.DeserializeObject<TumblingState>(File.ReadAllText(stateFilePath));
            
            this.OriginWalletName = savedState.OriginWalletName;
            this.DestinationWalletName = savedState.DestinationWalletName;
            this.LastBlockReceivedHeight = savedState.LastBlockReceivedHeight;
            this.TumblerParameters = savedState.TumblerParameters;
            this.TumblerUri = savedState.TumblerUri;
            this.TumblerNetwork = savedState.TumblerNetwork;
        }

        /// <inheritdoc />
        public void Delete()
        {
            var stateFilePath = GetStateFilePath();
            File.Delete(stateFilePath);
        }
        
        /// <summary>
        /// Gets the file path of the file containing the state of the tumbling execution.
        /// </summary>
        /// <returns></returns>
        private static string GetStateFilePath()
        {
            string defaultFolderPath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                defaultFolderPath = $@"{Environment.GetEnvironmentVariable("AppData")}\Breeze\TumbleBit";
            }
            else
            {
                defaultFolderPath = $"{Environment.GetEnvironmentVariable("HOME")}/.breeze/TumbleBit";
            }

            // create the directory if it doesn't exist
            Directory.CreateDirectory(defaultFolderPath);
            return Path.Combine(defaultFolderPath, StateFileName);
        }
    }
}
