using System;

using Breeze.TumbleBit.Client.Services;
using NBitcoin;
using NTumbleBit;
using NTumbleBit.Services;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.Wallet;

namespace Breeze.TumbleBit.Client
{
    public class ExternalServices
    {
        public static ExternalServices CreateUsingFullNode(IRepository repository, Tracker tracker, FullNode fullNode)
        {
            FeeRate minimumRate = new FeeRate(MempoolValidator.MinRelayTxFee.FeePerK);

            ExternalServices service = new ExternalServices();
            
            CoinType coinType;
            if (fullNode.Network == Network.Main)
                coinType = CoinType.Bitcoin;
            else if (fullNode.Network == Network.TestNet)
                coinType = CoinType.Testnet;
            else if (fullNode.Network == Network.StratisMain)
                coinType = CoinType.Stratis;
            else if (fullNode.Network == Network.StratisTest)
                coinType = CoinType.Testnet;
            else
                throw new Exception("Full node is on unrecognised network");
            
            service.FeeService = new FullNodeFeeService()
            {
                MinimumFeeRate = minimumRate
            };

            // on regtest the estimatefee always fails
            if (fullNode.Network == NBitcoin.Network.RegTest)
            {
                service.FeeService = new FullNodeFeeService()
                {
                    MinimumFeeRate = minimumRate,
                    FallBackFeeRate = new NBitcoin.FeeRate(NBitcoin.Money.Satoshis(50), 1)
                };
            }

            FullNodeWalletCache cache = new FullNodeWalletCache(repository, fullNode, coinType);
            service.WalletService = new FullNodeWalletService(fullNode.WalletManager, walletName, coinType, walletTransactionHandler, accountName);
            service.BroadcastService = new FullNodeBroadcastService(cache, repository, fullNode.WalletManager);
            service.BlockExplorerService = new FullNodeBlockExplorerService(cache, repository, fullNode.WalletManager, fullNode.Network);
            service.TrustedBroadcastService = new FullNodeTrustedBroadcastService(service.BroadcastService, service.BlockExplorerService, repository, cache, tracker, fullNode.Network)
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
