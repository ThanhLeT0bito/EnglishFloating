namespace PteFloatingSentence.Windows.Infrastructure;

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
}
