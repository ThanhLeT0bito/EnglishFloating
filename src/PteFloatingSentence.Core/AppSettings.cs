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

    /// <summary>The language flashcard meanings are shown and checked in (a <see cref="MeaningLanguages"/> code).</summary>
    public string FlashcardMeaningLanguage { get; init; } = MeaningLanguages.English;

    /// <summary>What a vocabulary check asks: the word from its meaning, the meaning from the word, or both.</summary>
    public VocabularyCheckDirection VocabularyCheckDirection { get; init; } = VocabularyCheckDirection.MeaningToWord;

    /// <summary>Meanings translated by the AI, kept so each word is translated once per language.</summary>
    public IReadOnlyList<MeaningTranslation> MeaningTranslations { get; init; } = [];
    public bool LaunchAtWindowsSignIn { get; init; } = true;
    public string TtsVoice { get; init; } = "en-US-JennyNeural";
    public double TtsSpeed { get; init; } = 1.0;
    public PracticeMode PracticeMode { get; init; } = PracticeMode.TextHints;
    public int PracticeAudioDelaySeconds { get; init; } = PracticeAudioDelay.DefaultSeconds;

    /// <summary>What the pen button on the floating sentence opens.</summary>
    public FloatingPenAction PenAction { get; init; } = FloatingPenAction.Writing;
    public IReadOnlyList<WritingTopic> CustomWritingTopics { get; init; } = [];
    public IReadOnlyList<string> SelectedWritingTopicKeys { get; init; } = [];
    public WritingLevel WritingLevel { get; init; } = WritingLevel.Medium;
    public WritingExplanationLanguage WritingExplanationLanguage { get; init; } = WritingExplanationLanguage.English;
    public WritingPracticeMode WritingPracticeMode { get; init; } = WritingPracticeMode.Translate;
    public bool WritingRetypeEnabled { get; init; } = true;
    public bool WritingAutoPlayCorrected { get; init; } = false;
    public bool WritingFocusWeakPoints { get; init; } = false;
    public static AppSettings Default => StudyListRules.CreateDefault("Right-click this sentence to open Settings.");
}

/// <summary>What the pen button on the floating sentence opens.</summary>
public enum FloatingPenAction
{
    /// <summary>Writing practice: the AI makes new sentences to translate.</summary>
    Writing = 0,

    /// <summary>Practice the active study list: hear each sentence and type it.</summary>
    ListPractice = 1
}
