using System;

using Breeze.TumbleBit.Client.Services;
using NBitcoin;
using NTumbleBit;
using NTumbleBit.Services;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.WatchOnlyWallet;

namespace Breeze.TumbleBit.Client
{
    public class ExternalServices
    {
        public static ExternalServices CreateUsingFullNode(IRepository repository, Tracker tracker, FullNode fullNode)
        {
            FeeRate minimumRate = new FeeRate(MempoolValidator.MinRelayTxFee.FeePerK);

            ExternalServices service = new ExternalServices();
                      
            service.FeeService = new FullNodeFeeService()
            {
                MinimumFeeRate = minimumRate
            };

            // on regtest the estimatefee always fails
            if (fullNode.Network == Network.RegTest)
            {
                service.FeeService = new FullNodeFeeService()
                {
                    MinimumFeeRate = minimumRate,
                    FallBackFeeRate = new FeeRate(Money.Satoshis(50), 1)
                };
            }

            WatchOnlyWalletManager watchOnlyWalletManager = new WatchOnlyWalletManager(fullNode.Settings.LoggerFactory, fullNode.ConnectionManager, fullNode.Network, fullNode.Chain, fullNode.Settings, fullNode.DataFolder);

            // TODO: Does the watch-only wallet need to be saved properly for shutdown?
            watchOnlyWalletManager.Initialize();

            FullNodeWalletCache cache = new FullNodeWalletCache(repository, fullNode, watchOnlyWalletManager);
            service.WalletService = new FullNodeWalletService(fullNode, walletName, accountName);
            service.BroadcastService = new FullNodeBroadcastService(cache, repository, fullNode, watchOnlyWalletManager);
            service.BlockExplorerService = new FullNodeBlockExplorerService(cache, repository, fullNode, watchOnlyWalletManager);
            service.TrustedBroadcastService = new FullNodeTrustedBroadcastService(service.BroadcastService, service.BlockExplorerService, repository, cache, tracker, fullNode, watchOnlyWalletManager)
            {
                // BlockExplorer will already track the addresses, since they used a shared bitcoind, no need of tracking again (this would overwrite labels)
                TrackPreviousScriptPubKey = false
            };
            return service;
        }

        public IFeeService FeeService
        {
            get; set;
        }
        public IWalletService WalletService
        {
            get; set;
        }
        public IBroadcastService BroadcastService
        {
            get; set;
        }
        public IBlockExplorerService BlockExplorerService
        {
            get; set;
        }
        public ITrustedBroadcastService TrustedBroadcastService
        {
            get; set;
        }
    }
}
