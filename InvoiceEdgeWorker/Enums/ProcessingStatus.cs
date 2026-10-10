using System;
using System.Collections.Generic;
using System.Text;

namespace InvoiceEdgeWorker.Enums
{
    public enum ProcessingStatus
    {
        Unprocessed,
        Processing,
        ProcessedLocally,
        RequiresReview,
        SyncFailed,
        SyncedToCloud
    }
}
