using System.Net.Http;
using System.Text.Json;

namespace PteFloatingSentence.Windows.Infrastructure;

/// <summary>
/// Google Gemini generateContent with a JSON response schema. Asks for low thinking to keep latency down;
/// the relaxed retry after HTTP 400 drops the thinking setting.
/// </summary>
public sealed class GeminiJsonClient : JsonLlmClientBase
{
    public static readonly IReadOnlyList<string> DefaultModels =
        ["gemini-3.8-flash", "gemini-3.6-flash", "gemini-3.5-flash", "gemini-3.5-flash-lite"];

    public const string MissingKeyMessage = "Configure your Gemini API key in Settings first.";
    public const string TimeoutMessage = "Gemini took too long to respond. Please try again.";
    public const string ConnectionMessage = "Could not connect to Gemini. Check your connection and try again.";
    public const string InvalidResponseMessage = "Gemini returned an invalid response. Please try again.";

    /// <summary>Lowest thinking level accepted by every default model (3.8/3.7 Flash reject "minimal").</summary>
    public const string ThinkingLevel = "low";

    /// <param name="timeout">Total time for the whole request, across all models.</param>
    /// <param name="attemptTimeout">Time one model may take before the next model is tried.</param>
    public GeminiJsonClient(
        Func<string?> keyProvider,
        HttpClient? client = null,
        IReadOnlyList<string>? models = null,
        TimeSpan? timeout = null,
        TimeSpan? attemptTimeout = null)
        : base("Gemini", keyProvider, client, models is { Count: > 0 } ? models : DefaultModels, timeout, attemptTimeout)
    {
    }

    protected override HttpRequestMessage CreateRequest(
        string model,
        string key,
        string systemInstruction,
        string userText,
        object responseSchema,
        double? temperature,
        bool relaxed)
    {
        var generationConfig = new Dictionary<string, object>
        {
            ["responseMimeType"] = "application/json",
            ["responseSchema"] = responseSchema
        };
        if (temperature is not null)
            generationConfig["temperature"] = temperature.Value;
        if (!relaxed)
            generationConfig["thinkingConfig"] = new { thinkingLevel = ThinkingLevel };

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent")
        {
            Content = JsonContent(new
            {
                systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
                contents = new[] { new { role = "user", parts = new[] { new { text = userText } } } },
                generationConfig
            })
        };
        request.Headers.Add("x-goog-api-key", key);
        return request;
    }

    protected override string? ExtractText(JsonElement root)
    {
        var parts = root.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts");
        return string.Concat(parts.EnumerateArray()
            .Where(part => !part.TryGetProperty("thought", out var thought) || thought.ValueKind != JsonValueKind.True)
            .Where(part => part.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String)
            .Select(part => part.GetProperty("text").GetString()));
    }
}
