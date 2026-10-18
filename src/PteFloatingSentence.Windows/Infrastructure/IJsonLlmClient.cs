using System.Diagnostics;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows.Infrastructure;

/// <summary>The model's JSON text plus how the request was answered.</summary>
public sealed record JsonLlmResult(string Text, AiCallInfo Info);

/// <summary>An AI chat model that answers one request with JSON text matching a schema.</summary>
public interface IJsonLlmClient
{
    /// <summary>Display name used in user-facing messages, e.g. "Gemini".</summary>
    string ProviderName { get; }

    bool HasKey { get; }

    /// <param name="responseSchema">Schema in Gemini style (types such as "OBJECT", "STRING"); converted per provider.</param>
    /// <returns>The model's JSON text.</returns>
    Task<string> GenerateJsonAsync(
        string systemInstruction,
        string userText,
        object responseSchema,
        double? temperature = null,
        CancellationToken cancellationToken = default);

    /// <summary>Like <see cref="GenerateJsonAsync"/>, also reporting the model that answered and the time taken.</summary>
    async Task<JsonLlmResult> GenerateJsonWithInfoAsync(
        string systemInstruction,
        string userText,
        object responseSchema,
        double? temperature = null,
        CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        var text = await GenerateJsonAsync(systemInstruction, userText, responseSchema, temperature, cancellationToken);
        return new JsonLlmResult(text, new AiCallInfo(ProviderName, string.Empty, watch.Elapsed, 1));
    }
}
