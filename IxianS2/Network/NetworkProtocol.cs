using IXICore;
using IXICore.Activity;
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
                                                                                                                                               { ProtocolMessageCode.getSectorNodes, new(new ByteArrayComparer()) },
                                                                                                                                               { ProtocolMessageCode.getPIT2, new(new ByteArrayComparer()) },
                                                                                                                                               { ProtocolMessageCode.getNameRecord, new(new ByteArrayComparer()) },
                                                                                                                                               { ProtocolMessageCode.getTransaction3, new(new ByteArrayComparer()) },
                                                                                                                                               { ProtocolMessageCode.getBlockHeaders4, new(new ByteArrayComparer()) }};

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
                int expiration = 30;
                if (prType.Key == ProtocolMessageCode.getBlockHeaders4)
                {
                    expiration = 120;
                }

                Dictionary<byte[], (long timestamp, List<RemoteEndpoint> endpoint)> tmpPendingRequests = new(prType.Value, new ByteArrayComparer());
                foreach (var pending in tmpPendingRequests)
                {
                    if (Clock.getTimestamp() - pending.Value.timestamp > expiration)
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

                    case ProtocolMessageCode.attachEvent:
                        NetworkEvents.handleAttachEventMessage(data, endpoint);
                        break;

                    case ProtocolMessageCode.detachEvent:
                        NetworkEvents.handleDetachEventMessage(data, endpoint);
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
                        handleTransactionData(data, endpoint);
                        break;

                    case ProtocolMessageCode.updatePresence:
                        handleUpdatePresence(data, endpoint);
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

                    case ProtocolMessageCode.blockHeaders4:
                        handleBlockHeaders4(data, endpoint);
                        break;

                    case ProtocolMessageCode.pitData2:
                        handlePITData(data, endpoint);
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

                    case ProtocolMessageCode.keepAlivesChunk:
                        handleKeepAlivesChunk(data, endpoint);
                        break;

                    case ProtocolMessageCode.getKeepAlives:
                        CoreProtocolMessage.processGetKeepAlives(data, endpoint);
                        break;

                    case ProtocolMessageCode.getBlockHeaders3:
                        handleGetBlockHeaders3(data, endpoint);
                        break;

                    case ProtocolMessageCode.getPIT2:
                        handleGetPIT2(data, endpoint);
                        break;

                    case ProtocolMessageCode.getBlockHeaders4:
                        handleGetBlockHeaders4(data, endpoint);
                        break;

                    case ProtocolMessageCode.getTransaction3:
                        handleGetTransaction(data, endpoint);
                        break;

                    // return 10 random presences of the selected type
                    case ProtocolMessageCode.getRandomPresences:
                        handleGetRandomPresences(data, endpoint);
                        break;

                    case ProtocolMessageCode.transactionsChunk3:
                        handleTransactionsChunk3(data, endpoint);
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

        public static void handleTransactionsChunk3(byte[] data, RemoteEndpoint endpoint)
        {
            using (MemoryStream m = new MemoryStream(data))
            {
                using (BinaryReader reader = new BinaryReader(m))
                {
                    if (endpoint.presenceAddress.type != 'M' && endpoint.presenceAddress.type != 'H')
                    {
                        Logging.warn("Received transactions chunk from non-master node {0}. Ignoring.", endpoint.getFullAddress());
                        return;
                    }

                    var tag = reader.ReadIxiBytes();
                    var pendingRequest = getAndRemovePendingRequest(ProtocolMessageCode.getBlockHeaders4, tag);
                    if (pendingRequest != default)
                    {
                        byte[] txChunkData = new byte[data.Length - reader.BaseStream.Position];
                        Buffer.BlockCopy(data, (int)reader.BaseStream.Position, txChunkData, 0, txChunkData.Length);
                        foreach (var client in pendingRequest.endpoints)
                        {
                            client.sendData(ProtocolMessageCode.blockHeaders4, txChunkData);
                        }
                        return;
                    }

                    long msg_id = reader.ReadIxiVarInt();

                    int tx_count = (int)reader.ReadIxiVarUInt();

                    int max_tx_per_chunk = CoreConfig.maximumTransactionsPerChunk;
                    if (tx_count > max_tx_per_chunk)
                    {
                        tx_count = max_tx_per_chunk;
                    }

                    var sw = new System.Diagnostics.Stopwatch();
                    sw.Start();
                    int processedTxCount = 0;
                    int totalTxCount = 0;
                    for (int i = 0; i < tx_count; i++)
                    {
                        if (m.Position == m.Length)
                        {
                            break;
                        }

                        int tx_len = (int)reader.ReadIxiVarUInt();
                        byte[] tx_bytes = reader.ReadBytes(tx_len);

                        Transaction tx = new Transaction(tx_bytes, false, true);

                        totalTxCount++;

                        if (IxianHandler.addIncomingTransaction(tx))
                        {
                            processedTxCount++;
                        }
                    }
                    sw.Stop();
                    TimeSpan elapsed = sw.Elapsed;
                    Logging.info("Processed {0}/{1} txs for #{2} in {3}ms", processedTxCount, totalTxCount, msg_id, elapsed.TotalMilliseconds);
                }
            }
        }

        static void handleBlockHeaders4(byte[] data, RemoteEndpoint endpoint)
        {
            using (MemoryStream m = new MemoryStream(data))
            {
                using (BinaryReader reader = new BinaryReader(m))
                {
                    ulong from = reader.ReadIxiVarUInt();
                    ulong totalCount = reader.ReadIxiVarUInt();

                    int filterLen = (int)reader.ReadIxiVarUInt();
                    byte[] filterBytes = reader.ReadBytes(filterLen);

                    byte[] prKey = new byte[reader.BaseStream.Position];
                    Buffer.BlockCopy(data, 0, prKey, 0, prKey.Length);

                    var pendingRequest = getAndRemovePendingRequest(ProtocolMessageCode.getBlockHeaders4, prKey);
                    if (pendingRequest != default)
                    {
                        foreach (var client in pendingRequest.endpoints)
                        {
                            client.sendData(ProtocolMessageCode.blockHeaders4, data);
                        }
                    }
                    else
                    {
                        byte[] headersBytes = new byte[reader.BaseStream.Length - reader.BaseStream.Position];
                        Buffer.BlockCopy(data, (int)reader.BaseStream.Position, headersBytes, 0, headersBytes.Length);

                        Node.tiv.receivedBlockHeaders3(headersBytes, endpoint);
                    }
                }
            }
        }

        static void handleGetTransaction(byte[] data, RemoteEndpoint endpoint)
        {
            using (MemoryStream m = new MemoryStream(data))
            {
                using (BinaryReader reader = new BinaryReader(m))
                {
                    // Retrieve the transaction id
                    int txid_len = (int)reader.ReadIxiVarUInt();
                    byte[] txid = reader.ReadBytes(txid_len);
                    ulong block_num = reader.ReadIxiVarUInt();

                    addPendingRequest(ProtocolMessageCode.getTransaction3, txid, endpoint);
                    NetworkClientManager.broadcastData(['M', 'H'], ProtocolMessageCode.getTransaction3, data, null);
                }
            }
        }

        static void handlePITData(byte[] data, RemoteEndpoint endpoint)
        {
            int offset = 0;

            var filterWithOffset = data.ReadIxiBytes(offset);
            offset += filterWithOffset.bytesRead;

            var blockNumWithOffset = data.GetIxiVarInt(offset);
            offset += blockNumWithOffset.bytesRead;


            byte[] key = new byte[offset];
            Buffer.BlockCopy(data, filterWithOffset.bytesRead, key, 0, blockNumWithOffset.bytesRead);
            Buffer.BlockCopy(data, 0, key, blockNumWithOffset.bytesRead, filterWithOffset.bytesRead);

            byte[] pitData = new byte[data.Length - filterWithOffset.bytesRead];
            Buffer.BlockCopy(data, filterWithOffset.bytesRead, pitData, 0, pitData.Length);

            var pendingRequest = getAndRemovePendingRequest(ProtocolMessageCode.getPIT2, key);
            if (pendingRequest != default)
            {
                foreach (var prEndpoint in pendingRequest.endpoints)
                {
                    prEndpoint.sendData(ProtocolMessageCode.pitData2, pitData, null, 0, MessagePriority.high);
                }
            }
            else if (pendingRequests[ProtocolMessageCode.getBlockHeaders4].TryGetValue(filterWithOffset.bytes, out pendingRequest))
            {
                foreach (var prEndpoint in pendingRequest.endpoints)
                {
                    prEndpoint.sendData(ProtocolMessageCode.pitData2, pitData, null, 0, MessagePriority.high);
                }
            }

            Node.tiv.receivedPIT2(pitData, endpoint);
        }

        public static void handleGetBlockHeaders3(byte[] data, RemoteEndpoint endpoint)
        {
            using (MemoryStream m = new MemoryStream(data))
            {
                using (BinaryReader reader = new BinaryReader(m))
                {
                    ulong from = reader.ReadIxiVarUInt();
                    ulong totalCount = reader.ReadIxiVarUInt();

                    if (totalCount < 1)
                        return;

                    ulong lastBlockNum = IxianHandler.getLastBlockHeight();

                    if (from > lastBlockNum - 1)
                        return;

                    // Adjust total count if necessary
                    if (from + totalCount > lastBlockNum)
                        totalCount = lastBlockNum - from;

                    if (totalCount < 1)
                        return;

                    // Cap total block headers sent
                    if (totalCount > (ulong)CoreConfig.maximumBlockHeadersPerChunk)
                        totalCount = (ulong)CoreConfig.maximumBlockHeadersPerChunk;

                    if (endpoint == null)
                    {
                        return;
                    }

                    if (!endpoint.isConnected())
                    {
                        return;
                    }

                    // TODO TODO TODO block headers should be read from a separate storage and every node should keep a full copy
                    for (ulong i = 0; i < totalCount;)
                    {
                        bool found = false;
                        using (MemoryStream mOut = new MemoryStream())
                        {
                            using (BinaryWriter writer = new BinaryWriter(mOut))
                            {
                                for (int j = 0; j < CoreConfig.maximumBlockHeadersPerChunk && i < totalCount; j++)
                                {
                                    Block? block = IxianHandler.getBlockHeader(from + i);
                                    i++;
                                    if (block == null)
                                        break;

                                    long rollback_len = mOut.Length;

                                    found = true;
                                    byte[] headerBytes = block.getBytes(true, true, true, true);
                                    writer.WriteIxiVarInt(headerBytes.Length);
                                    writer.Write(headerBytes);

                                    if (mOut.Length > CoreConfig.maxMessageSize)
                                    {
                                        mOut.SetLength(rollback_len);
                                        i--;
                                        break;
                                    }
                                }
                            }
                            if (!found)
                            {
                                break;
                            }
                            endpoint.sendData(ProtocolMessageCode.blockHeaders3, mOut.ToArray());
                        }
                    }
                }
            }
        }

        public static void handleTransactionData(byte[] data, RemoteEndpoint endpoint)
        {
            Transaction tx = new Transaction(data, true, true);

            bool myTransaction = false;

            if (endpoint.presenceAddress.type == 'C')
            {
                // TODO if transaction already processed, send proofs back to the client

                ToEntry value;
                if (tx.toList.TryGetValue(IxianHandler.primaryWalletAddress, out value)
                    /*&& value.amount >= tx.fee / 10*/)
                {
                    // Check if transaction already processed
                    if (!Node.addTransaction(endpoint.serverWalletAddress, tx, null, null, null, true))
                    {
                        endpoint.sendData(ProtocolMessageCode.rejected, new Rejected(RejectedCode.TransactionDuplicate, tx.id).getBytes());
                        return;
                    }
                    myTransaction = true;
                } /*else
                {
                    endpoint.sendData(ProtocolMessageCode.rejected, new Rejected(RejectedCode.TransactionInsufficientFee, tx.id).getBytes());
                    return;
                }*/
            }
            else
            {
                // Check if my transaction
                myTransaction = IxianHandler.isMyAddress(tx.pubKey);
                if (!myTransaction)
                {
                    foreach (var toEntry in tx.toList.Keys)
                    {
                        if (IxianHandler.isMyAddress(toEntry))
                        {
                            myTransaction = true;
                            break;
                        }
                    }
                }
            }

            var pendingRequest = getAndRemovePendingRequest(ProtocolMessageCode.getTransaction3, tx.id);
            if (pendingRequest != default)
            {
                foreach (var client in pendingRequest.endpoints)
                {
                    if (client.serverWalletAddress == null)
                    {
                        continue;
                    }
                    client.sendData(ProtocolMessageCode.transactionData2, tx.getBytes(true, true), null);
                }
            }

            Logging.trace("Received new transaction {0}", tx.getTxIdString());

            if (myTransaction)
            {
                // If transaction already processed
                ActivityObject? activity = Node.activityStorage.getActivityById(tx.id, null);
                if (activity != null)
                {
                    if (activity.status != ActivityStatus.Final)
                    {
                        if (endpoint.presenceAddress.type == 'M' || endpoint.presenceAddress.type == 'H')
                        {
                            PendingTransactions.increaseReceivedCount(tx.id, endpoint.presence.wallet);
                        }
                    }
                }
                else
                {
                    IxianHandler.addIncomingTransaction(tx);
                }
            }
        }

        public static void handleGetPIT2(byte[] data, RemoteEndpoint endpoint)
        {
            addPendingRequest(ProtocolMessageCode.getPIT2, data, endpoint);
            NetworkClientManager.broadcastData(['M', 'H'], ProtocolMessageCode.getPIT2, data, null);
        }

        public static void handleKeepAlivesChunk(byte[] data, RemoteEndpoint endpoint)
        {
            using (MemoryStream m = new MemoryStream(data))
            {
                using (BinaryReader reader = new BinaryReader(m))
                {
                    int ka_count = (int)reader.ReadIxiVarUInt();

                    int max_ka_per_chunk = CoreConfig.maximumKeepAlivesPerChunk;
                    if (ka_count > max_ka_per_chunk)
                    {
                        ka_count = max_ka_per_chunk;
                    }

                    for (int i = 0; i < ka_count; i++)
                    {
                        if (m.Position == m.Length)
                        {
                            break;
                        }

                        int ka_len = (int)reader.ReadIxiVarUInt();
                        byte[] ka_bytes = reader.ReadBytes(ka_len);

                        handleKeepAlivePresence(ka_bytes, endpoint);
                    }
                }
            }
        }

        private static void handleUpdatePresence(byte[] data, RemoteEndpoint endpoint)
        {
            // Parse the data and update entries in the presence list
            Presence updatedPresence = PresenceList.updateFromBytes(data, 0);

            // If a presence entry was updated, broadcast this message again
            if (updatedPresence != null)
            {
                foreach (var pa in updatedPresence.addresses)
                {
                    byte[] hash = CryptoManager.lib.sha3_512sqTrunc(pa.getBytes());
                    var iika = new InventoryItemKeepAlive(hash, pa.lastSeenTime, updatedPresence.wallet, pa.device);

                    if (pa.type == 'R')
                    {
                        Node.networkClientManagerStatic.addToInventory(['R'], iika, endpoint);
                        NetworkServer.addToInventory(['R'], iika, endpoint);
                    }
                    else if (pa.type == 'C')
                    {
                        sendKeepAlivePresenceToNeighbourSectorNodes(iika, endpoint);
                    }

                    NetworkServer.addToInventorySubscribed(iika, endpoint);
                }
            }
        }

        private static void sendKeepAlivePresenceToNeighbourSectorNodes(InventoryItemKeepAlive iika, RemoteEndpoint endpoint)
        {
            var sectorNodes = RelaySectors.Instance.getSectorNodes(IxianHandler.primaryWalletAddress.sectorPrefix, Config.maxRelaySectorNodesToConnectTo);
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

                RemoteEndpoint client = Node.networkClientManagerStatic.getClient(sectorNodes[i]);
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

            InventoryCache.Instance.setProcessedFlag(InventoryItemTypes.keepAlive, hash);

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
                    NetworkServer.addToInventory(['R'], iika, endpoint);
                }
                else if (node_type == 'C')
                {
                    sendKeepAlivePresenceToNeighbourSectorNodes(iika, endpoint);
                }

                // Send this keepalive message to all subscribed clients
                NetworkServer.addToInventorySubscribed(iika, endpoint);
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
            var addressWithOffset = data.ReadIxiBytes(0);
            addPendingRequest(ProtocolMessageCode.getBalance2, addressWithOffset.bytes, endpoint);
            NetworkClientManager.broadcastData(['M', 'H'], ProtocolMessageCode.getBalance2, data, null);
        }

        public static void handleGetBlockHeaders4(byte[] data, RemoteEndpoint endpoint)
        {
            using (MemoryStream m = new MemoryStream(data))
            using (BinaryReader reader = new BinaryReader(m))
            {
                ulong from = reader.ReadIxiVarUInt();
                ulong totalCount = reader.ReadIxiVarUInt();

                byte[] filterBytes = reader.ReadIxiBytes()!;

                byte[] prKey = new byte[reader.BaseStream.Position];
                Buffer.BlockCopy(data, 0, prKey, 0, prKey.Length);

                addPendingRequest(ProtocolMessageCode.getBlockHeaders4, prKey, endpoint);
                NetworkClientManager.broadcastData(['M', 'H'], ProtocolMessageCode.getBlockHeaders4, data, null);
            }
        }

        public static void handleGetSectorNodes(byte[] data, RemoteEndpoint endpoint)
        {
            int offset = 0;
            var prefixWithOffset = data.ReadIxiBytes(offset);
            offset += prefixWithOffset.bytesRead;

            var maxRelayCountWithOffset = data.GetIxiVarUInt(offset);
            offset += maxRelayCountWithOffset.bytesRead;
            int maxRelayCount = (int)maxRelayCountWithOffset.num;

            if (maxRelayCount > 20)
            {
                maxRelayCount = 20;
            }

            if (cachedSectors.ContainsKey(prefixWithOffset.bytes))
            {
                var relayList = RelaySectors.Instance.getSectorNodes(prefixWithOffset.bytes, maxRelayCount);

                CoreProtocolMessage.sendSectorNodes(prefixWithOffset.bytes, relayList, endpoint);
            }
            else
            {
                addPendingRequest(ProtocolMessageCode.getSectorNodes, prefixWithOffset.bytes, endpoint);
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

            if (IxianHandler.primaryWalletAddress.sectorPrefix.SequenceEqual(prefix))
            {
                List<Peer> peers = new();
                var relays = RelaySectors.Instance.getSectorNodes(prefix, Config.maxRelaySectorNodesToConnectTo + 1);
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

        static void handleRejected(byte[] data, RemoteEndpoint endpoint)
        {
            try
            {
                Rejected rej = new Rejected(data);
                switch (rej.code)
                {
                    case RejectedCode.TransactionInvalid:
                    case RejectedCode.TransactionInsufficientFee:
                    case RejectedCode.TransactionDust:
                        {
                            if (endpoint.presenceAddress.type != 'M'
                                && endpoint.presenceAddress.type != 'H')
                            {
                                Logging.error("Received 'rejected' message {0} {1} from non-master {2}", rej.code, Transaction.getTxIdString(rej.data), endpoint.getFullAddress());
                                return;
                            }
                            Logging.error("Transaction {0} was rejected with code: {1}", Transaction.getTxIdString(rej.data), rej.code);

                            PendingTransactions.increaseRejectedCount(rej.data, endpoint.serverWalletAddress);
                            var pendingTx = PendingTransactions.getPendingTransaction(rej.data);
                            if (pendingTx?.senderAddress != null)
                            {
                                // notify client who sent this transaction to us
                                NetworkServer.getClient(pendingTx.senderAddress)?.sendData(ProtocolMessageCode.rejected, data);
                            }
                        }
                        break;

                    case RejectedCode.TransactionDuplicate:
                        {
                            if (endpoint.presenceAddress.type != 'M'
                                && endpoint.presenceAddress.type != 'H')
                            {
                                Logging.error("Received 'rejected' message {0} {1} from non-master {2}", rej.code, Transaction.getTxIdString(rej.data), endpoint.getFullAddress());
                                return;
                            }
                            Logging.warn("Transaction {0} already sent.", Transaction.getTxIdString(rej.data), rej.code);
                            // All good
                            PendingTransactions.increaseReceivedCount(rej.data, endpoint.serverWalletAddress);
                            var pendingTx = PendingTransactions.getPendingTransaction(rej.data);
                            if (pendingTx?.senderAddress != null)
                            {
                                // notify client who sent this transaction to us
                                NetworkServer.getClient(pendingTx.senderAddress)?.sendData(ProtocolMessageCode.rejected, data);
                            }
                        }
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
                        if (node_type == 'M' || node_type == 'H' || node_type == 'R')
                        {
                            endpoint.sendData(ProtocolMessageCode.getRandomPresences, new byte[1] { (byte)'M' });
                            endpoint.sendData(ProtocolMessageCode.getRandomPresences, new byte[1] { (byte)'H' });
                        }

                        if (node_type == 'M' || node_type == 'H')
                        {
                            if (Node.networkClientManagerStatic.clientsToConnectTo.Count < 2)
                            {
                                CoreProtocolMessage.fetchSectorNodes(IxianHandler.primaryWalletAddress, Config.maxRelaySectorNodesToRequest, endpoint);
                            }
                        }
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
                    IxiNumber ixi_balance = new IxiNumber(new BigInteger(reader.ReadBytes(balance_len)));

                    // Retrieve the blockheight for the balance
                    ulong block_height = reader.ReadIxiVarUInt();

                    foreach (Balance balance in IxianHandler.balances)
                    {
                        if (address.addressNoChecksum.SequenceEqual(balance.address.addressNoChecksum))
                        {
                            if (block_height > balance.blockHeight && (balance.balance != ixi_balance || balance.blockHeight == 0))
                            {
                                byte[] block_checksum = reader.ReadBytes((int)reader.ReadIxiVarUInt());

                                balance.address = address;
                                balance.balance = ixi_balance;
                                balance.blockHeight = block_height;
                                balance.blockChecksum = block_checksum;
                                balance.verified = false;
                            }

                            balance.lastUpdate = Clock.getTimestamp();
                            return;
                        }
                    }

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
                    for (ulong i = 0; i < item_count; i++)
                    {
                        ulong len = reader.ReadIxiVarUInt();
                        byte[] item_bytes = reader.ReadBytes((int)len);
                        InventoryItem item = InventoryCache.decodeInventoryItem(item_bytes);

                        if (item == null)
                        {
                            Logging.warn("Failed to decode inventory item, skipping. Endpoint: {0}", endpoint.getFullAddress());
                            continue;
                        }

                        // First update endpoint blockheights and pending transactions
                        switch (item.type)
                        {
                            case InventoryItemTypes.transaction:
                                PendingTransactions.increaseReceivedCount(item.hash, endpoint.presence.wallet);
                                break;

                            case InventoryItemTypes.block:
                                var iib = ((InventoryItemBlock)item);
                                if (iib.blockNum > endpoint.blockHeight)
                                {
                                    endpoint.blockHeight = iib.blockNum;
                                }
                                break;
                        }

                        PendingInventoryItem pii = InventoryCache.Instance.add(item, endpoint, false);

                        if (!pii.processed && pii.lastRequested == 0)
                        {
                            // first time we're seeing this inventory item
                            switch (item.type)
                            {
                                case InventoryItemTypes.keepAlive:
                                    var iika = (InventoryItemKeepAlive)item;
                                    if (PresenceList.getPresenceByAddress(iika.address) != null)
                                    {
                                        ka_list.Add(iika);
                                        pii.lastRequested = Clock.getTimestamp();
                                    }
                                    else
                                    {
                                        InventoryCache.Instance.processInventoryItem(pii);
                                    }
                                    break;

                                case InventoryItemTypes.block:
                                    var iib = ((InventoryItemBlock)item);
                                    if (iib.blockNum <= last_accepted_block_height)
                                    {
                                        InventoryCache.Instance.setProcessedFlag(iib.type, iib.hash);
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
                                    InventoryCache.Instance.setProcessedFlag(item.type, item.hash);
                                    break;
                            }
                        }
                    }

                    CoreProtocolMessage.broadcastGetKeepAlives(ka_list, endpoint);
                }
            }
        }

        static void requestNextBlock(ulong blockNum, byte[] blockHash, RemoteEndpoint endpoint)
        {
            InventoryItemBlock iib = new InventoryItemBlock(blockHash, blockNum);
            PendingInventoryItem pii = InventoryCache.Instance.add(iib, endpoint, true);
            if (pii.lastRequested == 0)
            {
                pii.lastRequested = Clock.getTimestamp();
                InventoryCache.Instance.processInventoryItem(pii);
            }
        }

        static void handleGetRandomPresences(char type, RemoteEndpoint endpoint)
        {
            if (!endpoint.isConnected())
            {
                return;
            }

            List<Presence> presences = PresenceList.getPresencesByType(type, 20);
            int presence_count = presences.Count();
            if (presence_count > 10)
            {
                Random rnd = new Random();
                presences = presences.Skip(rnd.Next(presence_count - 10)).Take(10).ToList();
            }

            foreach (Presence presence in presences)
            {
                byte[][] presence_chunks = presence.getByteChunks();
                foreach (byte[] presence_chunk in presence_chunks)
                {
                    endpoint.sendData(ProtocolMessageCode.updatePresence, presence_chunk, null);
                }
            }
        }

        static void handleGetRandomPresences(byte[] data, RemoteEndpoint endpoint)
        {
            using (MemoryStream m = new MemoryStream(data))
            {
                using (BinaryReader reader = new BinaryReader(m))
                {
                    char type = reader.ReadChar();

                    handleGetRandomPresences(type, endpoint);
                }
            }
        }
    }
}