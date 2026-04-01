using IXICore;
using IXICore.Activity;
using IXICore.Inventory;
using IXICore.Meta;
using IXICore.Network;
using IXICore.RegNames;
using IXICore.Storage;
using IXICore.Streaming;
using IXICore.Utils;
using S2.Network;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace S2.Meta
{
    class Node : IxianNode
    {

        public static TransactionInclusion tiv = null;

        public static StreamProcessor streamProcessor = null;

        public static NetworkClientManagerStatic networkClientManagerStatic = null;
        public static NetworkClientManagerRandomized networkClientManagerRandomized = null;

        public static IActivityStorage activityStorage = null;

        public static IStorage storage = null;

        // Private data
        private static StatsConsoleScreen statsConsoleScreen;

        private static APIServer? apiServer = null;

        private static Thread? mainLoopThread = null;

        private static bool running = false;

        public Node()
        {
            CoreConfig.device_id = [1];
            IxianHandler.enableNetworkServer = true;
            init();
        }

        // Perform basic initialization of node
        private void init()
        {
            IxianHandler.init(Config.version, this, Config.networkType, true, Config.checksumLock);

            CoreConfig.maximumServerMasterNodes = Config.maximumStreamClients;
            CoreConfig.maximumServerClients = Config.maximumStreamClients;

            // Load or Generate the wallet
            if (!initWallet())
            {
                running = false;
                IxianHandler.forceShutdown = true;
                return;
            }

            // Network configuration
            NetworkUtils.configureNetwork(Config.externalIp, Config.serverPort);

            FriendList.init(Config.dataFolder, false);

            UpdateVerify.init(Config.checkVersionUrl, Config.checkVersionSeconds);

            // Initialize storage
            if (storage is null)
            {
                storage = new RocksDBStorage(Config.headersFolderPath, Config.blocksDbCacheSize, CoreConfig.maxBlockHeadersPerDatabase, 50, RocksDBOptimizations.Servers);
            }

            activityStorage = new ActivityStorage(Config.activityFolderPath, Config.activityDbCacheSize, 0, RocksDBOptimizations.Servers);

            PeerStorage.init(Config.dataFolder);

            // Prepare the stream processor
            streamProcessor = new StreamProcessor(new ICPendingMessageProcessor(Config.dataFolder, false), StreamCapabilities.Incoming);

            // Init TIV
            tiv = new TransactionInclusion(storage, new S2TransactionInclusionCallbacks(), TIVBlockVerificationMode.Transactions);

            Logging.info("Initing local storage");

            // Prepare the local storage
            IxianHandler.localStorage = new LocalStorage(Config.dataFolder, new ICLocalStorageCallbacks());

            InventoryCache.init(new InventoryCacheS2(tiv));

            networkClientManagerRandomized = new NetworkClientManagerRandomized(Config.maxRelayMasterNodesToConnectTo);

            NetworkClientManager.init(networkClientManagerRandomized);

            networkClientManagerStatic = new NetworkClientManagerStatic(Config.maxRelaySectorNodesToConnectTo, false);

            RelaySectors.init(CoreConfig.relaySectorLevels, null);

            // Setup the stats console
            statsConsoleScreen = new StatsConsoleScreen();

            Logging.info("Node init done");
        }

        private bool initWallet()
        {
            WalletStorage walletStorage = new WalletStorage(Config.walletFile);

            Logging.flush();

            if (!walletStorage.walletExists())
            {
                ConsoleHelpers.displayBackupText();

                // Request a password
                // NOTE: This can only be done in testnet to enable automatic testing!
                string password = "";
                if (Config.dangerCommandlinePasswordCleartextUnsafe != "")
                {
                    Logging.warn("TestNet detected and wallet password has been specified on the command line!");
                    password = Config.dangerCommandlinePasswordCleartextUnsafe;
                    // Also note that the commandline password still has to be >= 10 characters
                }
                while (password.Length < 10)
                {
                    Logging.flush();
                    password = ConsoleHelpers.requestNewPassword("Enter a password for your new wallet: ");
                    if (IxianHandler.forceShutdown)
                    {
                        return false;
                    }
                }
                walletStorage.generateWallet(password);
            }
            else
            {
                ConsoleHelpers.displayBackupText();

                bool success = false;
                while (!success)
                {

                    // NOTE: This is only permitted on the testnet for dev/testing purposes!
                    string password = "";
                    if (Config.dangerCommandlinePasswordCleartextUnsafe != "")
                    {
                        Logging.warn("Attempting to unlock the wallet with a password from commandline!");
                        password = Config.dangerCommandlinePasswordCleartextUnsafe;
                    }
                    if (password.Length < 10)
                    {
                        Logging.flush();
                        Console.Write("Enter wallet password: ");
                        password = ConsoleHelpers.getPasswordInput();
                    }
                    if (IxianHandler.forceShutdown)
                    {
                        return false;
                    }
                    if (walletStorage.readWallet(password))
                    {
                        success = true;
                    }
                }
            }


            if (walletStorage.getPrimaryPublicKey() == null)
            {
                return false;
            }

            // Wait for any pending log messages to be written
            Logging.flush();

            Console.WriteLine();
            Console.WriteLine("Your IXIAN addresses are: ");
            Console.ForegroundColor = ConsoleColor.Green;
            foreach (var entry in walletStorage.getMyAddressesBase58())
            {
                Console.WriteLine(entry);
            }
            Console.ResetColor();
            Console.WriteLine();

            if (Config.onlyShowAddresses)
            {
                return false;
            }

            // Check if we should change the password of the wallet
            if (Config.changePass == true)
            {
                // Request a new password
                string new_password = "";
                while (new_password.Length < 10)
                {
                    new_password = ConsoleHelpers.requestNewPassword("Enter a new password for your wallet: ");
                    if (IxianHandler.forceShutdown)
                    {
                        return false;
                    }
                }
                walletStorage.writeWallet(new_password);
            }

            Logging.info("Public Node Address: {0}", walletStorage.getPrimaryAddress().ToString());


            if (walletStorage.viewingWallet)
            {
                Logging.error("Viewing-only wallet {0} cannot be used as the primary DLT Node wallet.", walletStorage.getPrimaryAddress().ToString());
                return false;
            }

            IxianHandler.addWallet(walletStorage);

            // Prepare the balances list
            List<Address> address_list = IxianHandler.getWalletStorage().getMyAddresses();
            foreach (Address addr in address_list)
            {
                IxianHandler.balances.Add(addr, new Balance(addr, 0));
            }

            return true;
        }

        public void start(bool verboseConsoleOutput)
        {
            if (running)
            {
                return;
            }
            Logging.info("Starting node");

            running = true;

            // Start local storage
            IxianHandler.localStorage.start();

            FriendList.loadContacts();

            UpdateVerify.start();

            // Generate presence list
            PresenceList.init(IxianHandler.publicIP, Config.serverPort, 'R', CoreConfig.relayKeepAliveInterval);

            // Start the network queue
            NetworkQueue.start();

            streamProcessor.start();

            if (!storage.prepareStorage(false))
            {
                Logging.error("Error while preparing block storage! Aborting.");
                IxianHandler.forceShutdown = true;
                return;
            }

            activityStorage.prepareStorage(true);

            var pending_txs = activityStorage.getActivitiesByStatus(ActivityStatus.Pending, true);
            pending_txs.AddRange(activityStorage.getActivitiesByStatus(ActivityStatus.Reverted, true));
            // Load pending transactions
            foreach (var pending_tx in pending_txs)
            {
                if (pending_tx.type == ActivityType.TransactionReceived
                    || pending_tx.type == ActivityType.TransactionSent
                    || pending_tx.type == ActivityType.IxiName)
                {
                    PendingTransactions.addOutgoingTransaction(pending_tx.transaction, null);
                }
            }

            if (Config.apiBinds.Count == 0)
            {
                Config.apiBinds.Add("http://localhost:" + Config.apiPort + "/");
            }

            // Start the HTTP JSON API server
            apiServer = new APIServer(Config.apiBinds, Config.apiUsers, Config.apiAllowedIps, activityStorage);

            if (IXICore.Platform.onWindows() == true && !Config.disableWebStart)
            {
                Process.Start(new ProcessStartInfo(Config.apiBinds[0]) { UseShellExecute = true });
            }

            // Prepare stats screen
            ConsoleHelpers.verboseConsoleOutput = verboseConsoleOutput;
            Logging.consoleOutput = verboseConsoleOutput;
            Logging.flush();
            if (ConsoleHelpers.verboseConsoleOutput == false)
            {
                statsConsoleScreen.clearScreen();
            }

            // Check for test client mode
            if (Config.isTestClient)
            {
                TestClientNode.start();
                return;
            }

            // Start the node stream server
            NetworkServer.beginNetworkOperations();

            // Start the network client manager
            NetworkClientManager.start(1);
            networkClientManagerStatic.start(0);

            // Start the s2 client manager
            StreamClientManager.start(Config.maxConnectedStreamingNodes, false, false);

            // Start the keepalive thread
            PresenceList.startKeepAlive();

            // Start TIV
            tiv.start(0, null, false);

            // Start the maintenance thread
            mainLoopThread = new Thread(mainLoop);
            mainLoopThread.Name = "Main_Loop_Thread";
            mainLoopThread.Start();
        }

        static public void stop()
        {
            if (!running)
            {
                Logging.stop();
                IxianHandler.status = NodeStatus.stopped;
                return;
            }

            Logging.info("Stopping node...");
            running = false;

            IxianHandler.forceShutdown = true;

            UpdateVerify.stop();

            // Stop the stream processor
            streamProcessor.stop();

            IxianHandler.localStorage.stop();

            // Stop TIV
            tiv.stop();

            // Stop the keepalive thread
            PresenceList.stopKeepAlive();

            // Stop the API server
            if (apiServer != null)
            {
                apiServer.stop();
                apiServer = null;
            }

            if (mainLoopThread != null)
            {
                mainLoopThread.Interrupt();
                mainLoopThread.Join();
                mainLoopThread = null;
            }

            activityStorage.stopStorage();

            // Stop the network queue
            NetworkQueue.stop();

            // Check for test client mode
            if (Config.isTestClient)
            {
                TestClientNode.stop();
                return;
            }

            // Stop all network clients
            networkClientManagerStatic.stop();
            NetworkClientManager.stop();
            StreamClientManager.stop();

            // Stop the network server
            NetworkServer.stopNetworkOperations();

            // Stop the block storage
            storage.stopStorage();

            IxianHandler.status = NodeStatus.stopped;

            Logging.info("Node stopped");

            // Stop the console stats screen
            // Console screen has a thread running even if we are in verbose mode
            statsConsoleScreen.stop();
        }

        // Cleans the storage cache and logs
        public static bool cleanCacheAndLogs()
        {
            if (activityStorage is null)
            {
                activityStorage = new ActivityStorage(Config.activityFolderPath, Config.activityDbCacheSize, 0, RocksDBOptimizations.Servers);
            }
            activityStorage.stopStorage();
            activityStorage.deleteData();
            activityStorage.prepareStorage(false);

            if (storage is null)
            {
                storage = new RocksDBStorage(Config.headersFolderPath, Config.blocksDbCacheSize, CoreConfig.maxBlockHeadersPerDatabase, 50, RocksDBOptimizations.Servers);
            }
            storage.stopStorage();
            storage.deleteData();
            storage.prepareStorage(false);

            PeerStorage.deletePeersFile();

            Logging.clear();

            Logging.info("Cleaned cache and logs.");
            return true;
        }

        // Perform periodic cleanup tasks
        private static void mainLoop()
        {
            try
            {
                while (running)
                {
                    // Sleep a while to prevent cpu usage
                    Thread.Sleep(2500);

                    try
                    {
                        PeerStorage.savePeersFile();
                        // Update the friendlist
                        updateFriendStatuses();

                        // Cleanup the presence list
                        PresenceList.performCleanup();

                        // Request initial wallet balance
                        bool firstBalance = true;
                        foreach (var balance in IxianHandler.balances.Values)
                        {
                            // Request initial wallet balance
                            if (balance.blockHeight == 0 || balance.lastUpdate + 300 < Clock.getTimestamp())
                            {
                                CoreProtocolMessage.broadcastProtocolMessage(['M', 'H'], ProtocolMessageCode.getBalance2, balance.address.addressNoChecksum.GetIxiBytes(), null);

                                if (firstBalance)
                                {
                                    CoreProtocolMessage.fetchSectorNodes(IxianHandler.primaryWalletAddress, Config.maxRelaySectorNodesToRequest);

                                    ProtocolMessage.clearOldData();
                                }
                            }
                            firstBalance = false;
                        }

                        if (IxianHandler.status != NodeStatus.warmUp)
                        {
                            if (Clock.getTimestamp() - IxianHandler.getLastBlock().timestamp > 1800) // if no block for over 1800 seconds
                            {
                                IxianHandler.status = NodeStatus.stalled;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Logging.error("Exception in performMaintenance " + e);
                    }
                }
            }
            catch (ThreadInterruptedException)
            {
            }
        }

        static public void updateFriendStatuses()
        {
            lock (FriendList.friends)
            {
                // Go through each friend and check for the pubkey in the PL
                foreach (Friend friend in FriendList.friends)
                {
                    Presence? presence = null;

                    try
                    {
                        presence = PresenceList.getPresenceByAddress(friend.walletAddress);
                    }
                    catch (Exception e)
                    {
                        Logging.error("Presence Error {0}", e.Message);
                        presence = null;
                    }

                    if (presence != null)
                    {
                        if (friend.online == false
                            && friend.relayNode != null)
                        {
                            friend.online = true;
                        }
                    }
                    else
                    {
                        if (friend.online == true
                            && Clock.getNetworkTimestamp() - friend.updatedStreamingNodes > CoreConfig.requestPresenceTimeout)
                        {
                            friend.online = false;
                        }
                    }
                }
            }
        }

        public override bool isAcceptingConnections()
        {
            // TODO TODO TODO TODO implement this properly
            return true;
        }

        public override ulong getLastBlockHeight()
        {
            Block? block = tiv.getLastBlockHeader();
            if (block == null)
            {
                return 0;
            }
            return block.blockNum;
        }

        public override ulong getHighestKnownNetworkBlockHeight()
        {
            ulong bh = getLastBlockHeight();
            ulong netBlockNum = CoreProtocolMessage.determineHighestNetworkBlockNum();
            if (bh < netBlockNum)
            {
                bh = netBlockNum;
            }

            return bh;
        }

        public override int getLastBlockVersion()
        {
            Block? block = tiv.getLastBlockHeader();
            if (block == null
                || block.version < Block.maxVersion)
            {
                // TODO Omega force to v10 after upgrade
                return Block.maxVersion - 1;
            }
            return block.version;
        }

        public override bool addIncomingTransaction(Transaction tx)
        {
            if (tx.timeStamp == 0)
            {
                tx.timeStamp = Clock.getTimestamp();
            }
            if (IxianHandler.addTransactionToActivityStorage(activityStorage, tx))
            {
                return PendingTransactions.addIncomingTransaction(tx);
            }
            return false;
        }

        public override bool addTransaction(Transaction tx, List<Address> relayNodeAddresses, List<ExtendedAddress>? extendedAddresses, byte[]? requestId, bool force_broadcast)
        {
            return addTransaction(null, tx, relayNodeAddresses, extendedAddresses, requestId, force_broadcast);
        }

        public static bool addTransaction(Address? senderAddress, Transaction tx, List<Address> relayNodeAddresses, List<ExtendedAddress>? extendedAddresses, byte[]? requestId, bool force_broadcast)
        {
            if (tx.timeStamp == 0)
            {
                tx.timeStamp = Clock.getTimestamp();
            }
            if (IxianHandler.addTransactionToActivityStorage(activityStorage, tx))
            {
                if (PendingTransactions.addOutgoingTransaction(tx, null, senderAddress))
                {
                    CoreProtocolMessage.broadcastProtocolMessage(new char[] { 'M', 'H' }, ProtocolMessageCode.transactionData2, tx.getBytes(true, true), null);

                    if (extendedAddresses != null)
                    {
                        CoreStreamProcessor.transactionSend(tx, extendedAddresses, requestId);
                    }
                    return true;
                }
            }
            return false;
        }

        public override Block? getLastBlock()
        {
            return tiv.getLastBlockHeader();
        }

        public override void shutdown()
        {
            IxianHandler.forceShutdown = true;
        }

        public override void parseProtocolMessage(ProtocolMessageCode code, byte[] data, RemoteEndpoint endpoint)
        {
            ProtocolMessage.parseProtocolMessage(code, data, endpoint);
        }

        public override Block? getBlockHeader(ulong blockNum)
        {
            return storage.getBlock(blockNum);
        }

        public override IxiNumber getMinSignerPowDifficulty(ulong blockNum, int curBlockVersion, long curBlockTimestamp)
        {
            return tiv.getMinSignerPowDifficulty(blockNum, curBlockVersion, curBlockTimestamp);
        }

        public override byte[]? getBlockHash(ulong blockNum)
        {
            var tsd = storage.getBlockTotalSignerDifficulty(blockNum);
            return tsd.blockHash;
        }

        public override RegisteredNameRecord getRegName(byte[] name, bool useAbsoluteId)
        {
            throw new NotImplementedException();
        }
    }
}
