using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace InvoiceEdgeWorker.Services;

public sealed class OllamaInvoiceExtractor : IInvoiceExtractor
{
    private const string ExtractionPrompt = "Extract data from this invoice. Respond ONLY with a valid, clean JSON object containing: 'vendorName' (string), 'totalAmount' (number), and 'invoiceDate' (string format YYYY-MM-DD). Do not wrap inside markdown blocks.";
    private readonly OllamaApiClient _client;

    public OllamaInvoiceExtractor(Uri endpoint, string model)
    {
        _client = new OllamaApiClient(endpoint)
        {
            SelectedModel = model
        };
    }

    public async Task<string> ExtractAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        var request = new ChatRequest
        {
            Messages = new List<Message>
            {
                new()
                {
                    Role = ChatRole.User,
                    Content = ExtractionPrompt,
                    Images = new[] { Convert.ToBase64String(imageBytes) }
                }
            }
        };

        var response = new System.Text.StringBuilder();
        await foreach (var item in _client.ChatAsync(request, cancellationToken))
        {
            response.Append(item.Message.Content);
        }

        return response.ToString();
    }
}
