using IXICore;
using IXICore.Inventory;
using IXICore.Meta;
using IXICore.Network;
using IXICore.Network.Messages;
using IXICore.RegNames;
using IXICore.Utils;
using S2.Meta;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using static IXICore.Transaction;

namespace S2.Network
{
    public class ProtocolMessage
    {
        static Dictionary<ProtocolMessageCode, Dictionary<byte[], (long timestamp, List<RemoteEndpoint> endpoints)>> pendingRequests = new() { { ProtocolMessageCode.getBalance2, new(new ByteArrayComparer()) },
                                                                                                                                               { ProtocolMessageCode.getSectorNodes, new(new ByteArrayComparer()) }};

        static Dictionary<byte[], long> cachedSectors = new(new ByteArrayComparer());
        static Dictionary<byte[], (long timestamp, List<RegisteredNameDataRecord> nameRecords)> cachedNames = new(new ByteArrayComparer());

        public static void clearOldData()
        {
            clearOldCachedSectors();
            clearOldPendingRequests();
        }

        public static void clearOldPendingRequests()
        {
            foreach (var prType in pendingRequests)
            {
                Dictionary<byte[], (long timestamp, List<RemoteEndpoint> endpoint)> tmpPendingRequests = new(prType.Value, new ByteArrayComparer());
                foreach (var pending in tmpPendingRequests)
                {
                    if (Clock.getTimestamp() - pending.Value.timestamp > 30)
                    {
                        prType.Value.Remove(pending.Key);
                    }
                }
            }
        }

        public static void clearOldCachedSectors()
        {
            Dictionary<byte[], long> tmpCachedSectors = new(cachedSectors, new ByteArrayComparer());
            foreach (var cached in tmpCachedSectors)
            {
                if (Clock.getTimestamp() - cached.Value > 30)
                {
                    cachedSectors.Remove(cached.Key);
                }
            }
        }

        // Unified protocol message parsing
        public static void parseProtocolMessage(ProtocolMessageCode code, byte[] data, RemoteEndpoint endpoint)
        {
            if (endpoint == null)
            {
                Logging.error("Endpoint was null. parseProtocolMessage");
                return;
            }
            try
            {
                switch (code)
                {
                    case ProtocolMessageCode.hello:
                        handleHello(data, endpoint);
                        break;

                    case ProtocolMessageCode.helloData:
                        handleHelloData(data, endpoint);
                        break;

                    case ProtocolMessageCode.s2data:
                        StreamProcessor.receiveData(data, endpoint);
                        break;

                    case ProtocolMessageCode.s2failed:
                        Logging.error("Failed to send s2 data");
                        break;

                    case ProtocolMessageCode.s2signature:
                        StreamProcessor.receivedTransactionSignature(data, endpoint);
                        break;

                    case ProtocolMessageCode.transactionData2:
                        {
                            Transaction tx = new Transaction(data, true, true);

                            if (endpoint.presenceAddress.type == 'C')
                            {
                                ToEntry value;
                                if (tx.toList.TryGetValue(IxianHandler.primaryWalletAddress, out value)
                                    && value.amount >= tx.fee)
                                {
                                    IxianHandler.addTransaction(tx, null, true);
                                } else
                                {
                                    endpoint.sendData(ProtocolMessageCode.rejected, new Rejected(RejectedCode.TxInsufficientFee, tx.id).getBytes());
                                }
                            }
                            else
                            {
                                if (endpoint.presenceAddress.type == 'M' || endpoint.presenceAddress.type == 'H')
                                {
                                    PendingTransactions.increaseReceivedCount(tx.id, endpoint.presence.wallet);
                                }

                                Node.tiv.receivedNewTransaction(tx);
                                Logging.info("Received new transaction {0}", tx.id);

                                Node.addTransactionToActivityStorage(tx);
                            }
                        }
                        break;

                    case ProtocolMessageCode.updatePresence:
                        handlePresence(data, endpoint);
                        break;

                    case ProtocolMessageCode.keepAlivePresence:
                        handleKeepAlivePresence(data, endpoint);
                        break;

                    case ProtocolMessageCode.getPresence2:
                        handleGetPresence2(data, endpoint);
                        break;

                    case ProtocolMessageCode.balance2:
                        handleBalance2(data, endpoint);
                        break;

                    case ProtocolMessageCode.bye:
                        CoreProtocolMessage.processBye(data, endpoint);
                        break;

                    case ProtocolMessageCode.blockHeaders3:
                        // Forward the block headers to the TIV handler
                        Node.tiv.receivedBlockHeaders3(data, endpoint);
                        break;

                    case ProtocolMessageCode.pitData2:
                        Node.tiv.receivedPIT2(data, endpoint);
                        break;

                    case ProtocolMessageCode.rejected:
                        handleRejected(data, endpoint);
                        break;

                    case ProtocolMessageCode.inventory2:
                        handleInventory2(data, endpoint);
                        break;

                    //case ProtocolMessageCode.getStreamingNode:
                    //    handleGetStreamingNode(data, endpoint);
                    //    break;

                    case ProtocolMessageCode.getSectorNodes:
                        handleGetSectorNodes(data, endpoint);
                        break;

                    case ProtocolMessageCode.sectorNodes:
                        handleSectorNodes(data, endpoint);
                        break;

                    case ProtocolMessageCode.getBalance2:
                        handleGetBalance(data, endpoint);
                        break;

                    case ProtocolMessageCode.getNameRecord:
                        handleGetNameRecord(data, endpoint);
                        break;

                    case ProtocolMessageCode.nameRecord:
                        handleNameRecord(data, endpoint);
                        break;

                    default:
                        Logging.warn("Unknown protocol message: {0}, from {1} ({2})", code, endpoint.getFullAddress(), endpoint.serverWalletAddress);
                        break;
                }
            }
            catch (Exception e)
            {
                Logging.error("Error parsing network message. Details: {0}", e.ToString());
            }
        }

