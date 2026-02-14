using IXICore;
using IXICore.Meta;
using IXICore.Utils;
using S2.Meta;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace S2
{
    class Program
    {
        private static Thread mainLoopThread;

        private static Node node = null;

        private static bool running = false;

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

            // Start logging
            if (!Logging.start(Config.logFolderPath, Config.logVerbosity))
            {
                IxianHandler.forceShutdown = true;
                Logging.info("Press ENTER to exit.");
                Console.ReadLine();
                return;
            }

            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e) {
                ConsoleHelpers.verboseConsoleOutput = true;
                Logging.consoleOutput = ConsoleHelpers.verboseConsoleOutput;
                e.Cancel = true;
                IxianHandler.forceShutdown = true;
            };

            onStart(args);

            if (Node.apiServer != null)
            {
                while (IxianHandler.forceShutdown == false)
                {
                    Thread.Sleep(1000);
                }
            }
            onStop();

        }

        static void onStart(string[] args)
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
                return;
            }

            // Start the actual S2 node
            node.start(Config.verboseOutput);

            running = true;

            if (mainLoopThread != null)
            {
                mainLoopThread.Interrupt();
                mainLoopThread.Join();
                mainLoopThread = null;
            }

            mainLoopThread = new Thread(mainLoop);
            mainLoopThread.Name = "Main_Loop_Thread";
            mainLoopThread.Start();

            if (ConsoleHelpers.verboseConsoleOutput)
                Console.WriteLine("-----------\nPress Ctrl-C or use the /shutdown API to stop the S2 process at any time.\n");
        }

        static void mainLoop()
        {
            while (running)
            {
                try
                {
                    if (Node.update() == false)
                    {
                        IxianHandler.forceShutdown = true;
                    }
                    if (!Console.IsInputRedirected && Console.KeyAvailable)
                    {
                        ConsoleKeyInfo key = Console.ReadKey();

                        if (key.Key == ConsoleKey.V)
                        {
                            ConsoleHelpers.verboseConsoleOutput = !ConsoleHelpers.verboseConsoleOutput;
                            Logging.consoleOutput = ConsoleHelpers.verboseConsoleOutput;
                            Console.CursorVisible = ConsoleHelpers.verboseConsoleOutput;
                            if (ConsoleHelpers.verboseConsoleOutput == false)
                                Node.statsConsoleScreen.clearScreen();
                        }
                        else if (key.Key == ConsoleKey.Escape)
                        {
                            ConsoleHelpers.verboseConsoleOutput = true;
                            Logging.consoleOutput = ConsoleHelpers.verboseConsoleOutput;
                            IxianHandler.forceShutdown = true;
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
            running = false;

            // Stop the S2 node
            Node.stop();

            // Stop logging
            Logging.flush();
            Logging.stop();

            Console.WriteLine("");
            Console.WriteLine("Ixian S2 Node stopped.");
        }
    }
}
