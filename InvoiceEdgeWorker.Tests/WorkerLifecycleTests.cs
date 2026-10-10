using InvoiceEdgeWorker.Configuration;
using InvoiceEdgeWorker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace InvoiceEdgeWorker.Tests;

public sealed class WorkerLifecycleTests
{
    [Fact]
    public async Task StartAsync_CreatesConfiguredDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"invoice-edge-{Guid.NewGuid():N}");
        var options = new InvoiceProcessingOptions
        {
            InboundPath = Path.Combine(root, "Inbound"),
            ProcessingPath = Path.Combine(root, "Processing"),
            ProcessedPath = Path.Combine(root, "Processed"),
            FailedPath = Path.Combine(root, "Failed")
        };
        using var cancellation = new CancellationTokenSource();
        var worker = new Worker(
            NullLogger<Worker>.Instance,
            new Mock<IServiceScopeFactory>().Object,
            new StubInvoiceExtractor(),
            options,
            new InvoiceResponseParser());

        try
        {
            await worker.StartAsync(cancellation.Token);
            await Task.Delay(100);

            Assert.True(Directory.Exists(options.InboundPath));
            Assert.True(Directory.Exists(options.ProcessingPath));
            Assert.True(Directory.Exists(options.ProcessedPath));
            Assert.True(Directory.Exists(options.FailedPath));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubInvoiceExtractor : IInvoiceExtractor
    {
        public Task<string> ExtractAsync(byte[] imageBytes, CancellationToken cancellationToken = default) =>
            Task.FromResult("{}");
    }
}
