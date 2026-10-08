using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PteFloatingSentence.Windows.Infrastructure;

/// <summary>
/// Sends one bounded Gemini generateContent request that must return JSON text.
/// Asks for low thinking to keep latency down, gives each candidate model its own time slot and moves on
/// when a model is slow or returns 404/429/500/503, enforces a total timeout and a response size cap,
/// and turns failures into <see cref="InvalidOperationException"/> with a user-facing message.
/// Caller cancellation is reported as <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class GeminiJsonClient : IDisposable
{
    public static readonly IReadOnlyList<string> DefaultModels =
        ["gemini-3.8-flash", "gemini-3.6-flash", "gemini-3.5-flash", "gemini-3.5-flash-lite"];

    public const int MaxResponseBytes = 64 * 1024;
    public const string MissingKeyMessage = "Configure your Gemini API key in Settings first.";
    public const string TimeoutMessage = "Gemini took too long to respond. Please try again.";
    public const string ConnectionMessage = "Could not connect to Gemini. Check your connection and try again.";
    public const string InvalidResponseMessage = "Gemini returned an invalid response. Please try again.";

    /// <summary>Lowest thinking level accepted by every default model (3.8/3.7 Flash reject "minimal").</summary>
    public const string ThinkingLevel = "low";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan DefaultAttemptTimeout = TimeSpan.FromSeconds(20);

    private readonly Func<string?> _keyProvider;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly IReadOnlyList<string> _models;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _attemptTimeout;

    /// <param name="timeout">Total time for the whole request, across all models.</param>
    /// <param name="attemptTimeout">Time one model may take before the next model is tried.</param>
    public GeminiJsonClient(
        Func<string?> keyProvider,
        HttpClient? client = null,
        IReadOnlyList<string>? models = null,
        TimeSpan? timeout = null,
        TimeSpan? attemptTimeout = null)
    {
        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        _ownsClient = client is null;
        _client = client ?? new HttpClient();
        _models = models is { Count: > 0 } ? models : DefaultModels;
        _timeout = timeout ?? DefaultTimeout;
        _attemptTimeout = attemptTimeout ?? DefaultAttemptTimeout;
    }

    public bool HasKey => !string.IsNullOrWhiteSpace(_keyProvider());

    /// <summary>Returns the model's JSON text (non-thought parts concatenated).</summary>
    public async Task<string> GenerateJsonAsync(
        string systemInstruction,
        string userText,
        object responseSchema,
        double? temperature = null,
        CancellationToken cancellationToken = default)
    {
        var key = _keyProvider()?.Trim();
        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException(MissingKeyMessage);

        var payloadWithThinking = BuildPayload(systemInstruction, userText, responseSchema, temperature, includeThinking: true);
        string? payloadWithoutThinking = null;

        using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(_timeout);
        HttpStatusCode? lastTransientStatus = null;
        var modelTimedOut = false;
        try
        {
            foreach (var model in _models)
            {
                var payload = payloadWithThinking;
                for (var pass = 0; pass < 2; pass++)
                {
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
                    attempt.CancelAfter(_attemptTimeout);

                    HttpStatusCode status;
                    string? body;
                    try
                    {
                        (status, body) = await SendOnceAsync(model, key, payload, attempt.Token);
                    }
                    catch (OperationCanceledException) when (attempt.IsCancellationRequested && !total.IsCancellationRequested)
                    {
                        // This model is too slow right now; give the next one a chance.
                        modelTimedOut = true;
                        break;
                    }

                    if (status == HttpStatusCode.BadRequest && ReferenceEquals(payload, payloadWithThinking))
                    {
                        // A model that does not accept the thinking level gets one retry without it.
                        payload = payloadWithoutThinking ??= BuildPayload(systemInstruction, userText, responseSchema, temperature, includeThinking: false);
                        continue;
                    }

                    if (status is HttpStatusCode.NotFound
                        or HttpStatusCode.ServiceUnavailable
                        or HttpStatusCode.TooManyRequests
                        or HttpStatusCode.InternalServerError)
                    {
                        lastTransientStatus = status;
                        break;
                    }

                    if (body is null)
                        throw new InvalidOperationException($"Gemini request failed (HTTP {(int)status}). Try again later.");

                    return ExtractText(body);
                }
            }

            if (lastTransientStatus is HttpStatusCode.TooManyRequests)
                throw new InvalidOperationException("Gemini is busy or your quota is used up (HTTP 429). Try again in a moment.");
            if (lastTransientStatus is not null)
                throw new InvalidOperationException($"Gemini is unavailable right now (HTTP {(int)lastTransientStatus.Value}). Try again later.");
            if (modelTimedOut)
                throw new InvalidOperationException(TimeoutMessage);
            throw new InvalidOperationException("No supported Gemini model is available. Try again later.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(TimeoutMessage);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException(ConnectionMessage);
        }
    }

    private static string BuildPayload(string systemInstruction, string userText, object responseSchema, double? temperature, bool includeThinking)
    {
        var generationConfig = new Dictionary<string, object>
        {
            ["responseMimeType"] = "application/json",
            ["responseSchema"] = responseSchema
        };
        if (temperature is not null)
            generationConfig["temperature"] = temperature.Value;
        if (includeThinking)
            generationConfig["thinkingConfig"] = new { thinkingLevel = ThinkingLevel };

        return JsonSerializer.Serialize(new
        {
            systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = userText } } } },
            generationConfig
        });
    }

    /// <summary>Sends to one model; returns the status and, on success, the bounded body.</summary>
    private async Task<(HttpStatusCode Status, string? Body)> SendOnceAsync(string model, string key, string payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-goog-api-key", key);

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return (response.StatusCode, null);

        return (response.StatusCode, await ReadBoundedAsync(response, cancellationToken));
    }

    private static async Task<string> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var body = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (body.Length + count > MaxResponseBytes)
                throw new InvalidOperationException("Gemini returned an oversized response.");
            body.Write(buffer, 0, count);
        }

        return Encoding.UTF8.GetString(body.ToArray());
    }

    private static string ExtractText(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var parts = document.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts");
            var text = string.Concat(parts.EnumerateArray()
                .Where(part => !part.TryGetProperty("thought", out var thought) || thought.ValueKind != JsonValueKind.True)
                .Where(part => part.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String)
                .Select(part => part.GetProperty("text").GetString()));
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException(InvalidResponseMessage);
            return text;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            throw new InvalidOperationException(InvalidResponseMessage);
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }
}
