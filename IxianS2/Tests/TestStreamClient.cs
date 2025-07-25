﻿using IXICore;
using IXICore.Meta;
using IXICore.Network;
using S2.Meta;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace S2
{
    class TestStreamClient : RemoteEndpoint
    {
        public TcpClient tcpClient = null;

        private string tcpHostname = "";
        private int tcpPort = 0;
        private int totalReconnects = 0;

        private object reconnectLock = new object();


        public string ip_address = "127.0.0.1";  // TODO replace with getfulladdress?

        byte[] chachaKey = null;
        string aesPassword = null;


        public TestStreamClient()
        {
            prepareClient();
        }

        // Prepare the client socket
        private void prepareClient()
        {
            tcpClient = new TcpClient();


            Socket tmpSocket = tcpClient.Client;

            // Don't allow another socket to bind to this port.
            tmpSocket.ExclusiveAddressUse = true;

            prepareSocket(tmpSocket);
        }

        public bool connectToServer(string hostname, int port)
        {
            if (fullyStopped)
            {
                Logging.error("Can't start a fully stopped RemoteEndpoint");
                return false;
            }

            helloReceived = false;

            tcpHostname = hostname;
            tcpPort = port;
            address = string.Format("{0}:{1}", hostname, port);
            ip_address = address;
            incomingPort = port;

            // Prepare the TCP client
            prepareClient();

            try
            {
                totalReconnects++;
                tcpClient.Connect(hostname, port);
            }
            catch (SocketException se)
            {
                SocketError errorCode = (SocketError)se.ErrorCode;

                switch (errorCode)
                {
                    case SocketError.IsConnected:
                        break;

                    case SocketError.AddressAlreadyInUse:
                        Logging.warn(string.Format("Stream Socket exception for {0}:{1} has failed. Address already in use.", hostname, port));
                        break;

                    default:
                        {
                            Logging.warn(string.Format("Stream Socket connection for {0}:{1} has failed.", hostname, port));
                        }
                        break;
                }

                disconnect();

                running = false;
                return false;
            }
            catch (Exception)
            {
                Logging.warn(string.Format("Stream client connection to {0}:{1} has failed.", hostname, port));
                running = false;
                return false;
            }

            Logging.info(string.Format("Stream client connected to {0}:{1}", hostname, port));

            start(tcpClient.Client);
            return true;
        }

        // Reconnect with the previous settings
        public bool reconnect()
        {
            lock (reconnectLock)
            {
                if (tcpHostname.Length < 1)
                {
                    Logging.warn("Stream client reconnect failed due to invalid hostname.");
                    return false;
                }

                // Safely close the threads
                running = false;

                disconnect();

                Logging.info(string.Format("--> Reconnecting to {0}, total reconnects: {1}", getFullAddress(true), totalReconnects));
                return connectToServer(tcpHostname, tcpPort);
            }
        }

        // Receive thread
        protected override void onInitialized()
        {
            sendHello();

            base.recvLoop();
        }

        public override void disconnect()
        {
            base.disconnect();
            tcpClient.Close();
        }

        // Returns the number of failed reconnects
        public int getTotalReconnectsCount()
        {
            return totalReconnects;
        }

        // Send a hello message containing the public ip and port of this node
        public void sendHello()
        {
            using (MemoryStream m = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(m))
                {
                    string publicHostname = string.Format("spixi:000"); //string.Format("{0}:{1}", NetworkStreamServer.publicIPAddress, Config.serverPort);

                    // Send the node version
                    writer.Write(CoreConfig.protocolVersion);

                    // Send the public node address
                    Address address = IxianHandler.getWalletStorage().getPrimaryAddress();
                    writer.Write(address.addressWithChecksum.Length);
                    writer.Write(address.addressWithChecksum);

                    // Send the testnet designator
                    writer.Write(IxianHandler.isTestNet);

                    // Send the node type
                    char node_type = 'C'; // This is a Client node
                    writer.Write(node_type);

                    // Send the version
                    writer.Write(Config.version);

                    // Send the node device id
                    writer.Write(CoreConfig.device_id);

                    // Send the wallet public key
                    writer.Write(IxianHandler.getWalletStorage().getPrimaryPublicKey().Length);
                    writer.Write(IxianHandler.getWalletStorage().getPrimaryPublicKey());

                    // Send listening port
                    writer.Write(0);

                    // Send timestamp
                    long timestamp = Clock.getNetworkTimestamp();
                    writer.Write(timestamp);

                    // send signature
                    //byte[] signature = CryptoManager.lib.getSignature(Encoding.UTF8.GetBytes(ConsensusConfig.ch.ixianChecksumLockString + "-" + CoreConfig.device_id + "-" + timestamp + "-" + publicHostname), IxianHandler.getWalletStorage().getPrimaryPrivateKey());
                    //writer.Write(signature.Length);
                    //writer.Write(signature);

/*
                    // Send the public IP address and port
                    writer.Write(publicHostname);


                    // Send the S2 public key
                    writer.Write(IxianHandler.getWalletStorage().encPublicKey);

                    // Send the wallet public key
                    writer.Write(IxianHandler.getWalletStorage().publicKey);*/

                    sendData(ProtocolMessageCode.hello, m.ToArray());

                    // Send a test message
                    //sendTestMessage();
                }
            }
        }

    }
}
