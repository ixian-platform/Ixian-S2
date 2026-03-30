using IXICore;
using IXICore.Activity;
using IXICore.Inventory;
using IXICore.Meta;
using IXICore.Network;
using IXICore.Utils;

namespace S2.Meta
{
    internal class S2TransactionInclusionCallbacks : TransactionInclusionCallbacks
    {
        public void transactionVerified(Transaction tx)
        {
            IxianHandler.balances.First().lastUpdate = 0;

            var bh = IxianHandler.getBlockHeader(tx.applied);
            Node.activityStorage.updateStatus(tx.id, ActivityStatus.Final, tx.applied, bh.timestamp);

            using (MemoryStream mw = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(mw))
            {
                writer.WriteIxiBytes(tx.pubKey.addressNoChecksum);
                CoreProtocolMessage.broadcastProtocolMessage(['M', 'H'], ProtocolMessageCode.getBalance2, mw.ToArray(), null);
            }
        }

        public void transactionRejected(Transaction tx)
        {
            tx.applied = 0;
            Node.activityStorage.updateStatus(tx.id, ActivityStatus.Error, 0);
        }

        public void transactionExpired(Transaction tx)
        {
            tx.applied = 0;
            Node.activityStorage.updateStatus(tx.id, ActivityStatus.Error, 0);
        }

        public void receivedBlockHeader(Block blockHeader, bool verified)
        {
            foreach (Balance balance in IxianHandler.balances)
            {
                if (balance.blockChecksum != null && balance.blockChecksum.SequenceEqual(blockHeader.blockChecksum))
                {
                    balance.verified = true;
                }
            }

            if (blockHeader.blockNum + 10 >= IxianHandler.getHighestKnownNetworkBlockHeight())
            {
                IxianHandler.status = NodeStatus.ready;
            }

            if (blockHeader.blockNum % CoreConfig.maxBlockHeadersPerDatabase == 0)
            {
                ulong fullBlocksToKeep = 4000;
                if (blockHeader.blockNum > fullBlocksToKeep)
                {
                    ulong pruneBlocksBelow = blockHeader.blockNum - fullBlocksToKeep;
                    Logging.info("Pruning block signatures up to block " + pruneBlocksBelow + " at height " + blockHeader.blockNum);
                    Node.storage.pruneBlocks(pruneBlocksBelow, IXICore.Storage.BlockSigPruningType.Signatures, false);
                    Logging.info("Pruning TxIDs up to block " + pruneBlocksBelow + " at height " + blockHeader.blockNum);
                    Node.storage.pruneTxIDs(pruneBlocksBelow);
                }

                ulong PoCWBlocksToKeep = 100000;
                if (blockHeader.blockNum > PoCWBlocksToKeep)
                {
                    ulong pruneBlocksBelow = blockHeader.blockNum - PoCWBlocksToKeep;
                    Logging.info("Pruning block PoCW up to block " + pruneBlocksBelow + " at height " + blockHeader.blockNum);
                    Node.storage.pruneBlocks(pruneBlocksBelow, IXICore.Storage.BlockSigPruningType.PoCW, false);
                }
            }

            NetworkServer.addToInventory(['C'], new InventoryItemBlock(blockHeader.blockChecksum, blockHeader.blockNum), null);
        }

        public void blockReorg(Block blockHeader)
        {
            var revertedTransactions = Node.activityStorage.revertTransactionsByBlockHeight(blockHeader.blockNum);
            foreach (var revertedTx in revertedTransactions)
            {
                var activity = Node.activityStorage.getActivityById(revertedTx, null, true);
                PendingTransactions.addOutgoingTransaction(activity.transaction, null);
            }
        }
    }
}
