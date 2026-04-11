using IXICore;
using IXICore.Meta;
using IXICore.Utils;
using S2.Meta;
using System;
using System.IO;
using System.Threading;

namespace S2
{
    class Program
    {
        private static Node? node = null;

        static void Main(string[] args)
        {
            if (!Console.IsOutputRedirected)
            {
                // There are probably more problematic Console operations if we're working in stdout redirected mode, but 
                // this one is blocking automated testing.
                Console.Clear();
            }

            ConsoleHelpers.prepareWindowsConsole();

            ConsoleHelpers.verboseConsoleOutput = true;

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine(string.Format("IXIAN S2 {0} ({1})", Config.version, CoreConfig.version));
            Console.ResetColor();

            // Read configuration from command line
            Config.init(args);

            if (!Directory.Exists(Config.dataFolder))
            {
                Directory.CreateDirectory(Config.dataFolder);
            }

            // Start logging
            if (!Logging.start(Config.logFolderPath, Config.logVerbosity))
            {
                Logging.info("Press ENTER to exit.");
                Console.ReadLine();
                return;
            }

            Console.CancelKeyPress += delegate (object? sender, ConsoleCancelEventArgs e) {
                ConsoleHelpers.verboseConsoleOutput = true;
                Logging.consoleOutput = ConsoleHelpers.verboseConsoleOutput;
                e.Cancel = true;
                IxianHandler.requestShutdown();
            };

            if (onStart(args))
            {
                mainLoop();
            }

            onStop();

        }

        static bool onStart(string[] args)
        {
            // Set the logging options
            Logging.setOptions(Config.maxLogSize, Config.maxLogCount);
            Logging.flush();

            Logging.info("Starting IXIAN S2 {0} ({1})", Config.version, CoreConfig.version);

            // Log the parameters to notice any changes
            Logging.info("Network: {0}", Config.networkType);
            Logging.info("Server Port: {0}", Config.serverPort);
            Logging.info("API Port: {0}", Config.apiPort);
            Logging.info("Wallet File: {0}", Config.walletFile);

            // Initialize the node
            node = new Node();

            if (IxianHandler.forceShutdown)
            {
                Thread.Sleep(1000);
                return false;
            }

            // Start the actual S2 node
            if (!node.start(Config.verboseOutput))
            {
                Thread.Sleep(1000);
                return false;
            }

            if (ConsoleHelpers.verboseConsoleOutput)
                Console.WriteLine("-----------\nPress Ctrl-C or use the /shutdown API to stop the S2 process at any time.\n");

            return true;
        }

        static void mainLoop()
        {
            while (!IxianHandler.forceShutdown)
            {
                try
                {
                    if (!Console.IsInputRedirected && Console.KeyAvailable)
                    {
                        ConsoleKeyInfo key = Console.ReadKey();

                        if (key.Key == ConsoleKey.V)
                        {
                            ConsoleHelpers.verboseConsoleOutput = !ConsoleHelpers.verboseConsoleOutput;
                            Logging.consoleOutput = ConsoleHelpers.verboseConsoleOutput;
                            Console.CursorVisible = ConsoleHelpers.verboseConsoleOutput;
                            Console.Clear();
                        }
                        else if (key.Key == ConsoleKey.Escape)
                        {
                            ConsoleHelpers.verboseConsoleOutput = true;
                            Logging.consoleOutput = ConsoleHelpers.verboseConsoleOutput;
                            IxianHandler.requestShutdown();
                        }

                    }
                }
                catch (Exception e)
                {
                    Logging.error("Exception occured in mainLoop: " + e);
                }
                Thread.Sleep(1000);
            }
        }

        static void onStop()
        {
            // Stop the S2 node
            IxianHandler.shutdown();

            // Stop logging
            Logging.stop();

            Console.WriteLine("");
            Console.WriteLine("Ixian S2 Node stopped.");
        }
    }
}
