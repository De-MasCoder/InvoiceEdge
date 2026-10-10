using InvoiceEdgeWorker.Entities;
using InvoiceEdgeWorker.Enums;
using InvoiceEdgeWorker.Services;
using System.Text.Json;
using Xunit;

namespace InvoiceEdgeWorker.Tests;

public sealed class InvoiceResponseParserTests
{
    private readonly InvoiceResponseParser _parser = new();

    [Fact]
    public void Apply_WithValidJson_PopulatesInvoiceAndMarksItProcessed()
    {
        var invoice = new Invoice();
        const string json = "{\"vendorName\":\"Contoso\",\"totalAmount\":123.45,\"invoiceDate\":\"2026-10-10\"}";

        _parser.Apply(json, invoice);

        Assert.Equal("Contoso", invoice.VendorName);
        Assert.Equal(123.45m, invoice.TotalAmount);
        Assert.Equal(new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), invoice.InvoiceDate);
        Assert.Equal(ProcessingStatus.ProcessedLocally, invoice.Status);
    }

    [Fact]
    public void Apply_WithStringAmount_PopulatesDecimalValue()
    {
        var invoice = new Invoice();
        const string json = "{\"vendorName\":\"Fabrikam\",\"totalAmount\":\"987.65\",\"invoiceDate\":\"2026-01-31\"}";

        _parser.Apply(json, invoice);

        Assert.Equal(987.65m, invoice.TotalAmount);
        Assert.Equal(ProcessingStatus.ProcessedLocally, invoice.Status);
    }

    [Fact]
    public void Apply_WithMalformedJson_ThrowsJsonException()
    {
        var invoice = new Invoice();

        Assert.ThrowsAny<JsonException>(() => _parser.Apply("{malformed", invoice));
    }

    [Fact]
    public void Apply_WhenRequiredFieldIsMissing_ThrowsJsonException()
    {
        var invoice = new Invoice();
        const string json = "{\"vendorName\":\"Contoso\",\"invoiceDate\":\"2026-10-10\"}";

        Assert.Throws<JsonException>(() => _parser.Apply(json, invoice));
    }

    [Fact]
    public void Apply_WhenAmountIsInvalid_ThrowsJsonException()
    {
        var invoice = new Invoice();
        const string json = "{\"vendorName\":\"Contoso\",\"totalAmount\":\"not-a-number\",\"invoiceDate\":\"2026-10-10\"}";

        Assert.Throws<JsonException>(() => _parser.Apply(json, invoice));
    }

    [Fact]
    public void Apply_WhenDateIsInvalid_ThrowsJsonException()
    {
        var invoice = new Invoice();
        const string json = "{\"vendorName\":\"Contoso\",\"totalAmount\":123.45,\"invoiceDate\":\"not-a-date\"}";

        Assert.Throws<JsonException>(() => _parser.Apply(json, invoice));
    }
}
