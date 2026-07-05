using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hermes;

/// <summary>
/// HTTP calls to the LLM server (Ollama). Returns generated text plus input/output
/// token counts. POSTs to {OllamaUrl}/api/generate with stream disabled.
/// </summary>
public sealed class OllamaClient : IDisposable
{
    private readonly HermesConfig _config;
    private readonly HttpClient _http;

    public OllamaClient(HermesConfig config)
    {
        _config = config;
        _http = new HttpClient
        {
            BaseAddress = new Uri(config.OllamaUrl),
            // Local generation of a full spec can take 30-90s+, so be generous.
            Timeout = TimeSpan.FromSeconds(config.RequestTimeoutSeconds)
        };
    }

    /// <summary>
    /// Send <paramref name="prompt"/> to the configured model and return the generated
    /// text along with input/output token counts.
    /// </summary>
    public async Task<GenerationResult> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        var request = new GenerateRequest(_config.Model, prompt, Stream: false);

        using var response = await _http.PostAsJsonAsync("/api/generate", request, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<GenerateResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Ollama returned an empty response body.");

        return new GenerationResult(
            Response: body.Response ?? "",
            InputTokens: body.PromptEvalCount,
            OutputTokens: body.EvalCount,
            // Ollama reports total_duration in nanoseconds; convert to seconds.
            // Missing field -> null (leave duration unrecorded rather than crash).
            OllamaSeconds: body.TotalDuration is long ns ? ns / 1_000_000_000.0 : null);
    }

    public void Dispose() => _http.Dispose();

    // --- Wire types for /api/generate ---

    private sealed record GenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record GenerateResponse
    {
        [JsonPropertyName("response")] public string? Response { get; init; }
        [JsonPropertyName("prompt_eval_count")] public int PromptEvalCount { get; init; }
        [JsonPropertyName("eval_count")] public int EvalCount { get; init; }
        [JsonPropertyName("total_duration")] public long? TotalDuration { get; init; }
    }
}

/// <summary>
/// Text + token accounting returned from a single generation call.
/// <paramref name="OllamaSeconds"/> is the model's own inference time (from the
/// response's total_duration), null if Ollama didn't report it.
/// <paramref name="WallSeconds"/> is total elapsed time for the intent; it is not
/// known to the client, so the caller stamps it after measuring (defaults to 0).
/// </summary>
public readonly record struct GenerationResult(
    string Response,
    int InputTokens,
    int OutputTokens,
    double? OllamaSeconds,
    double WallSeconds = 0);