        private static void handlePresence(byte[] data, RemoteEndpoint endpoint)
        {
            // Parse the data and update entries in the presence list
            PresenceList.updateFromBytes(data, 0);
        }

        private static void sendKeepAlivePresenceToNeighbourSectorNodes(InventoryItemKeepAlive iika, RemoteEndpoint endpoint)
        {
            var sectorNodes = RelaySectors.Instance.getSectorNodes(IxianHandler.getWalletStorage().getPrimaryAddress().addressNoChecksum, Config.maxRelaySectorNodesToConnectTo);
            var thisNodeIndex = sectorNodes.FindIndex(x => x.addressNoChecksum.SequenceEqual(iika.address.addressNoChecksum));

            if (endpoint.presenceAddress.type != 'C')
            {
                return;
            }

            if (thisNodeIndex == -1)
            {
                return;
            }

            int startIndex = 0;
            if (thisNodeIndex >= 2)
            {
                startIndex = thisNodeIndex - 2;
            }

            int nodeCount = sectorNodes.Count;
            if (nodeCount - thisNodeIndex > 2)
            {
                nodeCount = thisNodeIndex + 2;
            }

            for (int i = startIndex; i < nodeCount; i++)
            {
                if (i == thisNodeIndex)
                {
                    continue;
                }

                var client = NetworkClientManager.getClient(sectorNodes[i]);
                if (client != null)
                {
                    client.addInventoryItem(iika);
                }
                else
                {
                    client = NetworkServer.getClient(sectorNodes[i]);
                    if (client != null)
                    {
                        client.addInventoryItem(iika);
                    }
                }
            }
        }

        private static void handleKeepAlivePresence(byte[] data, RemoteEndpoint endpoint)
        {
            byte[] hash = CryptoManager.lib.sha3_512sqTrunc(data);

            InventoryCache.Instance.setProcessedFlag(InventoryItemTypes.keepAlive, hash, true);

            Address address = null;
            long last_seen = 0;
            byte[] device_id = null;
            char node_type;
            bool updated = PresenceList.receiveKeepAlive(data, out address, out last_seen, out device_id, out node_type, endpoint);
            if (updated)
            {
                var iika = new InventoryItemKeepAlive(hash, last_seen, address, device_id);
                if (node_type == 'R')
                {
                    Node.networkClientManagerStatic.addToInventory(['R'], iika, endpoint);
                }
                else if (node_type == 'C')
                {
                    sendKeepAlivePresenceToNeighbourSectorNodes(iika, endpoint);
                }
            }
        }

