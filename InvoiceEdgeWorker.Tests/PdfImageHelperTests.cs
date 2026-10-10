using InvoiceEdgeWorker.Helpers;
using System.Text;
using Xunit;

namespace InvoiceEdgeWorker.Tests;

public sealed class PdfImageHelperTests
{
    [Fact]
    public void ConvertFirstPageToImageBytes_WithValidPdf_ReturnsPngBytes()
    {
        var path = WriteTemporaryPdf(pageCount: 1);

        try
        {
            var result = PdfImageHelper.ConvertFirstPageToImageBytes(path);

            Assert.NotEmpty(result);
            Assert.Equal(0x89, result[0]);
            Assert.Equal((byte)'P', result[1]);
            Assert.Equal((byte)'N', result[2]);
            Assert.Equal((byte)'G', result[3]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConvertFirstPageToImageBytes_WithMultiplePages_RendersFirstPage()
    {
        var path = WriteTemporaryPdf(pageCount: 2);

        try
        {
            var result = PdfImageHelper.ConvertFirstPageToImageBytes(path);

            Assert.NotEmpty(result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConvertFirstPageToImageBytes_WithInvalidPdf_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        File.WriteAllText(path, "not a PDF");

        try
        {
            Assert.ThrowsAny<Exception>(() => PdfImageHelper.ConvertFirstPageToImageBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConvertFirstPageToImageBytes_WithMissingFile_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");

        Assert.ThrowsAny<Exception>(() => PdfImageHelper.ConvertFirstPageToImageBytes(path));
    }

    private static string WriteTemporaryPdf(int pageCount)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        var pdf = CreatePdf(pageCount);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(pdf));
        return path;
    }

    private static string CreatePdf(int pageCount)
    {
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        var pageObjectNumbers = Enumerable.Range(3, pageCount).ToArray();
        var contentObjectNumbers = Enumerable.Range(3 + pageCount, pageCount).ToArray();

        AppendObject(builder, offsets, 1, "<< /Type /Catalog /Pages 2 0 R >>");
        AppendObject(builder, offsets, 2,
            $"<< /Type /Pages /Kids [{string.Join(" ", pageObjectNumbers.Select(number => $"{number} 0 R"))}] /Count {pageCount} >>");

        for (var index = 0; index < pageCount; index++)
        {
            AppendObject(builder, offsets, pageObjectNumbers[index],
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents {contentObjectNumbers[index]} 0 R /Resources << >> >>");
        }

        for (var index = 0; index < pageCount; index++)
        {
            var content = "q Q";
            AppendObject(builder, offsets, contentObjectNumbers[index], $"<< /Length {content.Length} >> stream\n{content}\nendstream");
        }

        var xrefOffset = builder.Length;
        var objectCount = offsets.Count;
        builder.Append($"xref\n0 {objectCount}\n");
        builder.Append("0000000000 65535 f \n");
        for (var index = 1; index < objectCount; index++)
        {
            builder.Append($"{offsets[index]:D10} 00000 n \n");
        }

        builder.Append($"trailer\n<< /Size {objectCount} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return builder.ToString();
    }

    private static void AppendObject(StringBuilder builder, List<int> offsets, int objectNumber, string content)
    {
        while (offsets.Count <= objectNumber)
        {
            offsets.Add(0);
        }

        offsets[objectNumber] = builder.Length;
        builder.Append($"{objectNumber} 0 obj\n{content}\nendobj\n");
    }
}
