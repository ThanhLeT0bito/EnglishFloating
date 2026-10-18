namespace PteFloatingSentence.Core;

public static class SettingsUpdateMerger
{
    public static AppSettings MergeEditableFields(AppSettings latest, AppSettings submitted) => VocabularyCheckRules.Normalize(WritingTopicRules.CopyPreferences(latest with
    {
        Sentence = submitted.Sentence,
        ActiveListId = submitted.ActiveListId,
        StudyLists = submitted.StudyLists,
        FontSize = submitted.FontSize,
        TextColor = submitted.TextColor,
        BackgroundOpacity = submitted.BackgroundOpacity,
        GeminiApiKeyConfigured = submitted.GeminiApiKeyConfigured,
        GroqApiKeyConfigured = submitted.GroqApiKeyConfigured,
        ClaudeApiKeyConfigured = submitted.ClaudeApiKeyConfigured,
        AiProvider = AiProviderRules.Normalize(submitted.AiProvider),
        ShowSentenceOverlay = submitted.ShowSentenceOverlay,
        ShowVocabularyCards = submitted.ShowVocabularyCards,
        ShowFloatingFlashcard = submitted.ShowFloatingFlashcard,
        ActiveFlashcardDeckKey = submitted.ActiveFlashcardDeckKey,
        LaunchAtWindowsSignIn = submitted.LaunchAtWindowsSignIn,
        CustomFlashcardDecks = submitted.CustomFlashcardDecks,
        FlashcardProgress = FlashcardRules.NormalizeProgress(latest.FlashcardProgress.Concat(submitted.FlashcardProgress)),
        TtsVoice = submitted.TtsVoice,
        TtsSpeed = submitted.TtsSpeed,
        PracticeMode = submitted.PracticeMode,
        PracticeAudioDelaySeconds = PracticeAudioDelay.Normalize(submitted.PracticeAudioDelaySeconds),
        PenAction = Enum.IsDefined(submitted.PenAction) ? submitted.PenAction : FloatingPenAction.Writing,
        FlashcardMeaningLanguage = submitted.FlashcardMeaningLanguage,
        VocabularyCheckDirection = submitted.VocabularyCheckDirection,
        // Both sides only add translations; keep all of them.
        MeaningTranslations = [.. latest.MeaningTranslations ?? [], .. submitted.MeaningTranslations ?? []]
    }, submitted));
}
