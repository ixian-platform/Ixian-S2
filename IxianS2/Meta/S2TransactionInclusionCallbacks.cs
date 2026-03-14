using IXICore;
using IXICore.Activity;
using IXICore.Inventory;
using IXICore.Meta;
using IXICore.Network;
using System;
using System.Linq;

namespace S2.Meta
{
    internal class S2TransactionInclusionCallbacks : TransactionInclusionCallbacks
    {
        public void transactionVerified(Transaction tx)
        {
            IxianHandler.balances.First().lastUpdate = 0;

            var bh = IxianHandler.getBlockHeader(tx.applied);
            Node.activityStorage.updateStatus(tx.id, ActivityStatus.Final, tx.applied, bh.timestamp);
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


        public void receivedBlockHeader(Block block_header, bool verified)
        {
            foreach (Balance balance in IxianHandler.balances)
            {
                if (balance.blockChecksum != null && balance.blockChecksum.SequenceEqual(block_header.blockChecksum))
                {
                    balance.verified = true;
                }
            }

            if (block_header.blockNum + 10 >= IxianHandler.getHighestKnownNetworkBlockHeight())
            {
                IxianHandler.status = NodeStatus.ready;
            }

            NetworkServer.addToInventory(['C'], new InventoryItemBlock(block_header.blockChecksum, block_header.blockNum), null);
        }

        public void blockReorg(Block blockHeader)
        {
            Node.activityStorage.revertTransactionsByBlockHeight(blockHeader.blockNum);
        }
    }
}
