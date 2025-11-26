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
        public void receivedTIVResponse(Transaction tx, bool verified)
        {
            if (!verified)
            {
                tx.applied = 0;
                Node.activityStorage.updateStatus(tx.id, ActivityStatus.Error, 0);
                return;
            }

            ActivityStatus status = ActivityStatus.Pending;
            if (verified)
            {
                status = ActivityStatus.Final;
                PendingTransactions.remove(tx.id);
            }

            var bh = IxianHandler.getBlockHeader(tx.applied);
            Node.activityStorage.updateStatus(tx.id, status, tx.applied, bh.timestamp);
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

            if (block_header.blockNum >= IxianHandler.getHighestKnownNetworkBlockHeight())
            {
                IxianHandler.status = NodeStatus.ready;
            }

            NetworkServer.addToInventory(['C'], new InventoryItemBlock(block_header.blockChecksum, block_header.blockNum), null);

            Node.processPendingTransactions();
        }
    }
}
