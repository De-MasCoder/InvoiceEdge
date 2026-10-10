using InvoiceEdgeWorker.Entities;
using System.Globalization;
using System.Text.Json;

namespace InvoiceEdgeWorker.Services;

public sealed class InvoiceResponseParser
{
    public void Apply(string rawJson, Invoice invoice)
    {
        using var document = JsonDocument.Parse(rawJson);
        var root = document.RootElement;

        invoice.VendorName = GetRequiredString(root, "vendorName");
        invoice.TotalAmount = GetRequiredDecimal(root, "totalAmount");
        invoice.InvoiceDate = GetRequiredDate(root, "invoiceDate");
        invoice.Status = Enums.ProcessingStatus.ProcessedLocally;
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
