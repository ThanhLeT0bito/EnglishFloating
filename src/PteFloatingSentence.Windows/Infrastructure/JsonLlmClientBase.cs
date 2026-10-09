using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows.Infrastructure;

/// <summary>
/// Shared request loop for AI providers: tries each model in turn with its own time slot, moves on when a model is
/// slow or returns a transient status, retries a model once with a simpler request after HTTP 400, caps the response
/// size and total time, and turns failures into <see cref="InvalidOperationException"/> with a user-facing message.
/// Caller cancellation stays an <see cref="OperationCanceledException"/>. The API key never appears in messages.
/// </summary>
public abstract class JsonLlmClientBase : IJsonLlmClient, IDisposable
{
    public const int MaxResponseBytes = 64 * 1024;

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan DefaultAttemptTimeout = TimeSpan.FromSeconds(20);

    private readonly Func<string?> _keyProvider;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _attemptTimeout;

    protected JsonLlmClientBase(
        string providerName,
        Func<string?> keyProvider,
        HttpClient? client,
        IReadOnlyList<string> models,
        TimeSpan? timeout,
        TimeSpan? attemptTimeout)
    {
        ProviderName = providerName;
        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        _ownsClient = client is null;
        _client = client ?? new HttpClient();
        Models = models;
        _timeout = timeout ?? DefaultTimeout;
        _attemptTimeout = attemptTimeout ?? DefaultAttemptTimeout;
    }

    public string ProviderName { get; }

    public IReadOnlyList<string> Models { get; }

    public bool HasKey => !string.IsNullOrWhiteSpace(_keyProvider());

    public static string MissingKeyText(string provider) => $"Configure your {provider} API key in Settings first.";
    public static string TimeoutText(string provider) => $"{provider} took too long to respond. Please try again.";
    public static string ConnectionText(string provider) => $"Could not connect to {provider}. Check your connection and try again.";
    public static string InvalidResponseText(string provider) => $"{provider} returned an invalid response. Please try again.";

    public async Task<string> GenerateJsonAsync(
        string systemInstruction,
        string userText,
        object responseSchema,
        double? temperature = null,
        CancellationToken cancellationToken = default) =>
        (await GenerateJsonWithInfoAsync(systemInstruction, userText, responseSchema, temperature, cancellationToken)).Text;

    public async Task<JsonLlmResult> GenerateJsonWithInfoAsync(
        string systemInstruction,
        string userText,
        object responseSchema,
        double? temperature = null,
        CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        var attempts = 0;
        var key = _keyProvider()?.Trim();
        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException(MissingKeyText(ProviderName));

        using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(_timeout);
        HttpStatusCode? lastTransientStatus = null;
        var modelTimedOut = false;
        try
        {
            foreach (var model in Models)
            {
                for (var pass = 0; pass < 2; pass++)
                {
                    var relaxed = pass == 1;
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
                    attempt.CancelAfter(_attemptTimeout);

                    HttpStatusCode status;
                    string? body;
                    attempts++;
                    try
                    {
                        using var request = CreateRequest(model, key, systemInstruction, userText, responseSchema, temperature, relaxed);
                        (status, body) = await SendOnceAsync(request, attempt.Token);
                    }
                    catch (OperationCanceledException) when (attempt.IsCancellationRequested && !total.IsCancellationRequested)
                    {
                        // This model is too slow right now; give the next one a chance.
                        modelTimedOut = true;
                        break;
                    }

                    if (status == HttpStatusCode.BadRequest && !relaxed)
                        continue;

                    if (IsTransient(status))
                    {
                        lastTransientStatus = status;
                        break;
                    }

                    if (body is null)
                    {
                        throw new InvalidOperationException(status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                            ? $"{ProviderName} rejected the API key (HTTP {(int)status}). Check it in Settings."
                            : $"{ProviderName} request failed (HTTP {(int)status}). Try again later.");
                    }

                    return new JsonLlmResult(ExtractJson(body), new AiCallInfo(ProviderName, model, watch.Elapsed, attempts));
                }
            }

            if (lastTransientStatus is HttpStatusCode.TooManyRequests)
                throw new InvalidOperationException($"{ProviderName} is busy or your quota is used up (HTTP 429). Try again in a moment.");
            if (lastTransientStatus is not null)
                throw new InvalidOperationException($"{ProviderName} is unavailable right now (HTTP {(int)lastTransientStatus.Value}). Try again later.");
            if (modelTimedOut)
                throw new InvalidOperationException(TimeoutText(ProviderName));
            throw new InvalidOperationException($"No supported {ProviderName} model is available. Try again later.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(TimeoutText(ProviderName));
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException(ConnectionText(ProviderName));
        }
    }

    /// <summary>
    /// Builds the HTTP request for one model. <paramref name="relaxed"/> is a simpler variant sent once after an
    /// HTTP 400, for models that reject an optional feature such as a thinking level or strict schema mode.
    /// </summary>
    protected abstract HttpRequestMessage CreateRequest(
        string model,
        string key,
        string systemInstruction,
        string userText,
        object responseSchema,
        double? temperature,
        bool relaxed);

    /// <summary>Returns the model's text from a successful response body, or null when there is none.</summary>
    protected abstract string? ExtractText(JsonElement root);

    protected virtual bool IsTransient(HttpStatusCode status) => status is HttpStatusCode.NotFound
        or HttpStatusCode.TooManyRequests
        or HttpStatusCode.InternalServerError
        or HttpStatusCode.BadGateway
        or HttpStatusCode.ServiceUnavailable
        or HttpStatusCode.GatewayTimeout
        or (HttpStatusCode)529;

    /// <summary>Text appended to the system prompt when the provider cannot enforce the schema itself.</summary>
    protected static string SchemaInstruction(object responseSchema) =>
        "Respond with only one JSON object, with no markdown or commentary, that matches this JSON schema: "
        + JsonSchemaConverter.ToStrict(responseSchema).ToJsonString();

    protected static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private string ExtractJson(string body)
    {
        string? text;
        try
        {
            using var document = JsonDocument.Parse(body);
            text = ExtractText(document.RootElement);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            throw new InvalidOperationException(InvalidResponseText(ProviderName));
        }

        text = StripCodeFence(text);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException(InvalidResponseText(ProviderName));
        return text;
    }

    /// <summary>Removes a surrounding ```json fence that some models add despite instructions.</summary>
    internal static string? StripCodeFence(string? text)
    {
        var trimmed = text?.Trim();
        if (trimmed is null || !trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstNewLine = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewLine < 0 || lastFence <= firstNewLine)
            return trimmed;
        return trimmed[(firstNewLine + 1)..lastFence].Trim();
    }

    private async Task<(HttpStatusCode Status, string? Body)> SendOnceAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return (response.StatusCode, null);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var body = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (body.Length + count > MaxResponseBytes)
                throw new InvalidOperationException($"{ProviderName} returned an oversized response.");
            body.Write(buffer, 0, count);
        }

        return (response.StatusCode, Encoding.UTF8.GetString(body.ToArray()));
    }

    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
        GC.SuppressFinalize(this);
    }
}
