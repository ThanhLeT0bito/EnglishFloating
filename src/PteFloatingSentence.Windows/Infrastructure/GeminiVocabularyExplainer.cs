using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PteFloatingSentence.Windows.Infrastructure;

public sealed class GeminiVocabularyExplainer : IVocabularyExplainer, IDisposable
{
    private static readonly string[] DefaultCandidateModels = ["gemini-3.5-flash", "gemini-3.6-flash", "gemini-3.5-flash-lite"];
    private const int MaxResponseBytes = 64 * 1024; // 64 KB
    private readonly Func<string?> _apiKeyProvider;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly IReadOnlyList<string> _candidateModels;

    public GeminiVocabularyExplainer(
        Func<string?> apiKeyProvider,
        HttpClient? httpClient = null,
        IReadOnlyList<string>? candidateModels = null)
    {
        _apiKeyProvider = apiKeyProvider ?? throw new ArgumentNullException(nameof(apiKeyProvider));
        _candidateModels = candidateModels is { Count: > 0 } ? candidateModels : DefaultCandidateModels;
        if (httpClient is null)
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    public async Task<VocabularyExplanation> ExplainAsync(string phrase, string sourceSentence, CancellationToken cancellationToken = default)
    {
        var apiKey = _apiKeyProvider()?.Trim();
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("Gemini API key is not configured.");

        var requestPayload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new
                        {
                            text = $"Explain the vocabulary phrase \"{phrase}\" from the following sentence: \"{sourceSentence}\". Provide a short meaning in simple English, a new example sentence, and the IPA pronunciation."
                        }
                    }
                }
            },
            generationConfig = new
            {
                response_mime_type = "application/json",
                response_schema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        meaning = new { type = "STRING", description = "One short sentence in simple English explaining the word or phrase in context." },
                        example = new { type = "STRING", description = "One new English sentence showing natural usage." },
                        pronunciationIpa = new { type = "STRING", description = "IPA pronunciation text, e.g. /əˈplaɪ/." }
                    },
                    required = new[] { "meaning", "example", "pronunciationIpa" }
                }
            }
        };

        var jsonPayload = JsonSerializer.Serialize(requestPayload);
        HttpRequestException? lastTransientException = null;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            foreach (var model in _candidateModels)
            {
                var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("x-goog-api-key", apiKey);

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                }
                catch (HttpRequestException ex)
                {
                    lastTransientException = new HttpRequestException(SanitizeMessage(ex.Message, apiKey), ex.InnerException, ex.StatusCode);
                    continue;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new InvalidOperationException(SanitizeMessage(ex.Message, apiKey));
                }

                using (response)
                {
                    if (response.IsSuccessStatusCode)
                    {
                        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                        using var limitedStream = new MemoryStream();
                        var buffer = new byte[4096];
                        int bytesRead;
                        int totalBytes = 0;

                        while ((bytesRead = await responseStream.ReadAsync(buffer, cancellationToken)) > 0)
                        {
                            totalBytes += bytesRead;
                            if (totalBytes > MaxResponseBytes)
                                throw new InvalidOperationException("Gemini response exceeded maximum allowable size.");

                            limitedStream.Write(buffer, 0, bytesRead);
                        }

                        var responseJson = Encoding.UTF8.GetString(limitedStream.ToArray());
                        return ParseGeminiResponse(responseJson);
                    }

                    var statusCode = response.StatusCode;
                    string? errorDetail = null;
                    try
                    {
                        var errorJson = await response.Content.ReadAsStringAsync(cancellationToken);
                        using var errDoc = JsonDocument.Parse(errorJson);
                        if (errDoc.RootElement.TryGetProperty("error", out var errObj) &&
                            errObj.TryGetProperty("message", out var msgProp))
                        {
                            errorDetail = msgProp.GetString();
                        }
                    }
                    catch
                    {
                        // Fall back to generic status code if error payload cannot be parsed
                    }

                    var message = string.IsNullOrWhiteSpace(errorDetail)
                        ? $"Gemini API returned status code {(int)statusCode} ({statusCode})."
                        : $"Gemini API error ({(int)statusCode} {statusCode}): {SanitizeMessage(errorDetail, apiKey)}";

                    var httpEx = new HttpRequestException(message, null, statusCode);

                    if (IsTransientOrModelError(statusCode))
                    {
                        lastTransientException = httpEx;
                        continue;
                    }

                    // Client errors (400, 401, 403, etc.) are non-transient and fail immediately
                    throw httpEx;
                }
            }

            if (attempt == 0 && lastTransientException is not null)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }

        if (lastTransientException is not null)
            throw lastTransientException;

        throw new InvalidOperationException("No Gemini model candidates were available.");
    }

    private static bool IsTransientOrModelError(System.Net.HttpStatusCode? statusCode) =>
        statusCode is System.Net.HttpStatusCode.ServiceUnavailable
                   or System.Net.HttpStatusCode.TooManyRequests
                   or System.Net.HttpStatusCode.NotFound
                   or System.Net.HttpStatusCode.InternalServerError
                   or System.Net.HttpStatusCode.BadGateway
                   or System.Net.HttpStatusCode.GatewayTimeout;

    private static VocabularyExplanation ParseGeminiResponse(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                throw new InvalidOperationException("No candidates returned from Gemini API.");

            var firstCandidate = candidates[0];
            var parts = firstCandidate.GetProperty("content").GetProperty("parts");
            if (parts.GetArrayLength() == 0)
                throw new InvalidOperationException("Empty content parts in Gemini response.");

            var rawText = parts[0].GetProperty("text").GetString();
            if (string.IsNullOrWhiteSpace(rawText))
                throw new InvalidOperationException("Empty text content in Gemini response.");

            using var innerDoc = JsonDocument.Parse(rawText);
            var root = innerDoc.RootElement;

            var meaning = root.GetProperty("meaning").GetString()?.Trim();
            var example = root.GetProperty("example").GetString()?.Trim();
            var ipa = root.GetProperty("pronunciationIpa").GetString()?.Trim();

            if (string.IsNullOrEmpty(meaning) || string.IsNullOrEmpty(example) || string.IsNullOrEmpty(ipa))
                throw new InvalidOperationException("Incomplete explanation fields in Gemini response.");

            return new VocabularyExplanation(meaning, example, ipa);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Malformed explanation response received from Gemini.", ex);
        }
        catch (KeyNotFoundException ex)
        {
            throw new InvalidOperationException("Missing required explanation fields in Gemini response.", ex);
        }
    }

    private static string SanitizeMessage(string message, string secret)
    {
        if (string.IsNullOrEmpty(secret))
            return message;

        return message.Replace(secret, "[REDACTED]");
    }
}
