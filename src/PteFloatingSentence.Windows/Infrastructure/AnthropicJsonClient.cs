using System.Net.Http;
using System.Text.Json;

namespace PteFloatingSentence.Windows.Infrastructure;

/// <summary>
/// Anthropic Messages API (Claude) with structured JSON output via <c>output_config.format</c>.
/// The relaxed retry after HTTP 400 drops the structured format and puts the schema in the system prompt.
/// </summary>
public sealed class AnthropicJsonClient : JsonLlmClientBase
{
    public const string Endpoint = "https://api.anthropic.com/v1/messages";
    public const string ApiVersion = "2023-06-01";
    public const int MaxTokens = 4096;

    /// <summary>Haiku is the fastest and cheapest Claude model.</summary>
    public static readonly IReadOnlyList<string> DefaultModels = ["claude-haiku-5-5", "claude-haiku-4-5"];

    public AnthropicJsonClient(
        Func<string?> keyProvider,
        HttpClient? client = null,
        IReadOnlyList<string>? models = null,
        TimeSpan? timeout = null,
        TimeSpan? attemptTimeout = null)
        : base("Claude", keyProvider, client, models is { Count: > 0 } ? models : DefaultModels, timeout, attemptTimeout)
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
        var payload = new Dictionary<string, object>
        {
            ["model"] = model,
            ["max_tokens"] = MaxTokens,
            ["system"] = relaxed ? systemInstruction + "\n\n" + SchemaInstruction(responseSchema) : systemInstruction,
            ["messages"] = new object[] { new { role = "user", content = userText } }
        };
        if (temperature is not null)
            payload["temperature"] = Math.Clamp(temperature.Value, 0, 1);
        if (!relaxed)
        {
            payload["output_config"] = new
            {
                format = new { type = "json_schema", schema = JsonSchemaConverter.ToStrict(responseSchema) }
            };
        }

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent(payload) };
        request.Headers.Add("x-api-key", key);
        request.Headers.Add("anthropic-version", ApiVersion);
        return request;
    }

    protected override string? ExtractText(JsonElement root)
    {
        if (root.TryGetProperty("stop_reason", out var stop) && stop.ValueKind == JsonValueKind.String
            && stop.GetString() is "refusal" or "max_tokens")
            return null;

        return string.Concat(root.GetProperty("content").EnumerateArray()
            .Where(block => block.TryGetProperty("type", out var type) && type.GetString() == "text")
            .Select(block => block.GetProperty("text").GetString()));
    }
}
