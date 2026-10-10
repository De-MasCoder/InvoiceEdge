using System;
using System.Collections.Generic;
using System.Text;

namespace InvoiceEdgeWorker.Repositories
{
    /// <summary>
    /// Represents a unit of work that encapsulates the repositories and provides a method to commit changes to the database.
    /// </summary>
    public interface IUnitofWork : IDisposable
    {
        IInvoiceRepository Invoices { get; }
        Task<int> CompleteAsync(CancellationToken cancellationToken = default);
    }
}
