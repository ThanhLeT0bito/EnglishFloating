using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PteFloatingSentence.Windows.Infrastructure;

/// <summary>
/// Sends one bounded Gemini generateContent request that must return JSON text.
/// Tries each candidate model in turn on 404/429/500/503, enforces a timeout and a response size cap,
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

    private readonly Func<string?> _keyProvider;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly IReadOnlyList<string> _models;
    private readonly TimeSpan _timeout;

    public GeminiJsonClient(
        Func<string?> keyProvider,
        HttpClient? client = null,
        IReadOnlyList<string>? models = null,
        TimeSpan? timeout = null)
    {
        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        _ownsClient = client is null;
        _client = client ?? new HttpClient();
        _models = models is { Count: > 0 } ? models : DefaultModels;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
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

        var payload = JsonSerializer.Serialize(new
        {
            systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = userText } } } },
            generationConfig = new Dictionary<string, object?>
            {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = responseSchema,
                ["temperature"] = temperature
            }.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value)
        });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        HttpStatusCode? lastTransientStatus = null;
        try
        {
            foreach (var model in _models)
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("x-goog-api-key", key);

                using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.StatusCode is HttpStatusCode.NotFound
                    or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.TooManyRequests
                    or HttpStatusCode.InternalServerError)
                {
                    lastTransientStatus = response.StatusCode;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Gemini request failed (HTTP {(int)response.StatusCode}). Try again later.");

                var body = await ReadBoundedAsync(response, timeout.Token);
                return ExtractText(body);
            }

            if (lastTransientStatus is HttpStatusCode.TooManyRequests)
                throw new InvalidOperationException("Gemini is busy or your quota is used up (HTTP 429). Try again in a moment.");
            if (lastTransientStatus is not null)
                throw new InvalidOperationException($"Gemini is unavailable right now (HTTP {(int)lastTransientStatus.Value}). Try again later.");
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