        private static void addPendingRequest(ProtocolMessageCode code, byte[] key, RemoteEndpoint endpoint)
        {
            (long timestamp, List<RemoteEndpoint> endpoints) pendingRequest;
            if (pendingRequests[code].TryGetValue(key, out pendingRequest))
            {
                if (!pendingRequest.endpoints.Contains(endpoint))
                {
                    pendingRequest.endpoints.Add(endpoint);
                    pendingRequest.timestamp = Clock.getTimestamp();
                    pendingRequests[code].AddOrReplace(key, pendingRequest);
                }
            }
            else
            {
                pendingRequests[code].AddOrReplace(key, (Clock.getTimestamp(), new() { endpoint }));
            }
        }

        private static (long timestamp, List<RemoteEndpoint> endpoints) getAndRemovePendingRequest(ProtocolMessageCode code, byte[] key)
        {
            (long timestamp, List<RemoteEndpoint> endpoints) pendingRequest;
            pendingRequests[code].TryGetValue(key, out pendingRequest);
            if (pendingRequest != default)
            {
                pendingRequests[code].Remove(key);
            }
            return pendingRequest;
        }

        static void handleNameRecord(byte[] data, RemoteEndpoint endpoint)
        {
            int offset = 0;

            var nameAndOffset = data.ReadIxiBytes(offset);
            offset += nameAndOffset.bytesRead;
            byte[] name = nameAndOffset.bytes;

            var recordCountAndOffset = data.GetIxiVarUInt(offset);
            offset += recordCountAndOffset.bytesRead;
            int recordCount = (int)recordCountAndOffset.num;

            for (int i = 0; i < recordCount; i++)
            {
                var recordAndOffset = data.ReadIxiBytes(offset);
                offset += recordAndOffset.bytesRead;
            }

            // Forward name records to client
            var pendingRequest = getAndRemovePendingRequest(ProtocolMessageCode.getNameRecord, name);
            if (pendingRequest != default)
            {
                foreach (var prEndpoint in pendingRequest.endpoints)
                {
                    prEndpoint.sendData(ProtocolMessageCode.nameRecord, data, null, 0, MessagePriority.high);
                }
            }
        }

        public static void handleGetNameRecord(byte[] data, RemoteEndpoint endpoint)
        {
            int offset = 0;
            var name = data.ReadIxiBytes(offset);
            offset += name.bytesRead;

            (long timestamp, List<RegisteredNameDataRecord> nameRecords) cachedName;
            if (cachedNames.TryGetValue(name.bytes, out cachedName))
            {
                CoreProtocolMessage.sendRegisteredNameRecord(endpoint, name.bytes, cachedName.nameRecords);
            }
            else
            {
                addPendingRequest(ProtocolMessageCode.getNameRecord, name.bytes, endpoint);
                NetworkClientManager.broadcastData(['M', 'H'], ProtocolMessageCode.getNameRecord, data, null);
            }
        }

        public static void handleGetBalance(byte[] data, RemoteEndpoint endpoint)
        {
            NetworkClientManager.broadcastData(['M', 'H'], ProtocolMessageCode.getBalance2, data, null);
        }

        public static void handleGetSectorNodes(byte[] data, RemoteEndpoint endpoint)
        {
            int offset = 0;
            var addressWithOffset = data.ReadIxiBytes(offset);
            offset += addressWithOffset.bytesRead;

            var maxRelayCountWithOffset = data.GetIxiVarUInt(offset);
            offset += maxRelayCountWithOffset.bytesRead;
            int maxRelayCount = (int)maxRelayCountWithOffset.num;

            if (maxRelayCount > 20)
            {
                maxRelayCount = 20;
            }

            if (cachedSectors.ContainsKey(addressWithOffset.bytes))
            {
                var relayList = RelaySectors.Instance.getSectorNodes(addressWithOffset.bytes, maxRelayCount);

                CoreProtocolMessage.sendSectorNodes(addressWithOffset.bytes, relayList, endpoint);
            }
            else
            {
                addPendingRequest(ProtocolMessageCode.getSectorNodes, addressWithOffset.bytes, endpoint);
                NetworkClientManager.broadcastData(['M', 'H'], ProtocolMessageCode.getSectorNodes, data, null);
            }
        }

