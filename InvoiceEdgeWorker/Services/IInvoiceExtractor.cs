namespace InvoiceEdgeWorker.Services;

public interface IInvoiceExtractor
{
    /// <summary>
    /// Extracts text from an image represented as a byte array.
    /// </summary>
    /// <param name="imageBytes"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<string> ExtractAsync(byte[] imageBytes, CancellationToken cancellationToken = default);
}
