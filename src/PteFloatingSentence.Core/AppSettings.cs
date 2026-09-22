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
    public bool ShowSentenceOverlay { get; init; } = true;
    public bool ShowVocabularyCards { get; init; } = true;
    public IReadOnlyList<CustomFlashcardDeck> CustomFlashcardDecks { get; init; } = [];
    public IReadOnlyList<FlashcardProgress> FlashcardProgress { get; init; } = [];
    public bool ShowFloatingFlashcard { get; init; } = false;
    public string? ActiveFlashcardDeckKey { get; init; } = null;
    public bool LaunchAtWindowsSignIn { get; init; } = true;
    public static AppSettings Default => StudyListRules.CreateDefault("Right-click this sentence to open Settings.");
}
