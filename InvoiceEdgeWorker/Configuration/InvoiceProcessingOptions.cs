namespace InvoiceEdgeWorker.Configuration;

public sealed class InvoiceProcessingOptions
{
    public string InboundPath { get; set; } = @"C:\InvoiceDropZone\Inbound";
    public string ProcessingPath { get; set; } = @"C:\InvoiceDropZone\Processing";
    public string ProcessedPath { get; set; } = @"C:\InvoiceDropZone\Processed";
    public string FailedPath { get; set; } = @"C:\InvoiceDropZone\Failed";
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "qwen2.5vl:7b";
}
