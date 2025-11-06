using IXICore;
using IXICore.Activity;
using IXICore.Inventory;
using IXICore.Meta;
using IXICore.Network;
using IXICore.RegNames;
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
        public static APIServer apiServer;

        public static StatsConsoleScreen statsConsoleScreen = null;

        public static TransactionInclusion tiv = null;

        // Private data
        private static Thread maintenanceThread;

        private static bool running = false;

        private bool generatedNewWallet = false;

        public static NetworkClientManagerStatic networkClientManagerStatic = null;
        public static NetworkClientManagerRandomized networkClientManagerRandomized = null;

        public static IActivityStorage activityStorage;

        public Node()
        {
            CoreConfig.device_id = [1];
            IxianHandler.enableNetworkServer = true;
            IxianHandler.init(Config.version, this, Config.networkType, true, Config.checksumLock);
            init();
        }

        // Perform basic initialization of node
        private void init()
        {
            running = true;

            CoreConfig.maximumServerMasterNodes = Config.maximumStreamClients;
            CoreConfig.maximumServerClients = Config.maximumStreamClients;

            UpdateVerify.init(Config.checkVersionUrl, Config.checkVersionSeconds);

            // Network configuration
            NetworkUtils.configureNetwork(Config.externalIp, Config.serverPort);

            // Load or Generate the wallet
            if (!initWallet())
            {
                running = false;
                S2.Program.noStart = true;
                return;
            }

            PeerStorage.init("");

            // Init TIV
            tiv = new TransactionInclusion(new S2TransactionInclusionCallbacks(), true);

            InventoryCache.init(new InventoryCacheS2(tiv));

            networkClientManagerRandomized = new NetworkClientManagerRandomized(Config.maxRelayMasterNodesToConnectTo);

            NetworkClientManager.init(networkClientManagerRandomized);

            networkClientManagerStatic = new NetworkClientManagerStatic(Config.maxRelaySectorNodesToConnectTo, false);

            RelaySectors.init(CoreConfig.relaySectorLevels, null);

            // Setup the stats console
            statsConsoleScreen = new StatsConsoleScreen();
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
                generatedNewWallet = true;
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
                IxianHandler.balances.Add(new Balance(addr, 0));
            }

            return true;
        }

        public void start(bool verboseConsoleOutput)
        {
            UpdateVerify.start();

            // Generate presence list
            PresenceList.init(IxianHandler.publicIP, Config.serverPort, 'R', CoreConfig.relayKeepAliveInterval);

            // Start the network queue
            NetworkQueue.start();

            activityStorage = new ActivityStorage(Path.Combine(Environment.CurrentDirectory, "activity"), 32 << 20, 0);
            activityStorage.prepareStorage(true);

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

            // Start the keepalive thread
            PresenceList.startKeepAlive();

            // Start TIV
            if (generatedNewWallet || !File.Exists(Config.walletFile))
            {
                generatedNewWallet = false;
                tiv.start("", false, false);
            }else
            {
                tiv.start("", 0, null, false, false);
            }

            // Start the maintenance thread
            maintenanceThread = new Thread(performMaintenance);
            maintenanceThread.Start();
        }

        static public bool update()
        {
            // Update the stream processor
            StreamProcessor.update();

            // Request initial wallet balance
            if (IxianHandler.balances.First().blockHeight == 0 || IxianHandler.balances.First().lastUpdate + 300 < Clock.getTimestamp())
            {
                using (MemoryStream mw = new MemoryStream())
                {
                    using (BinaryWriter writer = new BinaryWriter(mw))
                    {
                        writer.WriteIxiVarInt(IxianHandler.getWalletStorage().getPrimaryAddress().addressNoChecksum.Length);
                        writer.Write(IxianHandler.getWalletStorage().getPrimaryAddress().addressNoChecksum);
                        NetworkClientManager.broadcastData(new char[] { 'M', 'H' }, ProtocolMessageCode.getBalance2, mw.ToArray(), null);
                    }
                }

                CoreProtocolMessage.fetchSectorNodes(IxianHandler.primaryWalletAddress, Config.maxRelaySectorNodesToRequest);

                ProtocolMessage.clearOldData();
            }

            if (IxianHandler.status != NodeStatus.warmUp)
            {
                if (Clock.getTimestamp() - BlockHeaderStorage.lastBlockHeaderTime > 1800) // if no block for over 1800 seconds
                {
                    IxianHandler.status = NodeStatus.stalled;
                }
            }

            return running;
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

            Program.noStart = true;
            IxianHandler.forceShutdown = true;

            UpdateVerify.stop();

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

            if (maintenanceThread != null)
            {
                maintenanceThread.Interrupt();
                maintenanceThread.Join();
                maintenanceThread = null;
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

            // Stop the network server
            NetworkServer.stopNetworkOperations();

            IxianHandler.status = NodeStatus.stopped;

            Logging.info("Node stopped");

            // Stop the console stats screen
            // Console screen has a thread running even if we are in verbose mode
            statsConsoleScreen.stop();
        }

        // Cleans the storage cache and logs
        public static bool cleanCacheAndLogs()
        {
            activityStorage.stopStorage();
            activityStorage.deleteData();
            activityStorage.prepareStorage(false);

            PeerStorage.deletePeersFile();

            Logging.clear();

            Logging.info("Cleaned cache and logs.");
            return true;
        }

        // Perform periodic cleanup tasks
        private static void performMaintenance()
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

                        // Cleanup the presence list
                        PresenceList.performCleanup();
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

        public override bool isAcceptingConnections()
        {
            // TODO TODO TODO TODO implement this properly
            return true;
        }

        public override ulong getLastBlockHeight()
        {
            if (tiv.getLastBlockHeader() == null)
            {
                return 0;
            }
            return tiv.getLastBlockHeader().blockNum;
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
            if (tiv.getLastBlockHeader() == null
                || tiv.getLastBlockHeader().version < Block.maxVersion)
            {
                // TODO Omega force to v10 after upgrade
                return Block.maxVersion - 1;
            }
            return tiv.getLastBlockHeader().version;
        }

        public override bool addTransaction(Transaction tx, List<Address> relayNodeAddresses, bool force_broadcast)
        {
            return addTransaction(null, tx, relayNodeAddresses, force_broadcast);
        }

        public static bool addTransaction(Address senderAddress, Transaction tx, List<Address> relayNodeAddresses, bool force_broadcast)
        {
            CoreProtocolMessage.broadcastProtocolMessage(new char[] { 'M', 'H' }, ProtocolMessageCode.transactionData2, tx.getBytes(true, true), null);
            PendingTransactions.addPendingLocalTransaction(tx, null, null, senderAddress);
            return true;
        }

        public override Block getLastBlock()
        {
            return tiv.getLastBlockHeader();
        }

        public override Wallet getWallet(Address id)
        {
            foreach (Balance balance in IxianHandler.balances)
            {
                if (id.addressNoChecksum.SequenceEqual(balance.address.addressNoChecksum))
                    return new Wallet(id, balance.balance);
            }
            return new Wallet(id, 0);
        }

        public override IxiNumber getWalletBalance(Address id)
        {
            foreach (Balance balance in IxianHandler.balances)
            {
                if (id.addressNoChecksum.SequenceEqual(balance.address.addressNoChecksum))
                    return balance.balance;
            }
            return 0;
        }

        public override void shutdown()
        {
            IxianHandler.forceShutdown = true;
        }

        public override void parseProtocolMessage(ProtocolMessageCode code, byte[] data, RemoteEndpoint endpoint)
        {
            ProtocolMessage.parseProtocolMessage(code, data, endpoint);
        }

        public static void addTransactionToActivityStorage(Transaction transaction)
        {
            ActivityObject activity = null;
            ActivityType type;
            IxiNumber value = transaction.amount;
            Dictionary<byte[], List<byte[]>> wallet_list = null;
            Address wallet = null;
            Address primary_address = transaction.pubKey;

            ActivityStatus status = ActivityStatus.Pending;
            if (transaction.applied > 0)
            {
                status = ActivityStatus.Final;
            }

            if (IxianHandler.isMyAddress(primary_address))
            {
                // We are the sender
                wallet = primary_address;
                type = ActivityType.TransactionSent;
                if (transaction.type == (int)Transaction.Type.PoWSolution)
                {
                    type = ActivityType.MiningReward;
                    value = ConsensusConfig.calculateMiningRewardForBlock(transaction.powSolution.blockNum);
                }
                else if (transaction.type == (int)Transaction.Type.RegName)
                {
                    type = ActivityType.IxiName;
                }

                activity = new ActivityObject(IxianHandler.getWalletStorageBySecondaryAddress(primary_address).getSeedHash(),
                                wallet,
                                transaction.id,
                                transaction.toList.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.amount),
                                type,
                                null,
                                value,
                                transaction.timeStamp,
                                status,
                                transaction.applied);
                activityStorage.insertActivity(activity);
            }
            else
            {
                wallet_list = IxianHandler.extractMyAddressesFromAddressList(transaction.toList);
                if (wallet_list != null)
                {
                    // We are the recipient
                    type = ActivityType.TransactionReceived;
                    if (transaction.type == (int)Transaction.Type.StakingReward)
                    {
                        type = ActivityType.StakingReward;
                    }

                    foreach (var extractedWallet in wallet_list)
                    {
                        foreach (var addressBytes in extractedWallet.Value)
                        {
                            Address address = new Address(addressBytes);
                            activity = new ActivityObject(extractedWallet.Key,
                                                          address,
                                                          transaction.id,
                                                          transaction.fromList.ToDictionary(kvp => new Address(transaction.pubKey.addressNoChecksum, kvp.Key), kvp => kvp.Value),
                                                          type,
                                                          null,
                                                          transaction.toList[address].amount,
                                                          transaction.timeStamp,
                                                          status,
                                                          transaction.applied);
                            activityStorage.insertActivity(activity);
                        }
                    }
                }
            }
        }

        public static void processPendingTransactions()
        {
            // TODO TODO improve to include failed transactions
            ulong last_block_height = IxianHandler.getLastBlockHeight();
            lock (PendingTransactions.pendingTransactions)
            {
                long cur_time = Clock.getTimestamp();
                List<PendingTransaction> tmp_pending_transactions = new List<PendingTransaction>(PendingTransactions.pendingTransactions);
                int idx = 0;
                foreach (var entry in tmp_pending_transactions)
                {
                    Transaction t = entry.transaction;
                    long tx_time = entry.addedTimestamp;

                    if (t.applied != 0)
                    {
                        PendingTransactions.pendingTransactions.RemoveAll(x => x.transaction.id.SequenceEqual(t.id));
                        continue;
                    }

                    // if transaction expired, remove it from pending transactions
                    if (last_block_height > ConsensusConfig.getRedactedWindowSize() && t.blockHeight < last_block_height - ConsensusConfig.getRedactedWindowSize())
                    {
                        activityStorage.updateStatus(t.id, ActivityStatus.Error, 0);
                        PendingTransactions.pendingTransactions.RemoveAll(x => x.transaction.id.SequenceEqual(t.id));
                        continue;
                    }

                    if (cur_time - tx_time > 40) // if the transaction is pending for over 40 seconds, resend
                    {
                        CoreProtocolMessage.broadcastProtocolMessage(new char[] { 'M', 'H' }, ProtocolMessageCode.transactionData2, t.getBytes(true, true), null);
                        entry.addedTimestamp = cur_time;
                        entry.confirmedNodeList.Clear();
                    }

                    if (entry.confirmedNodeList.Count() > 3) // already received 3+ feedback
                    {
                        continue;
                    }

                    if (cur_time - tx_time > 20) // if the transaction is pending for over 20 seconds, send inquiry
                    {
                        CoreProtocolMessage.broadcastGetTransaction(t.id, 0, null, false);
                    }

                    idx++;
                }
            }
        }

        public override Block getBlockHeader(ulong blockNum)
        {
            return BlockHeaderStorage.getBlockHeader(blockNum);
        }

        public override IxiNumber getMinSignerPowDifficulty(ulong blockNum, int curBlockVersion, long curBlockTimestamp)
        {
            // TODO TODO implement this properly
            return ConsensusConfig.minBlockSignerPowDifficulty;
        }

        public override byte[] getBlockHash(ulong blockNum)
        {
            Block b = getBlockHeader(blockNum);
            if (b == null)
            {
                return null;
            }

            return b.blockChecksum;
        }

        public override RegisteredNameRecord getRegName(byte[] name, bool useAbsoluteId)
        {
            throw new NotImplementedException();
        }
    }
}