        static void handleSectorNodes(byte[] data, RemoteEndpoint endpoint)
        {
            int offset = 0;

            var prefixAndOffset = data.ReadIxiBytes(offset);
            offset += prefixAndOffset.bytesRead;
            byte[] prefix = prefixAndOffset.bytes;

            var nodeCountAndOffset = data.GetIxiVarUInt(offset);
            offset += nodeCountAndOffset.bytesRead;
            int nodeCount = (int)nodeCountAndOffset.num;

            for (int i = 0; i < nodeCount; i++)
            {
                var kaBytesAndOffset = data.ReadIxiBytes(offset);
                offset += kaBytesAndOffset.bytesRead;

                Presence p = PresenceList.updateFromBytes(kaBytesAndOffset.bytes, IxianHandler.getMinSignerPowDifficulty(IxianHandler.getLastBlockHeight() + 1, IxianHandler.getLastBlockVersion(), Clock.getNetworkTimestamp()));
                if (p != null)
                {
                    RelaySectors.Instance.addRelayNode(p.wallet);
                }
            }

            cachedSectors.AddOrReplace(prefix, Clock.getTimestamp());

            if (IxianHandler.isMyAddress(new Address(prefix)))
            {
                List<Peer> peers = new();
                var relays = RelaySectors.Instance.getSectorNodes(prefix, Config.maxRelaySectorNodesToConnectTo);
                foreach (var relay in relays)
                {
                    var p = PresenceList.getPresenceByAddress(relay);
                    if (p == null)
                    {
                        continue;
                    }
                    var pa = p.addresses.First();
                    peers.Add(new(pa.address, relay, pa.lastSeenTime, 0, 0, 0));
                }
                Node.networkClientManagerStatic.setClientsToConnectTo(peers);
            }
            else
            {
                // Forward sector nodes to client
                var pendingRequest = getAndRemovePendingRequest(ProtocolMessageCode.getSectorNodes, prefix);
                if (pendingRequest != default)
                {
                    var relays = RelaySectors.Instance.getSectorNodes(prefix, nodeCount);
                    foreach (var prEndpoint in pendingRequest.endpoints)
                    {
                        CoreProtocolMessage.sendSectorNodes(prefix, relays, prEndpoint);
                    }
                }
            }
        }

        static void handleRejected(byte[] data, RemoteEndpoint endpoint)
        {
            try
            {
                Rejected rej = new Rejected(data);
                switch (rej.code)
                {
                    case RejectedCode.TxInvalid:
                    case RejectedCode.TxInsufficientFee:
                    case RejectedCode.TxDust:
                        Logging.error("Received 'rejected' message {0} {1}", rej.code, Crypto.hashToString(rej.data));
                        // remove tx from pending transactions
                        // notify client who sent this transaction to us
                        throw new NotImplementedException();
                        break;

                    case RejectedCode.TxDuplicate:
                        Logging.warn("Received 'rejected' message {0} {1}", rej.code, Crypto.hashToString(rej.data));
                        // All good, do nothing
                        throw new NotImplementedException();
                        break;

                    default:
                        Logging.error("Received 'rejected' message with unknown code {0} {1}", rej.code, Crypto.hashToString(rej.data));
                        break;
                }
            }
            catch (Exception e)
            {
                throw new Exception(string.Format("Exception occured while processing 'rejected' message with code {0} {1}", data[0], Crypto.hashToString(data)), e);
            }
        }

        static void handleHello(byte[] data, RemoteEndpoint endpoint)
        {
            using (MemoryStream m = new MemoryStream(data))
            {
                using (BinaryReader reader = new BinaryReader(m))
                {
                    CoreProtocolMessage.processHelloMessageV6(endpoint, reader);
                }
            }
        }

