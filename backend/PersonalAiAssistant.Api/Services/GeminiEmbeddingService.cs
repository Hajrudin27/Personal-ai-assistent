using Google.GenAI;
using Microsoft.Extensions.Configuration;
using System.Linq;

namespace PersonalAiAssistant.Api.Services;

public sealed class GeminiEmbeddingService
{
    private readonly Client _client;

    public GeminiEmbeddingService(IConfiguration config)
    {
        var apiKey = config["GEMINI_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Missing GEMINI_API_KEY environment variable.");

        _client = new Client(apiKey: apiKey);
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(text))
        return [];

    var response = await _client.Models.EmbedContentAsync(
        model: "gemini-embedding-001",
        contents: text
    );

    var values = response?.Embeddings?.FirstOrDefault()?.Values;
    if (values == null || values.Count == 0)
        return [];

    return values.Select(v => (float)v).ToArray();
}

}
