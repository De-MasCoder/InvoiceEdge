using System;
using System.Collections.Generic;
using System.Text;

namespace InvoiceEdgeWorker.Services
{
    public interface IStorageService
    {
        /// <summary>
        /// Uploads a local file to cloud storage asynchronously.
        /// </summary>
        /// <returns>The unique cloud storage URL or key identifier.</returns>
        Task<string> UploadInvoiceAsync(string localFilePath, string fileName, CancellationToken cancellationToken);
    }
}