        static void handleHelloData(byte[] data, RemoteEndpoint endpoint)
        {
            using (MemoryStream m = new MemoryStream(data))
            {
                using (BinaryReader reader = new BinaryReader(m))
                {
                    if (CoreProtocolMessage.processHelloMessageV6(endpoint, reader))
                    {
                        char node_type = endpoint.presenceAddress.type;
                        if (node_type != 'M' && node_type != 'H' && node_type != 'R')
                        {
                            CoreProtocolMessage.sendBye(endpoint, ProtocolByeCode.expectingMaster, string.Format("Expecting master node."), "", true);
                            return;
                        }

                        ulong last_block_num = reader.ReadIxiVarUInt();

                        int bcLen = (int)reader.ReadIxiVarUInt();
                        byte[] block_checksum = reader.ReadBytes(bcLen);

                        endpoint.blockHeight = last_block_num;

                        int block_version = (int)reader.ReadIxiVarUInt();

                        try
                        {
                            string public_ip = reader.ReadString();
                            ((NetworkClient)endpoint).myAddress = public_ip;
                        }
                        catch (Exception)
                        {

                        }

                        string address = Node.networkClientManagerStatic.getMyAddress();
                        if (address != null)
                        {
                            if (IxianHandler.publicIP != address)
                            {
                                Logging.info("Setting public IP to " + address);
                                IxianHandler.publicIP = address;
                            }
                        }

                        // Process the hello data
                        endpoint.helloReceived = true;
                        NetworkClientManager.recalculateLocalTimeDifference();

                        // Get random presences
                        endpoint.sendData(ProtocolMessageCode.getRandomPresences, new byte[1] { (byte)'M' });
                        endpoint.sendData(ProtocolMessageCode.getRandomPresences, new byte[1] { (byte)'H' });

                        CoreProtocolMessage.subscribeToEvents(endpoint);
                    }
                }
            }
        }

        static void handleGetPresence2(byte[] data, RemoteEndpoint endpoint)
        {
            using (MemoryStream m = new MemoryStream(data))
            {
                using (BinaryReader reader = new BinaryReader(m))
                {
                    int walletLen = (int)reader.ReadIxiVarUInt();
                    Address wallet = new Address(reader.ReadBytes(walletLen));
                    Presence p = PresenceList.getPresenceByAddress(wallet);
                    if (p != null)
                    {
                        lock (p)
                        {
                            byte[][] presence_chunks = p.getByteChunks();
                            foreach (byte[] presence_chunk in presence_chunks)
                            {
                                endpoint.sendData(ProtocolMessageCode.updatePresence, presence_chunk, null);
                            }
                        }
                    }
                    else
                    {
                        // TODO blacklisting point
                        Logging.warn(string.Format("Node has requested presence information about {0} that is not in our PL.", wallet.ToString()));
                    }
                }
            }
        }

        static void handleBalance2(byte[] data, RemoteEndpoint endpoint)
        {
            using (MemoryStream m = new MemoryStream(data))
            {
                using (BinaryReader reader = new BinaryReader(m))
                {
                    int address_length = (int)reader.ReadIxiVarUInt();
                    Address address = new Address(reader.ReadBytes(address_length));

                    // Retrieve the latest balance
                    int balance_len = (int)reader.ReadIxiVarUInt();
                    IxiNumber balance = new IxiNumber(new BigInteger(reader.ReadBytes(balance_len)));

                    if (address.SequenceEqual(IxianHandler.getWalletStorage().getPrimaryAddress()))
                    {
                        // Retrieve the blockheight for the balance
                        ulong block_height = reader.ReadIxiVarUInt();

                        if (block_height > Node.balance.blockHeight && (Node.balance.balance != balance || Node.balance.blockHeight == 0))
                        {
                            byte[] block_checksum = reader.ReadBytes((int)reader.ReadIxiVarUInt());

                            Node.balance.address = address;
                            Node.balance.balance = balance;
                            Node.balance.blockHeight = block_height;
                            Node.balance.blockChecksum = block_checksum;
                            Node.balance.lastUpdate = Clock.getTimestamp();
                            Node.balance.verified = false;
                        }
                    }
                    else
                    {
                        // Forward balance to client
                        var addressBytes = address.addressNoChecksum;
                        var pendingRequest = getAndRemovePendingRequest(ProtocolMessageCode.getBalance2, addressBytes);
                        if (pendingRequest != default)
                        {
                            foreach (var prEndpoint in pendingRequest.endpoints)
                            {
                                prEndpoint.sendData(ProtocolMessageCode.balance2, data, null, 0, MessagePriority.high);
                            }
                        }
                    }
                }
            }
        }



