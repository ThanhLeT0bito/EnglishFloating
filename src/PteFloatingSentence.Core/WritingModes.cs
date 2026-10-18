namespace PteFloatingSentence.Core;

/// <summary>What each practice mode shows, expects, plays and records. No UI or AI involved.</summary>
public static class WritingModes
{
    public static IReadOnlyList<WritingPracticeMode> All { get; } =
    [
        WritingPracticeMode.Translate,
        WritingPracticeMode.Dictation,
        WritingPracticeMode.Paraphrase,
        WritingPracticeMode.Combine,
        WritingPracticeMode.Reverse
    ];

    /// <summary>The kind of prompt a mode needs from the AI. Dictation reads out a translate prompt's English.</summary>
    public static WritingPracticeMode GenerationMode(WritingPracticeMode mode) =>
        mode == WritingPracticeMode.Dictation ? WritingPracticeMode.Translate : mode;

    /// <summary>True when a prompt generated for <paramref name="promptMode"/> can be practiced in <paramref name="mode"/>.</summary>
    public static bool Fits(WritingPracticeMode promptMode, WritingPracticeMode mode) =>
        GenerationMode(promptMode) == GenerationMode(mode);

    /// <summary>The mode a prompt is practiced in: the chosen mode when it fits, otherwise the prompt's own mode.</summary>
    public static WritingPracticeMode Effective(WritingPrompt prompt, WritingPracticeMode chosen) =>
        Fits(prompt.Mode, chosen) ? chosen : prompt.Mode;

    /// <summary>True when the learner answers in Vietnamese.</summary>
    public static bool AnswersInVietnamese(WritingPracticeMode mode) => mode == WritingPracticeMode.Reverse;

    /// <summary>The text shown as the exercise (hidden in dictation until the answer is checked).</summary>
    public static string PromptText(WritingPrompt prompt, WritingPracticeMode mode) => mode switch
    {
        WritingPracticeMode.Paraphrase or WritingPracticeMode.Combine => prompt.SourceEnglish,
        WritingPracticeMode.Reverse => prompt.ReferenceEnglish,
        _ => prompt.Vietnamese
    };

    /// <summary>English the learner can listen to; empty in translate mode, where the English is the answer.</summary>
    public static string ListenText(WritingPrompt prompt, WritingPracticeMode mode) => mode switch
    {
        WritingPracticeMode.Dictation or WritingPracticeMode.Reverse => prompt.ReferenceEnglish,
        WritingPracticeMode.Paraphrase or WritingPracticeMode.Combine => prompt.SourceEnglish.Replace('\n', ' '),
        _ => string.Empty
    };

    /// <summary>The model answer: Vietnamese for reverse translation, English otherwise.</summary>
    public static string ExpectedAnswer(WritingPrompt prompt, WritingPracticeMode mode) =>
        AnswersInVietnamese(mode) ? prompt.Vietnamese : prompt.ReferenceEnglish;

    /// <summary>Vietnamese meaning shown after checking a paraphrase or combine exercise; empty otherwise.</summary>
    public static string MeaningText(WritingPrompt prompt, WritingPracticeMode mode) =>
        mode is WritingPracticeMode.Paraphrase or WritingPracticeMode.Combine ? prompt.Vietnamese : string.Empty;

    /// <summary>The exercise text saved with a mistake in the notebook.</summary>
    public static string HistorySource(WritingPrompt prompt, WritingPracticeMode mode) => mode switch
    {
        WritingPracticeMode.Dictation or WritingPracticeMode.Reverse => prompt.ReferenceEnglish,
        WritingPracticeMode.Paraphrase or WritingPracticeMode.Combine => prompt.SourceEnglish,
        _ => prompt.Vietnamese
    };

    /// <summary>English sentence to play or save after a check: the English source in reverse mode, the correction otherwise.</summary>
    public static string EnglishResult(WritingPrompt prompt, WritingFeedback feedback, WritingPracticeMode mode) =>
        AnswersInVietnamese(mode) ? prompt.ReferenceEnglish : feedback.CorrectedSentence;

    /// <summary>True when a paraphrase answer just repeats the original sentence.</summary>
    public static bool IsCopiedParaphrase(WritingPrompt prompt, string? answer) =>
        prompt.SourceEnglish.Length > 0 && WritingTextMatch.SameWords(prompt.SourceEnglish, answer);
}
