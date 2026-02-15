using System.Net.Http.Json;

namespace PersonalAiAssistant.Api.Services;

public sealed class OllamaChatService
{
    private readonly HttpClient _http;

    public OllamaChatService(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> ChatAsync(string system, string user, CancellationToken ct = default)
    {
        var req = new
        {
            model = "llama3.1:8b",
            stream = false,
            options = new
            {
                temperature = 0.1
            },
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        };

        using var res = await _http.PostAsJsonAsync("/api/chat", req, ct);
        res.EnsureSuccessStatusCode();

        var data = await res.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: ct);
        return data?.message?.content ?? "";
    }

    private sealed class OllamaChatResponse
    {
        public OllamaMessage? message { get; set; }
    }

    private sealed class OllamaMessage
    {
        public string? role { get; set; }
        public string? content { get; set; }
    }
}
