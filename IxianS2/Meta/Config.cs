﻿using Fclp;
using IXICore;
using IXICore.Meta;
using IXICore.Network;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace S2.Meta
{
    public class Config
    {
        // Providing pre-defined values
        // Can be read from a file later, or read from the command line
        public static int serverPort = 10235;

        private static int defaultServerPort = 10235;
        private static int defaultTestnetServerPort = 11235;

        public static NetworkType networkType = NetworkType.main;

        public static int apiPort = 8001;
        public static int testnetApiPort = 8101;

        public static Dictionary<string, string> apiUsers = new Dictionary<string, string>();

        public static List<string> apiAllowedIps = new List<string>();
        public static List<string> apiBinds = new List<string>();

        public static string configFilename = "ixian.cfg";
        public static string walletFile = "ixian.wal";

        public static int maxLogSize = 50;
        public static int maxLogCount = 10;

        public static int logVerbosity = (int)LogSeverity.info + (int)LogSeverity.warn + (int)LogSeverity.error;

        public static bool disableWebStart = false;

        public static bool onlyShowAddresses = false;

        // Store the device id in a cache for reuse in later instances
        public static string externalIp = "";

        // Read-only values
        public static readonly string version = "xs2c-0.9.0c"; // S2 Node version

        public static readonly string checkVersionUrl = "https://resources.ixian.io/s2-update.txt";
        public static readonly int checkVersionSeconds = 6 * 60 * 60; // 6 hours

        public static readonly int maximumStreamClients = 1000; // Maximum number of stream clients this server can accept

        // Quotas
        public static readonly long lastPaidTimeQuota = 10 * 60; // Allow 10 minutes after payment before checking quotas
        public static readonly int infoMessageQuota = 10;  // Allow 10 info messages per 1 data message
        public static readonly int dataMessageQuota = 3; // Allow up to 3 data messages before receiving a transaction signature


        public static bool isTestClient = false;

        // Debugging values
        public static string networkDumpFile = "";

        // Development/testing options
        public static bool generateWalletOnly = false;
        public static string dangerCommandlinePasswordCleartextUnsafe = "";


        // internal
        public static bool changePass = false;

        /// <summary>
        /// Command to execute when a new block is accepted.
        /// </summary>
        public static string blockNotifyCommand = "";


        public static int maxRelaySectorNodesToRequest = 20;
        public static int maxRelaySectorNodesToConnectTo = 10;

        public static bool verboseOutput = false;

        public static byte[] checksumLock = null;

        private Config()
        {

        }

        private static string outputHelp()
        {
            S2.Program.noStart = true;

            Console.WriteLine("Starts a new instance of Ixian S2 Node");
            Console.WriteLine("");
            Console.WriteLine(" IxianS2.exe [-h] [-v] [-t] [-x] [-c] [-p 10234] [-a 8081] [-i ip] [-w ixian.wal] [-n seed1.ixian.io:10234]");
            Console.WriteLine(" [--config ixian.cfg] [--maxLogSize 50] [--maxLogCount 10]  [--logVerbosity 14] [--disableWebStart]");
            Console.WriteLine(" [--netdump] [--generateWallet] [--walletPassword] [--checksumLock Ixian] [--verboseOutput]");
            Console.WriteLine("");
            Console.WriteLine("    -h\t\t\t Displays this help");
            Console.WriteLine("    -v\t\t\t Displays version");
            Console.WriteLine("    -t\t\t\t Starts node in testnet mode");
            Console.WriteLine("    -x\t\t\t Change password of an existing wallet");
            Console.WriteLine("    -c\t\t\t Removes cache, peers.dat and ixian.log files before starting");
            Console.WriteLine("    -p\t\t\t Port to listen on");
            Console.WriteLine("    -a\t\t\t HTTP/API port to listen on");
            Console.WriteLine("    -i\t\t\t External IP Address to use");
            Console.WriteLine("    -w\t\t\t Specify location of the ixian.wal file");
            Console.WriteLine("    -n\t\t\t Specify which seed node to use");
            Console.WriteLine("    --config\t\t Specify config filename (default ixian.cfg)");
            Console.WriteLine("    --maxLogSize\t Specify maximum log file size in MB");
            Console.WriteLine("    --maxLogCount\t Specify maximum number of log files");
            Console.WriteLine("    --logVerbosity\t Sets log verbosity (0 = none, trace = 1, info = 2, warn = 4, error = 8)");
            Console.WriteLine("    --disableWebStart\t Disable running http://localhost:8081 on startup");
            Console.WriteLine("    --checksumLock\t\t Sets the checksum lock for seeding checksums - useful for custom networks.");
            Console.WriteLine("    --verboseOutput\t\t Starts node with verbose output.");
            Console.WriteLine("    --networkType\t\t mainnet, testnet or regtest.");
            Console.WriteLine("");
            Console.WriteLine("----------- Developer CLI flags -----------");
            Console.WriteLine("    --netdump\t\t Enable netdump for debugging purposes");
            Console.WriteLine("    --generateWallet\t Generates a wallet file and exits, printing the public address. [TESTNET ONLY!]");
            Console.WriteLine("    --walletPassword\t Specify the password for the wallet. [TESTNET ONLY!]");
            Console.WriteLine("");
            Console.WriteLine("----------- Config File Options -----------");
            Console.WriteLine(" Config file options should use parameterName = parameterValue semantics.");
            Console.WriteLine(" Each option should be specified in its own line. Example:");
            Console.WriteLine("    s2Port = 10234");
            Console.WriteLine("    apiPort = 8081");
            Console.WriteLine("");
            Console.WriteLine(" Available options:");
            Console.WriteLine("    s2Port\t\t Port to listen on (same as -p CLI)");
            Console.WriteLine("    testnetS2Port\t Port to listen on in testnet mode (same as -p CLI)");

            Console.WriteLine("    apiPort\t\t HTTP/API port to listen on (same as -a CLI)");
            Console.WriteLine("    apiAllowIp\t\t Allow API connections from specified source or sources (can be used multiple times)");
            Console.WriteLine("    apiBind\t\t Bind to given address to listen for API connections (can be used multiple times)");
            Console.WriteLine("    testnetApiPort\t HTTP/API port to listen on in testnet mode (same as -a CLI)");
            Console.WriteLine("    addApiUser\t\t Adds user:password that can access the API (can be used multiple times)");

            Console.WriteLine("    externalIp\t\t External IP Address to use (same as -i CLI)");
            Console.WriteLine("    addPeer\t\t Specify which seed node to use (same as -n CLI) (can be used multiple times)");
            Console.WriteLine("    addTestnetPeer\t Specify which seed node to use in testnet mode (same as -n CLI) (can be used multiple times)");
            Console.WriteLine("    maxLogSize\t\t Specify maximum log file size in MB (same as --maxLogSize CLI)");
            Console.WriteLine("    maxLogCount\t\t Specify maximum number of log files (same as --maxLogCount CLI)");
            Console.WriteLine("    logVerbosity\t Sets log verbosity (same as --logVerbosity CLI)");
            Console.WriteLine("    disableWebStart\t 1 to disable running http://localhost:8081 on startup (same as --disableWebStart CLI)");
            Console.WriteLine("    walletNotify\t Execute command when a wallet transaction changes");
            Console.WriteLine("    blockNotify\t Execute command when the block changes");

            return "";
        }

        private static string outputVersion()
        {
            S2.Program.noStart = true;

            // Do nothing since version is the first thing displayed

            return "";
        }

        private static NetworkType parseNetworkTypeValue(string value)
        {
            NetworkType netType;
            value = value.ToLower();
            switch (value)
            {
                case "mainnet":
                    netType = NetworkType.main;
                    break;
                case "testnet":
                    netType = NetworkType.test;
                    break;
                case "regtest":
                    netType = NetworkType.reg;
                    break;
                default:
                    throw new Exception(string.Format("Unknown network type '{0}'. Possible values are 'mainnet', 'testnet', 'regtest'", value));
            }
            return netType;
        }

        private static void readConfigFile(string filename)
        {
            if (!File.Exists(filename))
            {
                return;
            }
            Logging.info("Reading config file: " + filename);
            bool foundAddPeer = false;
            bool foundAddTestPeer = false;
            List<string> lines = File.ReadAllLines(filename).ToList();
            foreach (string line in lines)
            {
                string[] option = line.Split('=');
                if (option.Length < 2)
                {
                    continue;
                }
                string key = option[0].Trim(new char[] { ' ', '\t', '\r', '\n' });
                string value = option[1].Trim(new char[] { ' ', '\t', '\r', '\n' });

                if (key.StartsWith(";"))
                {
                    continue;
                }
                Logging.info("Processing config parameter '" + key + "' = '" + value + "'");
                switch (key)
                {
                    case "s2Port":
                        Config.defaultServerPort = int.Parse(value);
                        break;
                    case "testnetS2Port":
                        Config.defaultTestnetServerPort = int.Parse(value);
                        break;
                    case "apiPort":
                        apiPort = int.Parse(value);
                        break;
                    case "testnetApiPort":
                        testnetApiPort = int.Parse(value);
                        break;
                    case "apiAllowIp":
                        apiAllowedIps.Add(value);
                        break;
                    case "apiBind":
                        apiBinds.Add(value);
                        break;
                    case "addApiUser":
                        string[] credential = value.Split(':');
                        if (credential.Length == 2)
                        {
                            apiUsers.Add(credential[0], credential[1]);
                        }
                        break;
                    case "externalIp":
                        externalIp = value;
                        break;
                    case "addPeer":
                        if (!foundAddPeer)
                        {
                            NetworkUtils.seedNodes.Clear();
                        }
                        foundAddPeer = true;
                        NetworkUtils.seedNodes.Add(new string[2] { value, null });
                        break;
                    case "addTestnetPeer":
                        if (!foundAddTestPeer)
                        {
                            NetworkUtils.seedTestNetNodes.Clear();
                        }
                        foundAddTestPeer = true;
                        NetworkUtils.seedTestNetNodes.Add(new string[2] { value, null });
                        break;
                    case "maxLogSize":
                        maxLogSize = int.Parse(value);
                        break;
                    case "maxLogCount":
                        maxLogCount = int.Parse(value);
                        break;
                    case "disableWebStart":
                        if (int.Parse(value) != 0)
                        {
                            disableWebStart = true;
                        }
                        break;
                    case "walletNotify":
                        CoreConfig.walletNotifyCommand = value;
                        break;
                    case "blockNotify":
                        Config.blockNotifyCommand = value;
                        break;
                    case "logVerbosity":
                        logVerbosity = int.Parse(value);
                        break;
                    case "checksumLock":
                        checksumLock = Encoding.UTF8.GetBytes(value);
                        break;
                    case "networkType":
                        networkType = parseNetworkTypeValue(value);
                        break;
                    default:
                        // unknown key
                        Logging.warn("Unknown config parameter was specified '" + key + "'");
                        break;
                }
            }
        }
        public static void readFromCommandLine(string[] args)
        {
            // first pass
            var cmd_parser = new FluentCommandLineParser();

            // help
            cmd_parser.SetupHelp("h", "help").Callback(text => outputHelp());

            // config file
            cmd_parser.Setup<string>("config").Callback(value => configFilename = value).Required();

            cmd_parser.Parse(args);

            if (S2.Program.noStart)
            {
                return;
            }

            readConfigFile(configFilename);



            // second pass
            cmd_parser = new FluentCommandLineParser();

            // testnet
            cmd_parser.Setup<bool>('t', "testnet").Callback(value => networkType = NetworkType.test).Required();
            cmd_parser.Setup<string>("networkType").Callback(value => networkType = parseNetworkTypeValue(value)).Required();

            cmd_parser.Parse(args);

            if (networkType == NetworkType.test
                || networkType == NetworkType.reg)
            {
                serverPort = defaultTestnetServerPort;
                apiPort = testnetApiPort;
            }
            else
            {
                serverPort = defaultServerPort;
            }


            string seedNode = "";

            // third pass
            cmd_parser = new FluentCommandLineParser();

            bool start_clean = false; // Flag to determine if node should delete cache+logs

            // version
            cmd_parser.Setup<bool>('v', "version").Callback(text => outputVersion());

            // Check for password change
            cmd_parser.Setup<bool>('x', "changepass").Callback(value => changePass = value).Required();

            // Check for clean parameter
            cmd_parser.Setup<bool>('c', "clean").Callback(value => start_clean = value).Required();


            cmd_parser.Setup<int>('p', "port").Callback(value => Config.serverPort = value).Required();

            cmd_parser.Setup<int>('a', "apiport").Callback(value => apiPort = value).Required();

            cmd_parser.Setup<string>('i', "ip").Callback(value => externalIp = value).Required();

            cmd_parser.Setup<string>('w', "wallet").Callback(value => walletFile = value).Required();

            cmd_parser.Setup<string>('n', "node").Callback(value => seedNode = value).Required();

            cmd_parser.Setup<int>("maxLogSize").Callback(value => maxLogSize = value).Required();

            cmd_parser.Setup<int>("maxLogCount").Callback(value => maxLogCount = value).Required();

            cmd_parser.Setup<bool>("disableWebStart").Callback(value => disableWebStart = true).Required();

            cmd_parser.Setup<bool>("onlyShowAddresses").Callback(value => onlyShowAddresses = true).Required();

            cmd_parser.Setup<string>("checksumLock").Callback(value => checksumLock = Encoding.UTF8.GetBytes(value)).Required();

            // Debug
            cmd_parser.Setup<string>("netdump").Callback(value => networkDumpFile = value).SetDefault("");

            cmd_parser.Setup<bool>("generateWallet").Callback(value => generateWalletOnly = value).SetDefault(false);

            cmd_parser.Setup<string>("walletPassword").Callback(value => dangerCommandlinePasswordCleartextUnsafe = value).SetDefault("");

            cmd_parser.Setup<bool>("testClient").Callback(value => isTestClient = true).Required();

            cmd_parser.Setup<int>("logVerbosity").Callback(value => logVerbosity = value).Required();

            cmd_parser.Setup<bool>("verboseOutput").Callback(value => verboseOutput = value).SetDefault(false);

            cmd_parser.Parse(args);


            // Validate parameters

            if (start_clean)
            {
                Node.cleanCacheAndLogs();
            }

            if (seedNode != "")
            {
                switch (networkType)
                {
                    case NetworkType.main:
                        NetworkUtils.seedNodes = new List<string[]>
                            {
                                new string[2] { seedNode, null }
                            };
                        break;

                    case NetworkType.test:
                    case NetworkType.reg:
                        NetworkUtils.seedTestNetNodes = new List<string[]>
                            {
                                new string[2] { seedNode, null }
                            };
                        break;
                }
            }
        }

    }

}