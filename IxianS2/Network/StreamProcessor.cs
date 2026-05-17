using IXICore;
using IXICore.Meta;
using IXICore.Network;
using IXICore.Streaming;
using System;
using System.Collections.Generic;
using System.IO;

namespace S2.Network
{
    class StreamTransaction
    {
        public string messageID;
        public Transaction transaction;
    }

    class StreamProcessor : CoreStreamProcessor
    {
        public static ulong bytesForRelayReceived = 0;
        public static ulong bytesRelayed = 0;
        List<StreamTransaction> transactions = new List<StreamTransaction>(); // List that stores stream transactions

        public StreamProcessor(PendingMessageProcessor pendingMessageProcessor, StreamCapabilities streamCapabilites) : base(pendingMessageProcessor, streamCapabilites)
        {
        }

        // Called when receiving S2 data from clients
        public override ReceiveDataResponse? receiveData(byte[] bytes, RemoteEndpoint endpoint, bool fireLocalNotification = true, bool alert = true)
        {
            string endpoint_wallet_string = endpoint.presence.wallet.ToString();
            Logging.trace("Receiving S2 data from {0}", endpoint_wallet_string);

            StreamMessage message = new StreamMessage(bytes);

            bool data_message = false;
            if (message.type == StreamMessageCode.data)
                data_message = true;

            QuotaManager.addActivity(endpoint.presence.wallet, data_message);
            bytesForRelayReceived += (ulong)bytes.Length;
            if (!IxianHandler.isMyAddress(message.recipient))
            {
                // Don't allow clients to send error stream messages, as it's reserved for S2 nodes only
                if (message.type == StreamMessageCode.error)
                {
                    Logging.warn("Discarding error message type from {0}", endpoint_wallet_string);
                    return null;
                }

                // TODO: commented for development purposes ONLY!
                /*if (QuotaManager.exceededQuota(endpoint.presence.wallet))
                {
                    Logging.error(string.Format("Exceeded quota of info relay messages for {0}", endpoint_wallet_string));
                    sendError(endpoint.presence.wallet);
                    return;
                }*/

                if (!NetworkServer.forwardMessage(message.recipient, ProtocolMessageCode.s2data, bytes))
                {
                    // Couldn't forward the message, send failed to client
                    CoreProtocolMessage.sendStreamError(message.sender, message.recipient, message.id, endpoint);
                    return null;
                }
                bytesRelayed += (ulong)bytes.Length;
                return null;
            }

            ReceiveDataResponse? rdr = base.receiveData(bytes, endpoint, false);
            if (rdr == null)
            {
                return rdr;
            }

            SpixiMessage spixi_message = rdr.spixiMessage;
            Friend friend = rdr.friend;
            Address sender_address = rdr.senderAddress;
            Address? group_sender_address = rdr.groupSenderAddress;

            if (friend != null)
            {
                if (endpoint != null)
                {
                    // Update friend's last seen and relay if outgoing stream capabilities are disabled
                    if ((streamCapabilities & StreamCapabilities.Outgoing) == 0)
                    {
                        friend.updatedStreamingNodes = Clock.getNetworkTimestamp();
                        friend.relayNode = new Peer(endpoint.getFullAddress(true), endpoint.serverWalletAddress, Clock.getTimestamp(), Clock.getTimestamp(), Clock.getTimestamp(), 0);
                        friend.updatedStreamingNodes = friend.relayNode.lastSeen;
                        friend.lastSeenTime = friend.relayNode.lastSeen; 
                        friend.online = true;
                    }
                }
            }

            return rdr;
            // TODO: commented for development purposes ONLY!
            /*
                        // Extract the transaction
                        Transaction transaction = new Transaction(message.transaction);

                        // Validate transaction sender
                        if(transaction.from.SequenceEqual(message.sender) == false)
                        {
                            Logging.error(string.Format("Relayed message transaction mismatch for {0}", endpoint_wallet_string));
                            sendError(message.sender);
                            return;
                        }

                        // Validate transaction amount and fee
                        if(transaction.amount < CoreConfig.relayPriceInitial || transaction.fee < CoreConfig.forceTransactionPrice)
                        {
                            Logging.error(string.Format("Relayed message transaction amount too low for {0}", endpoint_wallet_string));
                            sendError(message.sender);
                            return;
                        }

                        // Validate transaction receiver
                        if (transaction.toList.Keys.First().SequenceEqual(IxianHandler.getWalletStorage().address) == false)
                        {
                            Logging.error("Relayed message transaction receiver is not this S2 node");
                            sendError(message.sender);
                            return;
                        }

                        // Update the recipient dictionary
                        if (dataRelays.ContainsKey(message.recipient))
                        {
                            dataRelays[message.recipient]++;
                            if(dataRelays[message.recipient] > Config.relayDataMessageQuota)
                            {
                                Logging.error(string.Format("Exceeded amount of unpaid data relay messages for {0}", endpoint_wallet_string));
                                sendError(message.sender);
                                return;
                            }
                        }
                        else
                        {
                            dataRelays.Add(message.recipient, 1);
                        }


                        // Store the transaction
                        StreamTransaction streamTransaction = new StreamTransaction();
                        streamTransaction.messageID = message.getID();
                        streamTransaction.transaction = transaction;
                        lock (transactions)
                        {
                            transactions.Add(streamTransaction);
                        }

                        // For testing purposes, allow the S2 node to receive relay data itself
                        if (message.recipient.SequenceEqual(IxianHandler.getWalletStorage().getWalletAddress()))
                        {               
                            string test = Encoding.UTF8.GetString(message.data);
                            Logging.info(test);

                            return;
                        }

                        Logging.info("NET: Forwarding S2 data");
                        NetworkStreamServer.forwardMessage(message.recipient, DLT.Network.ProtocolMessageCode.s2data, bytes);      
                        */
        }

        // Called when receiving a transaction signature from a client
        public void receivedTransactionSignature(byte[] bytes, RemoteEndpoint endpoint)
        {
            using (MemoryStream m = new MemoryStream(bytes))
            {
                using (BinaryReader reader = new BinaryReader(m))
                {
                    // Read the message ID
                    string messageID = reader.ReadString();
                    int sig_length = reader.ReadInt32();
                    if(sig_length <= 0)
                    {
                        Logging.warn("Incorrect signature length received.");
                        return;
                    }

                    // Read the signature
                    byte[] signature = reader.ReadBytes(sig_length);

                    lock (transactions)
                    {
                        // Find the transaction with a matching message id
                        StreamTransaction tx = transactions.Find(x => x.messageID.Equals(messageID, StringComparison.Ordinal));
                        if(tx == null)
                        {
                            Logging.warn("No transaction found to match signature messageID.");
                            return;
                        }
                     
                        // Compose a new transaction and apply the received signature
                        Transaction transaction = new Transaction(tx.transaction);
                        transaction.signature = signature;

                        // Verify the signed transaction
                        if (transaction.verifySignature(transaction.pubKey.pubKey, null))
                        {
                            // Broadcast the transaction
                            CoreProtocolMessage.broadcastProtocolMessage(new char[] { 'M', 'H' }, ProtocolMessageCode.transactionData2, transaction.getBytes(true, true), endpoint);
                        }
                        return;
                                                 
                    }
                }
            }
        }
    }
}
