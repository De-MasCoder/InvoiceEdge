using InvoiceEdgeWorker.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace InvoiceEdgeWorker.Repositories
{
    public class UnitOfWork : IUnitofWork
    {
        private readonly InvoiceDbContext _context;
        public IInvoiceRepository Invoices { get; }

        public UnitOfWork(InvoiceDbContext context)
        {
            _context = context;
            Invoices = new InvoiceRepository(_context);
        }

        public async Task<int> CompleteAsync(CancellationToken cancellationToken = default) =>
            await _context.SaveChangesAsync(cancellationToken);

        public void Dispose() =>
            _context.Dispose();
    }
}
