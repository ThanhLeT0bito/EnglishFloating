namespace PteFloatingSentence.Core;

public static class SettingsUpdateMerger
{
    public static AppSettings MergeEditableFields(AppSettings latest, AppSettings submitted) => latest with
    {
        Sentence = submitted.Sentence,
        ActiveListId = submitted.ActiveListId,
        StudyLists = submitted.StudyLists,
        FontSize = submitted.FontSize,
        TextColor = submitted.TextColor,
        BackgroundOpacity = submitted.BackgroundOpacity,
        GeminiApiKeyConfigured = submitted.GeminiApiKeyConfigured,
        ShowSentenceOverlay = submitted.ShowSentenceOverlay,
        ShowVocabularyCards = submitted.ShowVocabularyCards,
        ShowFloatingFlashcard = submitted.ShowFloatingFlashcard,
        ActiveFlashcardDeckKey = submitted.ActiveFlashcardDeckKey,
        LaunchAtWindowsSignIn = submitted.LaunchAtWindowsSignIn,
        CustomFlashcardDecks = submitted.CustomFlashcardDecks,
        FlashcardProgress = FlashcardRules.NormalizeProgress(latest.FlashcardProgress.Concat(submitted.FlashcardProgress))
    };
}
