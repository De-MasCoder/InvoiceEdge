using InvoiceEdgeWorker.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace InvoiceEdgeWorker.Entities
{
    public class Invoice
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;

        // Extracted Fields (Nullable in case AI extraction completely fails)
        public string? VendorName { get; set; }
        public decimal? TotalAmount { get; set; }
        public DateTime? InvoiceDate { get; set; }

        // Audit & System Metadata
        public string RawJson { get; set; } = string.Empty;
        public ProcessingStatus Status { get; set; } = ProcessingStatus.Unprocessed;
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
