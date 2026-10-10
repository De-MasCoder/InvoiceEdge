using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;

namespace InvoiceEdgeWorker.Helpers;

public static class PdfImageHelper
{
    public static byte[] ConvertFirstPageToImageBytes(string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath, SkiaRenderingParsingOptions.Instance);
        if (document.NumberOfPages == 0)
        {
            throw new InvalidOperationException("The provided PDF file contains no readable pages.");
        }

        document.AddSkiaPageFactory();

        using var pngStream = document.GetPageAsPng(
            pageNumber: 1,
            scale: 2.0f);

        return pngStream.ToArray();
    }
}