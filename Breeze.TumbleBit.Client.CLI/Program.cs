using System;
using NBitcoin;
using NTumbleBit.Logging;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Api;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.LightWallet;
using Stratis.Bitcoin.Features.Notifications;
using Stratis.Bitcoin.Features.WatchOnlyWallet;
using Stratis.Bitcoin.Utilities;

namespace Breeze.TumbleBit.Client.CLI
{
    class Program
    {
        static void Main(string[] args)
        {
            string[] args2 = {
                "-testnet",
                "-ctb",
                //"ctb://7obtcd7mkosmxeuh.onion?h=03c632023c4a8587845ad918b8e5f53f7bf18319",
                "ctb://wljlc26vwmc65lk6.onion?h=0f802acf528ca7a0742c3074ba7daa35bdae98c9",
                "-origin",
                "xxxx",
                "-destination",
                "yyyy"
            };

	        FuncLoggerFactory loggerFactory =
		        new FuncLoggerFactory(i => new CustomerConsoleLogger(i, (a, b) => true, false));
			Logs.Configure(loggerFactory);

			ArgsReader argsReader = new ArgsReader(args2);
	        if (!argsReader.VerifyArgs()) return;

			//we don't want to be anywhere near MainNet
	        if (NodeSettings.PrintHelp(args, Network.Main))
		        return;

	        Uri tumblerUri = new Uri(argsReader.UriString);

			//Start the engines!
			NodeSettings nodeSettings = NodeSettings.FromArguments(args2);
			FullNode fullNode = StartupFullNode(nodeSettings, tumblerUri);

            fullNode.Run();

            // Not needed at all, invoke tumbler connection via the API
			//ITumbleBitManager tumbleBitManager = new TumbleBitManager(loggerFactory, fullNode.WalletManager, null, fullNode.Chain, Network.TestNet, fullNode.Signals);

			//use the tumblebitManager with a wait
	        //tumbleBitManager.ConnectToTumblerAsync(tumblerUri).GetAwaiter().GetResult();

			//we don't want to do this as it will lock up the console...
			//tumbleBitManager.TumbleAsync(argsReader.OriginWalletName, argsReader.DestinationWalletName).GetAwaiter().GetResult();
        }

	    private static FullNode StartupFullNode(NodeSettings nodeSettings, Uri tumblerUri)
	    {
            var fullNodeBuilder = new FullNodeBuilder()
                .UseNodeSettings(nodeSettings)
                .UseLightWallet()
                .UseBlockNotification()
                .UseTransactionNotification()
                .UseApi()
                .UseTumbleBit(tumblerUri)
                .UseWatchOnlyWallet()
                .Build();

            return fullNodeBuilder as FullNode;
        }
    }
}