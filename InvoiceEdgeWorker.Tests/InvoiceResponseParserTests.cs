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
    public void Apply_WithMarkdownJsonFence_ParsesTheContainedJson()
    {
        var invoice = new Invoice();
        const string json = "```json\n{\"vendorName\":\"Contoso\",\"totalAmount\":123.45,\"invoiceDate\":\"2026-10-10\"}\n```";

        _parser.Apply(json, invoice);

        Assert.Equal("Contoso", invoice.VendorName);
        Assert.Equal(123.45m, invoice.TotalAmount);
        Assert.Equal(ProcessingStatus.ProcessedLocally, invoice.Status);
    }

    [Fact]
    public void Apply_WithLineItems_PopulatesAllLineItemFields()
    {
        var invoice = new Invoice();
        const string json = "{\"vendorName\":\"Contoso\",\"totalAmount\":123.45,\"invoiceDate\":\"2026-10-10\",\"lineItems\":[{\"description\":\"Widget\",\"quantity\":2,\"unitPrice\":10.50,\"lineTotal\":21.00},{\"description\":\"Service\",\"quantity\":\"1.5\",\"unitPrice\":\"68.30\",\"lineTotal\":\"102.45\"}]}";

        _parser.Apply(json, invoice);

        Assert.Collection(
            invoice.LineItems,
            item =>
            {
                Assert.Equal("Widget", item.Description);
                Assert.Equal(2m, item.Quantity);
                Assert.Equal(10.50m, item.UnitPrice);
                Assert.Equal(21.00m, item.LineTotal);
                Assert.Equal(invoice.Id, item.InvoiceId);
            },
            item =>
            {
                Assert.Equal("Service", item.Description);
                Assert.Equal(1.5m, item.Quantity);
                Assert.Equal(68.30m, item.UnitPrice);
                Assert.Equal(102.45m, item.LineTotal);
            });
    }

    [Fact]
    public void Apply_WithEmptyLineItems_ReturnsEmptyCollection()
    {
        var invoice = new Invoice();
        const string json = "{\"vendorName\":\"Contoso\",\"totalAmount\":123.45,\"invoiceDate\":\"2026-10-10\",\"lineItems\":[]}";

        _parser.Apply(json, invoice);

        Assert.Empty(invoice.LineItems);
        Assert.Equal(ProcessingStatus.ProcessedLocally, invoice.Status);
    }

    [Fact]
    public void Apply_WithMalformedLineItem_ThrowsAndDoesNotPartiallyApply()
    {
        var invoice = new Invoice
        {
            VendorName = "Original",
            Status = ProcessingStatus.Processing
        };
        const string json = "{\"vendorName\":\"Contoso\",\"totalAmount\":123.45,\"invoiceDate\":\"2026-10-10\",\"lineItems\":[{\"description\":\"Widget\",\"quantity\":2,\"unitPrice\":10.50,\"lineTotal\":\"invalid\"}]}";

        Assert.Throws<JsonException>(() => _parser.Apply(json, invoice));

        Assert.Equal("Original", invoice.VendorName);
        Assert.Equal(ProcessingStatus.Processing, invoice.Status);
        Assert.Empty(invoice.LineItems);
    }

    [Fact]
    public void Apply_WithNonArrayLineItems_ThrowsJsonException()
    {
        var invoice = new Invoice();
        const string json = "{\"vendorName\":\"Contoso\",\"totalAmount\":123.45,\"invoiceDate\":\"2026-10-10\",\"lineItems\":{}}";

        Assert.Throws<JsonException>(() => _parser.Apply(json, invoice));
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
