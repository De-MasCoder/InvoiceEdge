using InvoiceEdgeWorker.Helpers;
using Xunit;

namespace InvoiceEdgeWorker.Tests;

public sealed class FileHashHelperTests
{
    [Fact]
    public async Task ComputeSha256Async_WithIdenticalContent_ReturnsSameHash()
    {
        var firstPath = WriteTemporaryFile("same content");
        var secondPath = WriteTemporaryFile("same content");

        try
        {
            var firstHash = await FileHashHelper.ComputeSha256Async(firstPath);
            var secondHash = await FileHashHelper.ComputeSha256Async(secondPath);

            Assert.Equal(firstHash, secondHash);
            Assert.Equal(64, firstHash.Length);
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    [Fact]
    public async Task ComputeSha256Async_WithDifferentContent_ReturnsDifferentHash()
    {
        var firstPath = WriteTemporaryFile("content one");
        var secondPath = WriteTemporaryFile("content two");

        try
        {
            var firstHash = await FileHashHelper.ComputeSha256Async(firstPath);
            var secondHash = await FileHashHelper.ComputeSha256Async(secondPath);

            Assert.NotEqual(firstHash, secondHash);
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    private static string WriteTemporaryFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"invoice-edge-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(path, content);
        return path;
    }
}
