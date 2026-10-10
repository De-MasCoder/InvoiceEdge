using InvoiceEdgeWorker.Entities;
using InvoiceEdgeWorker.Enums;
using InvoiceEdgeWorker.Helpers;
using InvoiceEdgeWorker.Repositories;
using OllamaSharp;
using OllamaSharp.Models.Chat;
using System.Text.Json;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;

namespace InvoiceEdgeWorker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OllamaApiClient _ollamaClient;

    private readonly string _inboundPath = @"C:\InvoiceDropZone\Inbound";
    private readonly string _processingPath = @"C:\InvoiceDropZone\Processing";
    private readonly string _processedPath = @"C:\InvoiceDropZone\Processed";
    private readonly string _failedPath = @"C:\InvoiceDropZone\Failed";

    public Worker(ILogger<Worker> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;

        // Connect to local Ollama instance running Qwen2.5-VL
        _ollamaClient = new OllamaApiClient(new Uri("http://localhost:11434"))
        {
            SelectedModel = "qwen2.5vl:7b"
        };
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
            string base64Image = Convert.ToBase64String(fileBytes);

            var chatRequest = new ChatRequest
            {
                Messages = new List<Message>
                {
                    new Message
                    {
                        Role = ChatRole.User,
                        Content = "Extract data from this invoice. Respond ONLY with a valid, clean JSON object containing: 'vendorName' (string), 'totalAmount' (number), and 'invoiceDate' (string format YYYY-MM-DD). Do not wrap inside markdown blocks.",
                        Images = new string[] { base64Image }
                    }
                }
            };

            // Stream response out of your local GPU memory
            string rawResponse = "";
            await foreach (var responseStream in _ollamaClient.ChatAsync(chatRequest, cancellationToken))
            {
                rawResponse += responseStream.Message.Content;
            }

            _logger.LogDebug("Raw AI extraction output: {rawResponse}", rawResponse);

            // 5. Parse, Validate, and update state entity mapping
            invoiceEntity.RawJson = rawResponse;
            invoiceEntity.UpdatedAt = DateTime.UtcNow;

            try
            {
                using var jsonDoc = JsonDocument.Parse(rawResponse);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("vendorName", out var vendorProp) && vendorProp.ValueKind == JsonValueKind.String)
                {
                    invoiceEntity.VendorName = vendorProp.GetString();
                }

                if (root.TryGetProperty("totalAmount", out var amountProp) && (amountProp.ValueKind == JsonValueKind.Number || amountProp.ValueKind == JsonValueKind.String))
                {
                    if (amountProp.ValueKind == JsonValueKind.Number && amountProp.TryGetDecimal(out var dec))
                    {
                        invoiceEntity.TotalAmount = dec;
                    }
                    else if (amountProp.ValueKind == JsonValueKind.String && Decimal.TryParse(amountProp.GetString(), out var decFromString))
                    {
                        invoiceEntity.TotalAmount = decFromString;
                    }
                }

                if (root.TryGetProperty("invoiceDate", out var dateProp) && dateProp.ValueKind == JsonValueKind.String)
                {
                    var dateStr = dateProp.GetString();
                    if (DateTime.TryParse(dateStr, out DateTime parsedDate))
                    {
                        invoiceEntity.InvoiceDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
                    }
                }

                invoiceEntity.Status = ProcessingStatus.ProcessedLocally;

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
