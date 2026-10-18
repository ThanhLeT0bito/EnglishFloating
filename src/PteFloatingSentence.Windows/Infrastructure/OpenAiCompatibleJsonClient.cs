using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PteFloatingSentence.Windows.Infrastructure;

/// <summary>
/// OpenAI-compatible chat completions (Groq by default). Models that support strict structured outputs get
/// <c>json_schema</c>; others, and the relaxed retry after HTTP 400, use <c>json_object</c> with the schema in the prompt.
/// </summary>
public sealed class OpenAiCompatibleJsonClient : JsonLlmClientBase
{
    public const string GroqEndpoint = "https://api.groq.com/openai/v1/chat/completions";

    /// <summary>gpt-oss-120b supports strict JSON schema on Groq; Llama 3.3 70B is the fallback.</summary>
    public static readonly IReadOnlyList<string> DefaultGroqModels = ["openai/gpt-oss-120b", "llama-3.3-70b-versatile"];

    private static readonly HashSet<string> StrictSchemaModels =
        new(["openai/gpt-oss-120b", "openai/gpt-oss-20b"], StringComparer.OrdinalIgnoreCase);

    private readonly string _endpoint;

    public OpenAiCompatibleJsonClient(
        string providerName,
        string endpoint,
        Func<string?> keyProvider,
        IReadOnlyList<string> models,
        HttpClient? client = null,
        TimeSpan? timeout = null,
        TimeSpan? attemptTimeout = null)
        : base(providerName, keyProvider, client, models, timeout, attemptTimeout)
    {
        _endpoint = endpoint;
    }

    public static OpenAiCompatibleJsonClient ForGroq(
        Func<string?> keyProvider,
        HttpClient? client = null,
        IReadOnlyList<string>? models = null,
        TimeSpan? timeout = null,
        TimeSpan? attemptTimeout = null) =>
        new("Groq", GroqEndpoint, keyProvider, models is { Count: > 0 } ? models : DefaultGroqModels, client, timeout, attemptTimeout);

    protected override HttpRequestMessage CreateRequest(
        string model,
        string key,
        string systemInstruction,
        string userText,
        object responseSchema,
        double? temperature,
        bool relaxed)
    {
        var strict = !relaxed && StrictSchemaModels.Contains(model);
        var system = strict ? systemInstruction : systemInstruction + "\n\n" + SchemaInstruction(responseSchema);
        var payload = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = userText }
            },
            ["response_format"] = strict
                ? new
                {
                    type = "json_schema",
                    json_schema = new { name = "response", strict = true, schema = JsonSchemaConverter.ToStrict(responseSchema) }
                }
                : new { type = "json_object" }
        };
        if (temperature is not null)
            payload["temperature"] = temperature.Value;
        if (!relaxed && model.StartsWith("openai/gpt-oss", StringComparison.OrdinalIgnoreCase))
        {
            payload["reasoning_effort"] = "low";
            payload["include_reasoning"] = false;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = JsonContent(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }

    protected override string? ExtractText(JsonElement root) =>
        root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
}
