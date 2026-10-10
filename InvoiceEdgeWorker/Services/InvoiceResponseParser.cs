using InvoiceEdgeWorker.Entities;
using System.Globalization;
using System.Text.Json;

namespace InvoiceEdgeWorker.Services;

public sealed class InvoiceResponseParser
{
    public void Apply(string rawJson, Invoice invoice)
    {
        using var document = JsonDocument.Parse(NormalizeJson(rawJson));
        var root = document.RootElement;

        var vendorName = GetRequiredString(root, "vendorName");
        var totalAmount = GetRequiredDecimal(root, "totalAmount");
        var invoiceDate = GetRequiredDate(root, "invoiceDate");
        var lineItems = GetLineItems(root, invoice.Id);

        invoice.VendorName = vendorName;
        invoice.TotalAmount = totalAmount;
        invoice.InvoiceDate = invoiceDate;
        invoice.LineItems.Clear();
        foreach (var lineItem in lineItems)
        {
            invoice.LineItems.Add(lineItem);
        }

        invoice.Status = Enums.ProcessingStatus.ProcessedLocally;
    }

    private static string NormalizeJson(string rawJson)
    {
        var normalized = rawJson.Trim();

        if (!normalized.StartsWith("```", StringComparison.Ordinal))
        {
            return normalized;
        }

        var openingLineEnd = normalized.IndexOf('\n');
        var closingFenceStart = normalized.LastIndexOf("```", StringComparison.Ordinal);
        if (openingLineEnd < 0 || closingFenceStart <= openingLineEnd)
        {
            throw new JsonException("The AI response contained an incomplete Markdown code fence.");
        }

        return normalized[(openingLineEnd + 1)..closingFenceStart].Trim();
    }

    private static List<InvoiceLineItem> GetLineItems(JsonElement root, Guid invoiceId)
    {
        if (!root.TryGetProperty("lineItems", out var lineItemsProperty))
        {
            return new List<InvoiceLineItem>();
        }

        if (lineItemsProperty.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Property 'lineItems' must be an array.");
        }

        var lineItems = new List<InvoiceLineItem>();
        foreach (var item in lineItemsProperty.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Each line item must be an object.");
            }

            lineItems.Add(new InvoiceLineItem
            {
                InvoiceId = invoiceId,
                Description = GetRequiredString(item, "description"),
                Quantity = GetRequiredDecimal(item, "quantity"),
                UnitPrice = GetRequiredDecimal(item, "unitPrice"),
                LineTotal = GetRequiredDecimal(item, "lineTotal")
            });
        }

        return lineItems;
    }

    private static string GetRequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Required string property '{propertyName}' was not found.");
        }

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException($"Required string property '{propertyName}' was empty.");
        }

        return value;
    }

    private static decimal GetRequiredDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            throw new JsonException($"Required numeric property '{propertyName}' was not found.");
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var numericValue))
        {
            return numericValue;
        }

        if (property.ValueKind == JsonValueKind.String &&
            decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var stringValue))
        {
            return stringValue;
        }

        throw new JsonException($"Property '{propertyName}' was not a valid decimal.");
    }

    private static DateTime GetRequiredDate(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String ||
            !DateTime.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new JsonException($"Required date property '{propertyName}' was invalid.");
        }

        return DateTime.SpecifyKind(date, DateTimeKind.Utc);
    }
}
