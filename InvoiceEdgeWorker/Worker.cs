using InvoiceEdgeWorker.Entities;
using InvoiceEdgeWorker.Enums;
using InvoiceEdgeWorker.Configuration;
using InvoiceEdgeWorker.Helpers;
using InvoiceEdgeWorker.Repositories;
using InvoiceEdgeWorker.Services;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;

namespace InvoiceEdgeWorker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IInvoiceExtractor _invoiceExtractor;
    private readonly InvoiceResponseParser _responseParser;

    private readonly string _inboundPath;
    private readonly string _processingPath;
    private readonly string _processedPath;
    private readonly string _failedPath;

    public Worker(
        ILogger<Worker> logger,
        IServiceScopeFactory scopeFactory,
        IInvoiceExtractor invoiceExtractor,
        InvoiceProcessingOptions options,
        InvoiceResponseParser responseParser)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _invoiceExtractor = invoiceExtractor;
        _responseParser = responseParser;
        _inboundPath = options.InboundPath;
        _processingPath = options.ProcessingPath;
        _processedPath = options.ProcessedPath;
        _failedPath = options.FailedPath;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        Directory.CreateDirectory(_inboundPath);
        Directory.CreateDirectory(_processingPath);
        Directory.CreateDirectory(_processedPath);
        Directory.CreateDirectory(_failedPath);

        _logger.LogInformation("🚀 EdgeInvoice Service Initialised. Monitoring path: {_inboundPath}", _inboundPath);

        // System Directory Event Watcher
        using var watcher = new FileSystemWatcher(_inboundPath)
        {
            Filter = "*.*",
            EnableRaisingEvents = true
        };

        // Register a resilient, fire-and-forget handler that logs failures
        RegisterWatcher(watcher, stoppingToken);

        // Keep the application token alive infinitely
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

    }

    private void RegisterWatcher(FileSystemWatcher watcher, CancellationToken token)
    {
        watcher.Created += (sender, e) =>
        {
            // Run the processing on the thread-pool and capture/log any exceptions
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessFileAsync(e.FullPath, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Expected during shutdown - ignore
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception while processing file '{fileName}'", e.Name);
                }
            }, token);
        };
    }

    private async Task ProcessFileAsync(string filePath, CancellationToken cancellationToken)
    {
        string fileName = Path.GetFileName(filePath);
        string targetProcessingPath = Path.Combine(_processingPath, fileName);

        // Create an isolated database connection execution scope for this file instance
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitofWork>();

        try
        {
            _logger.LogInformation("📂 New document detected: {fileName}. Ingesting...", fileName);

            // Allow disk IO stream to finish flushing file safely
            await Task.Delay(1500, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(filePath)) return;

            // Ensure we do not clobber an existing file in the processing folder
            if (File.Exists(targetProcessingPath))
            {
                var uniqueName = $"{Path.GetFileNameWithoutExtension(fileName)}_{Guid.NewGuid()}{Path.GetExtension(fileName)}";
                targetProcessingPath = Path.Combine(_processingPath, uniqueName);
            }

            // Move the file out of Inbound immediately to avoid double-processing collisions
            try
            {
                File.Move(filePath, targetProcessingPath);
            }
            catch (Exception moveEx)
            {
                _logger.LogWarning(moveEx, "Could not move incoming file '{fileName}' to processing folder. Skipping.", fileName);
                return;
            }

            // 3. Persist the initial discovery state to PostgreSQL
            var invoiceEntity = new Invoice
            {
                FileName = fileName,
                FilePath = targetProcessingPath,
                Status = ProcessingStatus.Processing
            };
            await unitOfWork.Invoices.AddAsync(invoiceEntity);
            await unitOfWork.CompleteAsync();

            _logger.LogInformation("🤖 Activating local GPU extraction engine for {fileName}...", fileName);

            // 4. Transform local file bytes to raw data payloads for Ollama
            byte[] fileBytes;
            string fileExtension = Path.GetExtension(targetProcessingPath).ToLower();

            if (fileExtension == ".pdf")
            {
                // Call your new private function here to turn the PDF page into a PNG in memory
                _logger.LogInformation("📄 PDF detected. Rasterizing page 1 to image bytes...");
                fileBytes = PdfImageHelper.ConvertFirstPageToImageBytes(targetProcessingPath);
            }
            else
            {
                // It's already a native image (.png, .jpg, .jpeg)
                _logger.LogInformation("🖼️ Native image detected. Reading bytes directly...");
                fileBytes = await File.ReadAllBytesAsync(targetProcessingPath, cancellationToken);
            }
            // Stream response out of your local GPU memory
            string rawResponse = await _invoiceExtractor.ExtractAsync(fileBytes, cancellationToken);

            _logger.LogDebug("Raw AI extraction output: {rawResponse}", rawResponse);

            // 5. Parse, Validate, and update state entity mapping
            invoiceEntity.RawJson = rawResponse;
            invoiceEntity.UpdatedAt = DateTime.UtcNow;

            try
            {
                _responseParser.Apply(rawResponse, invoiceEntity);

                // Route file to completed workspace (preserve original file name when possible)
                var processedTarget = Path.Combine(_processedPath, Path.GetFileName(targetProcessingPath));
                File.Move(targetProcessingPath, processedTarget);
                _logger.LogInformation("✅ Invoice {fileName} parsed and saved successfully.", fileName);
            }
            catch (Exception jsonEx)
            {
                _logger.LogWarning(jsonEx, "⚠️ AI output parsing failed for {fileName}. Flagging for Review.", fileName);
                invoiceEntity.Status = ProcessingStatus.RequiresReview;
                invoiceEntity.ErrorMessage = $"JSON Structural Mismatch: {jsonEx.Message}";

                try
                {
                    var processedTarget = Path.Combine(_processedPath, Path.GetFileName(targetProcessingPath));
                    File.Move(targetProcessingPath, processedTarget);
                }
                catch (Exception fileMoveEx)
                {
                    _logger.LogError(fileMoveEx, "Failed to move '{fileName}' to processed folder after parse error.", fileName);
                }
            }

            // Sync structural changes into local relational tables
            unitOfWork.Invoices.Update(invoiceEntity);
            await unitOfWork.CompleteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Fatal error during asynchronous execution workflow for file: {fileName}", fileName);

            try
            {
                if (File.Exists(targetProcessingPath))
                {
                    File.Move(targetProcessingPath, Path.Combine(_failedPath, fileName));
                }
            }
            catch (Exception fileMoveEx)
            {
                _logger.LogCritical(fileMoveEx, "Could not isolate broken file to failed folder.");
            }
        }
    }

}
