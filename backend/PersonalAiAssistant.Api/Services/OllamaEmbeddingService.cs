using System.Net.Http.Json;

namespace PersonalAiAssistant.Api.Services;

public sealed class OllamaEmbeddingService
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OllamaEmbeddingService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _model = config["OLLAMA_EMBEDDING_MODEL"]
                 ?? config["Ollama:EmbeddingModel"]
                 ?? "nomic-embed-text";
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];


        try
        {
            var req1 = new { model = _model, prompt = text };
            using var res1 = await _http.PostAsJsonAsync("/api/embeddings", req1, ct);
            if (res1.IsSuccessStatusCode)
            {
                var data1 = await res1.Content.ReadFromJsonAsync<EmbeddingsResponse>(cancellationToken: ct);
                if (data1?.embedding is { Count: > 0 })
                    return data1.embedding.Select(v => (float)v).ToArray();
            }
        }
        catch
        {

        }

        var req2 = new { model = _model, input = text };
        using var res2 = await _http.PostAsJsonAsync("/api/embed", req2, ct);
        res2.EnsureSuccessStatusCode();

        var data2 = await res2.Content.ReadFromJsonAsync<EmbedResponse>(cancellationToken: ct);
        var first = data2?.embeddings?.FirstOrDefault();
        if (first == null || first.Count == 0)
            return [];

        return first.Select(v => (float)v).ToArray();
    }

    private sealed class EmbeddingsResponse
    {
        public List<double>? embedding { get; set; }
    }

    private sealed class EmbedResponse
    {
        public List<List<double>>? embeddings { get; set; }
    }
}