        static void handleInventory2(byte[] data, RemoteEndpoint endpoint)
        {
            using (MemoryStream m = new MemoryStream(data))
            {
                using (BinaryReader reader = new BinaryReader(m))
                {
                    ulong item_count = reader.ReadIxiVarUInt();
                    if (item_count > (ulong)CoreConfig.maxInventoryItems)
                    {
                        Logging.warn("Received {0} inventory items, max items is {1}", item_count, CoreConfig.maxInventoryItems);
                        item_count = (ulong)CoreConfig.maxInventoryItems;
                    }

                    ulong last_accepted_block_height = IxianHandler.getLastBlockHeight();

                    ulong network_block_height = IxianHandler.getHighestKnownNetworkBlockHeight();

                    Dictionary<ulong, List<InventoryItemSignature>> sig_lists = new Dictionary<ulong, List<InventoryItemSignature>>();
                    List<InventoryItemKeepAlive> ka_list = new List<InventoryItemKeepAlive>();
                    List<byte[]> tx_list = new List<byte[]>();
                    for (ulong i = 0; i < item_count; i++)
                    {
                        ulong len = reader.ReadIxiVarUInt();
                        byte[] item_bytes = reader.ReadBytes((int)len);
                        InventoryItem item = InventoryCache.decodeInventoryItem(item_bytes);
                        if (item.type == InventoryItemTypes.transaction)
                        {
                            PendingTransactions.increaseReceivedCount(item.hash, endpoint.presence.wallet);
                        }
                        PendingInventoryItem pii = InventoryCache.Instance.add(item, endpoint);

                        // First update endpoint blockheights
                        switch (item.type)
                        {
                            case InventoryItemTypes.block:
                                var iib = ((InventoryItemBlock)item);
                                if (iib.blockNum > endpoint.blockHeight)
                                {
                                    endpoint.blockHeight = iib.blockNum;
                                }
                                break;
                        }

                        if (!pii.processed && pii.lastRequested == 0)
                        {
                            // first time we're seeing this inventory item
                            switch (item.type)
                            {
                                case InventoryItemTypes.keepAlive:
                                    ka_list.Add((InventoryItemKeepAlive)item);
                                    pii.lastRequested = Clock.getTimestamp();
                                    break;

                                case InventoryItemTypes.transaction:
                                    tx_list.Add(item.hash);
                                    pii.lastRequested = Clock.getTimestamp();
                                    break;

                                case InventoryItemTypes.block:
                                    var iib = ((InventoryItemBlock)item);
                                    if (iib.blockNum <= last_accepted_block_height)
                                    {
                                        InventoryCache.Instance.setProcessedFlag(iib.type, iib.hash, true);
                                        continue;
                                    }

                                    var netBlockNum = CoreProtocolMessage.determineHighestNetworkBlockNum();
                                    if (iib.blockNum > netBlockNum)
                                    {
                                        continue;
                                    }

                                    requestNextBlock(iib.blockNum, iib.hash, endpoint);
                                    break;

                                default:
                                    Logging.warn("Unhandled inventory item {0}", item.type);
                                    break;
                            }
                        }
                    }

                    CoreProtocolMessage.broadcastGetKeepAlives(ka_list, endpoint);

                    CoreProtocolMessage.broadcastGetTransactions(tx_list, 0, endpoint);
                }
            }
        }

        static void requestNextBlock(ulong blockNum, byte[] blockHash, RemoteEndpoint endpoint)
        {
            InventoryItemBlock iib = new InventoryItemBlock(blockHash, blockNum);
            PendingInventoryItem pii = InventoryCache.Instance.add(iib, endpoint);
            if (!pii.processed
                && pii.lastRequested == 0)
            {
                pii.lastRequested = Clock.getTimestamp();
                InventoryCache.Instance.processInventoryItem(pii);
            }
        }
    }
}