using InvoiceEdgeWorker.Entities;
using InvoiceEdgeWorker.Enums;
using InvoiceEdgeWorker.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using System.IO;
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

        // Fire and forget handler to keep the core directory stream unblocked
        watcher.Created += (sender, e) =>
        {
            _ = Task.Run(() => ProcessFileAsync(e.FullPath, stoppingToken), stoppingToken);
        };

        // Keep the application token alive infinitely
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
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
            await Task.Delay(1500, cancellationToken);

            if (!File.Exists(filePath)) return;

            // Move the file out of Inbound immediately to avoid double-processing collisions
            File.Move(filePath, targetProcessingPath);

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
                fileBytes = ConvertPdfPageToImageBytes(targetProcessingPath);
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

                invoiceEntity.VendorName = root.GetProperty("vendorName").GetString();
                invoiceEntity.TotalAmount = root.GetProperty("totalAmount").GetDecimal();

                if (DateTime.TryParse(root.GetProperty("invoiceDate").GetString(), out DateTime parsedDate))
                {
                    invoiceEntity.InvoiceDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
                }

                invoiceEntity.Status = ProcessingStatus.ProcessedLocally;

                // Route file to completed workspace
                File.Move(targetProcessingPath, Path.Combine(_processedPath, fileName));
                _logger.LogInformation("✅ Invoice {fileName} parsed and saved successfully.", fileName);
            }
            catch (Exception jsonEx)
            {
                _logger.LogWarning("⚠️ AI output parsing failed for {fileName}. Flagging for Review. Details: {msg}", fileName, jsonEx.Message);
                invoiceEntity.Status = ProcessingStatus.RequiresReview;
                invoiceEntity.ErrorMessage = $"JSON Structural Mismatch: {jsonEx.Message}";

                File.Move(targetProcessingPath, Path.Combine(_processedPath, fileName)); // Keep it in processed since we parsed text, but needs eyes
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

    private byte[] ConvertPdfPageToImageBytes(string pdfPath)
    {
        // 1. Initialize the document with the Skia layout parsing engine options
        using var document = PdfDocument.Open(pdfPath, SkiaRenderingParsingOptions.Instance);
        if (document.NumberOfPages == 0)
        {
            throw new InvalidOperationException("The provided PDF file contains no readable pages.");
        }

        // 2. Register the Skia page factories onto the document instance
        document.AddSkiaPageFactory();

        // 3. Render page 1 directly to an internal PNG MemoryStream at 2.0x scale (300+ DPI)
        // Pass 10 for compression/quality balancing
        using MemoryStream pngStream = document.GetPageAsPng(
            pageNumber: 1,
            scale: 2.0f
        );

        // 4. Return the pure byte array right into your base64 encoder
        return pngStream.ToArray();
    }

}
