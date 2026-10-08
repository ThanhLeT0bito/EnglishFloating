namespace PteFloatingSentence.Core;

public sealed record AppSettings
{
    public int Version { get; init; } = 2;
    public string Sentence { get; init; } = "Right-click this sentence to open Settings.";
    public Guid ActiveListId { get; init; }
    public IReadOnlyList<StudyList> StudyLists { get; init; } = [];
    public double FontSize { get; init; } = 30;
    public string TextColor { get; init; } = "#FFFFFFFF";
    public double BackgroundOpacity { get; init; } = 0.35;
    public double Left { get; init; } = 100;
    public double Top { get; init; } = 100;
    public bool GeminiApiKeyConfigured { get; init; } = false;
    public bool GroqApiKeyConfigured { get; init; } = false;
    public bool ClaudeApiKeyConfigured { get; init; } = false;
    public AiProvider AiProvider { get; init; } = AiProvider.Gemini;
    public bool ShowSentenceOverlay { get; init; } = true;
    public bool ShowVocabularyCards { get; init; } = true;
    public IReadOnlyList<CustomFlashcardDeck> CustomFlashcardDecks { get; init; } = [];
    public IReadOnlyList<FlashcardProgress> FlashcardProgress { get; init; } = [];
    public bool ShowFloatingFlashcard { get; init; } = false;
    public string? ActiveFlashcardDeckKey { get; init; } = null;
    public bool LaunchAtWindowsSignIn { get; init; } = true;
    public string TtsVoice { get; init; } = "en-US-JennyNeural";
    public double TtsSpeed { get; init; } = 1.0;
    public PracticeMode PracticeMode { get; init; } = PracticeMode.TextHints;
    public int PracticeAudioDelaySeconds { get; init; } = PracticeAudioDelay.DefaultSeconds;
    public IReadOnlyList<WritingTopic> CustomWritingTopics { get; init; } = [];
    public IReadOnlyList<string> SelectedWritingTopicKeys { get; init; } = [];
    public WritingLevel WritingLevel { get; init; } = WritingLevel.Medium;
    public WritingExplanationLanguage WritingExplanationLanguage { get; init; } = WritingExplanationLanguage.English;
    public static AppSettings Default => StudyListRules.CreateDefault("Right-click this sentence to open Settings.");
}
