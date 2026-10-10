using InvoiceEdgeWorker.Entities;

namespace InvoiceEdgeWorker.Repositories;

public interface IInvoiceRepository
{
    /// <summary>
    /// Gets an invoice by its unique identifier.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>
    /// Gets an invoice by its file name.
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    Task<Invoice?> GetByFileNameAsync(string fileName, CancellationToken cancellationToken = default);
    Task<Invoice?> GetByContentHashAsync(string contentHash, CancellationToken cancellationToken = default);
    /// <summary>
    /// Gets all invoices.
    /// </summary>
    /// <param name="invoice"></param>
    /// <returns></returns>
    Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default);
    /// <summary>
    /// Updates an existing invoice.
    /// </summary>
    /// <param name="invoice"></param>
    void Update(Invoice invoice);
}
