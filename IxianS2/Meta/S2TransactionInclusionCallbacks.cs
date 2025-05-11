using IXICore;
using IXICore.Meta;
using System;
using System.Linq;

namespace S2.Meta
{
    internal class S2TransactionInclusionCallbacks : TransactionInclusionCallbacks
    {
        public void receivedTransactionInclusionVerificationResponse(byte[] txid, bool verified)
        {
            // TODO implement error
            // TODO implement blocknum

            ActivityStatus status = ActivityStatus.Pending;
            if (verified)
            {
                status = ActivityStatus.Final;
                PendingTransactions.remove(txid);
            }

            ActivityStorage.updateStatus(txid, status, 0);
        }

        public void receivedBlockHeader(Block blockHeader, bool verified)
        {
            if (Node.balance.blockChecksum != null && Node.balance.blockChecksum.SequenceEqual(blockHeader.blockChecksum))
            {
                Node.balance.verified = true;
            }
            if (blockHeader.blockNum >= IxianHandler.getHighestKnownNetworkBlockHeight())
            {
                IxianHandler.status = NodeStatus.ready;
            }
            Node.processPendingTransactions();
        }
    }
}
