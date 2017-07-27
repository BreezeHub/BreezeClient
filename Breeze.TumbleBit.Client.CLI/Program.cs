using System;
using NBitcoin;
using NTumbleBit.Logging;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Configuration;

namespace Breeze.TumbleBit.Client.CLI
{
    class Program
    {
        static void Main(string[] args)
        {
	        FuncLoggerFactory loggerFactory =
		        new FuncLoggerFactory(i => new CustomerConsoleLogger(i, (a, b) => true, false));
			Logs.Configure(loggerFactory);

			ArgsReader argsReader = new ArgsReader(args);
	        if (!argsReader.VerifyArgs()) return;

			//we don't want to be anywhere near MainNet
	        if (NodeSettings.PrintHelp(args, Network.Main))
		        return;

	        Uri tumblerUri = new Uri(argsReader.UriString);

			//Start the engines!
			NodeSettings nodeSettings = NodeSettings.FromArguments(args);
			FullNode fullNode = StartupFullNode(nodeSettings, tumblerUri);

			ITumbleBitManager tumbleBitManager;// = new TumbleBitManager(loggerFactory, );

			//use the tumblebitManager with a wait
	        //tumbleBitManager.ConnectToTumblerAsync(tumblerUri).GetAwaiter().GetResult();

			//we don't want to do this as it will lock up the console...
			//tumbleBitManager.TumbleAsync(argsReader.OriginWalletName, argsReader.DestinationWalletName).GetAwaiter().GetResult();
        }

	    private static FullNode StartupFullNode(NodeSettings nodeSettings, Uri tumblerUri)
	    {
		    //var fullNodeBuilder = new FullNodeBuilder()
			   // .UseNodeSettings(nodeSettings)
			   // .UseLightWallet()
			   // .UseBlockNotification()
			   // .UseTransactionNotification()
			   // .UseApi()
			   // .UseTumbleBit(tumblerUri)
			   // .UseWatchOnlyWallet()
			   // .Build();

		    //return fullNodeBuilder;
		    return null;
	    }
    }
}