namespace PteFloatingSentence.Core;

/// <summary>Which AI service powers vocabulary explanations, sentence phrasing and writing practice.</summary>
public enum AiProvider
{
    Gemini = 0,
    Groq = 1,
    Claude = 2
}

/// <summary>How one AI request was answered: which provider and model, how long it took, how many HTTP attempts.</summary>
public sealed record AiCallInfo(string Provider, string Model, TimeSpan Elapsed, int Attempts);

public static class AiProviderRules
{
    public static AiProvider Normalize(AiProvider provider) =>
        Enum.IsDefined(provider) ? provider : AiProvider.Gemini;

    public static bool IsKeyConfigured(AppSettings settings, AiProvider provider) => Normalize(provider) switch
    {
        AiProvider.Groq => settings.GroqApiKeyConfigured,
        AiProvider.Claude => settings.ClaudeApiKeyConfigured,
        _ => settings.GeminiApiKeyConfigured
    };

    public static AppSettings WithKeyConfigured(AppSettings settings, AiProvider provider, bool configured) => Normalize(provider) switch
    {
        AiProvider.Groq => settings with { GroqApiKeyConfigured = configured },
        AiProvider.Claude => settings with { ClaudeApiKeyConfigured = configured },
        _ => settings with { GeminiApiKeyConfigured = configured }
    };
}
