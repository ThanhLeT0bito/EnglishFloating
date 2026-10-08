using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows.Infrastructure;

/// <summary>Sends each request to the client of the provider selected right now in settings.</summary>
public sealed class SwitchingJsonClient : IJsonLlmClient
{
    private readonly Func<AiProvider> _currentProvider;
    private readonly IReadOnlyDictionary<AiProvider, IJsonLlmClient> _clients;

    public SwitchingJsonClient(Func<AiProvider> currentProvider, IReadOnlyDictionary<AiProvider, IJsonLlmClient> clients)
    {
        _currentProvider = currentProvider ?? throw new ArgumentNullException(nameof(currentProvider));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        if (!_clients.ContainsKey(AiProvider.Gemini))
            throw new ArgumentException("A Gemini client is required as the default provider.", nameof(clients));
    }

    public IJsonLlmClient Current =>
        _clients.TryGetValue(AiProviderRules.Normalize(_currentProvider()), out var client) ? client : _clients[AiProvider.Gemini];

    public string ProviderName => Current.ProviderName;

    public bool HasKey => Current.HasKey;

    public Task<string> GenerateJsonAsync(
        string systemInstruction,
        string userText,
        object responseSchema,
        double? temperature = null,
        CancellationToken cancellationToken = default) =>
        Current.GenerateJsonAsync(systemInstruction, userText, responseSchema, temperature, cancellationToken);
}
