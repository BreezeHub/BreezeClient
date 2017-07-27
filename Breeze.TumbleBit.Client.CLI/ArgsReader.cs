using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;

namespace Breeze.TumbleBit.Client.CLI
{
	/// <summary>
	/// ArgsReader gets parameters from the 
	/// </summary>
    internal sealed class ArgsReader
	{
		public string UriString { get; } = String.Empty;
	    public string OriginWalletName { get; } = String.Empty;
		public string DestinationWalletName { get; } = String.Empty;

		public ArgsReader(string[] args)
		{
			this.UriString = this.GetArgument(args, "-ctb");
			this.OriginWalletName = this.GetArgument(args, "-origin");
			this.DestinationWalletName = this.GetArgument(args, "-destination");
		}

	    public bool VerifyArgs()
	    {
		    if (this.UriString == "xxxx")
			    throw new Exception("If you are debugging set command line arguments in Project properties in the Debug Tab.");
		    
			if (!this.UriString.StartsWith("ctb"))
		    {
			    Console.WriteLine("The -ctb parameter is required and needs to be a ctb uri.");
			    return false;
		    }

		    if(this.OriginWalletName == string.Empty|this.DestinationWalletName == string.Empty) {
			    Console.WriteLine("Both the -origin and -destination parameters are required.");
			    return false;
		    }
		    return true;
	    }

		string GetArgument(IEnumerable<string> args, string option)
		    => args.SkipWhile(i => i != option).Skip(1).Take(1).FirstOrDefault();
	}
}
