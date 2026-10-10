using InvoiceEdgeWorker.Data;
using InvoiceEdgeWorker.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvoiceEdgeWorker.Repositories
{
    public class InvoiceRepository : IInvoiceRepository
    {
        private readonly InvoiceDbContext _context;

        public InvoiceRepository(InvoiceDbContext context)
        {
            _context = context;
        }

        public async Task<Invoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            await _context.Invoices.FindAsync(id, cancellationToken);

        public async Task<Invoice?> GetByFileNameAsync(string fileName, CancellationToken cancellationToken = default) =>
            await _context.Invoices.FirstOrDefaultAsync(i => i.FileName == fileName, cancellationToken);

        public async Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default) =>
            await _context.Invoices.AddAsync(invoice, cancellationToken);

        public void Update(Invoice invoice) =>
            _context.Invoices.Update(invoice);
    }
}
