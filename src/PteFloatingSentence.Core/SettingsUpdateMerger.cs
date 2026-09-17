namespace PteFloatingSentence.Core;

public static class SettingsUpdateMerger
{
    public static AppSettings MergeEditableFields(AppSettings latest, AppSettings submitted) => latest with
    {
        Sentence = submitted.Sentence,
        FontSize = submitted.FontSize,
        TextColor = submitted.TextColor,
        BackgroundOpacity = submitted.BackgroundOpacity,
        ShowSentenceOverlay = submitted.ShowSentenceOverlay,
        ShowVocabularyCards = submitted.ShowVocabularyCards
    };
}
