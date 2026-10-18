using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

/// <summary>A piece of the learner's answer; <see cref="IssueIndex"/> is set when the piece is highlighted.</summary>
public readonly record struct AnswerSegment(string Text, int? IssueIndex);

/// <summary>WPF-free text and color choices for the Writing page.</summary>
public static class WritingPresentation
{
    public const string NoCoachMessage = "Add an API key on the AI page in Settings first.";

    public static string LevelLabel(WritingLevel level) => level switch
    {
        WritingLevel.Easy => "Easy",
        WritingLevel.Hard => "Hard",
        _ => "Medium"
    };

    public static string IssueLabel(WritingIssueType type) => type switch
    {
        WritingIssueType.Vocabulary => "Vocabulary",
        WritingIssueType.Spelling => "Spelling",
        WritingIssueType.WordOrder => "Word order",
        WritingIssueType.MissingMeaning => "Missing meaning",
        WritingIssueType.ExtraMeaning => "Extra meaning",
        WritingIssueType.Punctuation => "Punctuation",
        _ => "Grammar"
    };

    /// <summary>ARGB hex color for an issue type, used for its badge and highlight.</summary>
    public static string IssueColor(WritingIssueType type) => type switch
    {
        WritingIssueType.Vocabulary => "#FFFBBF24",
        WritingIssueType.Spelling => "#FFF472B6",
        WritingIssueType.WordOrder => "#FFA78BFA",
        WritingIssueType.MissingMeaning => "#FF38BDF8",
        WritingIssueType.ExtraMeaning => "#FFFB923C",
        WritingIssueType.Punctuation => "#FF94A3B8",
        _ => "#FFF87171"
    };

    public static string VerdictLabel(WritingVerdict verdict) => verdict switch
    {
        WritingVerdict.Correct => "Correct",
        WritingVerdict.Almost => "Almost there",
        _ => "Needs work"
    };

    public static string VerdictColor(WritingVerdict verdict) => verdict switch
    {
        WritingVerdict.Correct => "#FF34D399",
        WritingVerdict.Almost => "#FFFBBF24",
        _ => "#FFF87171"
    };

    public static string TipButtonText(int revealed, int available) =>
        available == 0 ? "No tips" : $"Tip {Math.Min(revealed, available)}/{available}";

    public static string TipsUsedText(int count) => count switch
    {
        <= 0 => string.Empty,
        1 => "Used 1 tip",
        _ => $"Used {count} tips"
    };

    public static string? BusyText(WritingSessionState state) => state switch
    {
        WritingSessionState.Generating => "Creating a sentence…",
        WritingSessionState.Checking => "Checking your translation…",
        _ => null
    };

    public const int SummaryTopicCount = 3;

    /// <summary>One-line summary of the setup card, e.g. "Working in IT, Traffic &amp; transport · Medium · Explain in English".</summary>
    public static string SetupSummary(
        IReadOnlyList<string>? topicNames,
        WritingLevel level,
        WritingExplanationLanguage language,
        WritingPracticeMode mode = WritingPracticeMode.Translate,
        bool focusWeakPoints = false)
    {
        var names = (topicNames ?? []).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        var topics = names.Count switch
        {
            0 => "Mixed topics",
            <= SummaryTopicCount => string.Join(", ", names),
            _ => string.Join(", ", names.Take(SummaryTopicCount)) + $" +{names.Count - SummaryTopicCount} more"
        };
        var explain = language == WritingExplanationLanguage.Vietnamese ? "Tiếng Việt" : "English";
        var prefix = mode == WritingPracticeMode.Translate ? string.Empty : $"{ModeLabel(mode)} · ";
        var focus = focusWeakPoints ? " · Weak points" : string.Empty;
        return $"{prefix}{topics} · {LevelLabel(level)} · Explain in {explain}{focus}";
    }

    /// <summary>Timing line such as "Groq · openai/gpt-oss-120b · 1.3 s", or "ready instantly" for a prepared sentence.</summary>
    public static string CallText(AiCallInfo? call, bool prefetched = false)
    {
        if (prefetched)
            return call is null ? "Prepared in advance · ready instantly" : $"{call.Provider} · prepared in advance · ready instantly";
        if (call is null)
            return string.Empty;

        var model = string.IsNullOrWhiteSpace(call.Model) ? call.Provider : $"{call.Provider} · {call.Model}";
        var seconds = call.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        var retries = call.Attempts > 1 ? $" · {call.Attempts} attempts" : string.Empty;
        return $"{model} · {seconds} s{retries}";
    }

    public static string PromptMeta(WritingPrompt prompt) =>
        prompt.FocusTags.Count == 0
            ? $"{prompt.Topic} · {LevelLabel(prompt.Level)}"
            : $"{prompt.Topic} · {LevelLabel(prompt.Level)} · {FocusText(prompt.FocusTags)}";

    public static string FocusText(IReadOnlyList<WritingSkillTag> tags) =>
        "Focus: " + string.Join(", ", tags.Select(WritingSkillTags.Label));

    public static string ModeLabel(WritingPracticeMode mode) => mode switch
    {
        WritingPracticeMode.Dictation => "Dictation",
        WritingPracticeMode.Paraphrase => "Paraphrase",
        WritingPracticeMode.Combine => "Combine",
        WritingPracticeMode.Reverse => "EN → VI",
        _ => "Translate"
    };

    /// <summary>Heading of the exercise card.</summary>
    public static string PromptHeader(WritingPracticeMode mode) => mode switch
    {
        WritingPracticeMode.Dictation => "Write what you hear",
        WritingPracticeMode.Paraphrase => "Rewrite in your own words",
        WritingPracticeMode.Combine => "Combine into one sentence",
        WritingPracticeMode.Reverse => "Translate into Vietnamese",
        _ => "Translate into English"
    };

    /// <summary>Line under the exercise, e.g. the connector to use when combining.</summary>
    public static string InstructionText(WritingPrompt prompt, WritingPracticeMode mode) =>
        mode == WritingPracticeMode.Combine && prompt.Instruction.Length > 0 ? $"Use: {prompt.Instruction}" : string.Empty;

    /// <summary>Vietnamese meaning shown after a paraphrase or combine exercise is checked.</summary>
    public static string MeaningLine(WritingPrompt prompt, WritingPracticeMode mode)
    {
        var meaning = WritingModes.MeaningText(prompt, mode);
        return meaning.Length == 0 ? string.Empty : $"Nghĩa: {meaning}";
    }

    /// <summary>Label above the answer box.</summary>
    public static string AnswerLabel(WritingPracticeMode mode, bool isRetyping) =>
        isRetyping ? "Type the corrected sentence"
        : mode switch
        {
            WritingPracticeMode.Dictation => "What you hear",
            WritingPracticeMode.Paraphrase => "Your paraphrase",
            WritingPracticeMode.Combine => "Your combined sentence",
            WritingPracticeMode.Reverse => "Your Vietnamese translation",
            _ => "Your English translation"
        };

    /// <summary>Keyboard hint under the answer box on the Writing page.</summary>
    public static string AnswerHint(WritingPracticeMode mode, bool isRetyping) =>
        isRetyping ? "Enter to submit · Typing it once helps you remember it"
        : mode switch
        {
            WritingPracticeMode.Dictation => "Enter to check · Shift+Enter for a new line · Replay with Ctrl+Space (slow: Ctrl+Shift+Space)",
            WritingPracticeMode.Translate => "Enter to check · Shift+Enter for a new line · Stuck? Press Tip (Ctrl+H)",
            _ => "Enter to check · Shift+Enter for a new line · Tip: Ctrl+H · Listen: Ctrl+Space"
        };

    /// <summary>True when the listen buttons are shown: every mode except translate, where the English is the answer.</summary>
    public static bool ShowsListenBar(WritingPracticeMode mode) => mode != WritingPracticeMode.Translate;

    /// <summary>Vocabulary tip line; in reverse translation the English word comes first.</summary>
    public static string VocabularyLine(WritingVocabularyHint hint, WritingPracticeMode mode) =>
        mode != WritingPracticeMode.Reverse
            ? VocabularyLine(hint)
            : string.IsNullOrWhiteSpace(hint.PartOfSpeech)
                ? $"{hint.English} → {hint.Vietnamese}"
                : $"{hint.English} → {hint.Vietnamese} ({hint.PartOfSpeech})";

    public const string DictationPromptText = "Listen and type the English sentence you hear.";

    public const string RetypeOptionHelp =
        "When a checked answer has mistakes, type the corrected sentence once before moving on. " +
        "Copying the right form by hand helps you remember it. You can always press Skip.";

    public const string AutoPlayOptionHelp =
        "Read the corrected sentence aloud right after each check, with your TTS voice and speed. " +
        "With retype on, it plays after you retype the sentence correctly instead.";

    public const string WeakPointsOptionHelp =
        "New sentences are written to make you use the grammar you get wrong most (up to 3 points, listed here). " +
        "It unlocks after 5 mistakes in the last 30 days. A weak-point sentence done without that mistake counts as practiced (✓ in My mistakes).";

    /// <summary>Header text of the tips box, e.g. "Key words · Grammar hint".</summary>
    public static string TipsSummary(IReadOnlyList<WritingTipTier> revealed) =>
        string.Join(" · ", revealed.Select(tier => tier switch
        {
            WritingTipTier.Vocabulary => "Key words",
            WritingTipTier.Grammar => "Grammar hint",
            _ => "Start with"
        }));

    /// <summary>Placeholder of the answer box.</summary>
    public static string AnswerPlaceholder(WritingPracticeMode mode, bool isRetyping) =>
        isRetyping ? "Type the corrected sentence once to remember it… (Enter)"
        : mode switch
        {
            WritingPracticeMode.Dictation => "Type what you hear… (Enter to check)",
            WritingPracticeMode.Paraphrase => "Rewrite the sentence in your own words… (Enter to check)",
            WritingPracticeMode.Combine => "Write one sentence that joins both… (Enter to check)",
            WritingPracticeMode.Reverse => "Type your Vietnamese translation… (Enter to check)",
            _ => "Type your English translation… (Enter to check)"
        };

    /// <summary>Label of the main button: check, check again or submit a retype.</summary>
    public static string CheckButtonText(WritingSessionState state, bool isRetyping) =>
        isRetyping ? "Submit" : state == WritingSessionState.Reviewed ? "Check again" : "Check";

    /// <summary>Explains the first difference of a failed retype, e.g. word 3 should be "has".</summary>
    public static string RetypeMessage(RetypeResult? result, bool skipped = false)
    {
        if (skipped)
            return string.Empty;
        if (result is null)
            return "Now type the corrected sentence once.";
        if (result.IsMatch)
            return "Well done! Press Enter for the next sentence.";

        var position = result.WordIndex + 1;
        if (result.Expected is null)
            return $"Word {position}: \"{result.Typed}\" is extra — the sentence ends before it.";
        if (result.Typed is null)
            return $"Word {position}: \"{result.Expected}\" is missing.";
        return $"Word {position}: expected \"{result.Expected}\", you typed \"{result.Typed}\".";
    }

    /// <summary>Slower playback for dictation: 80% of the learner's speed, never below 0.5.</summary>
    public static double SlowSpeed(double normalSpeed)
    {
        var speed = normalSpeed is >= 0.5 and <= 2.0 ? normalSpeed : 1.0;
        return Math.Max(0.5, Math.Round(speed * 0.8, 2));
    }

    /// <summary>Normal playback speed from settings.</summary>
    public static double NormalSpeed(double settingsSpeed) => settingsSpeed is >= 0.8 and <= 1.2 ? settingsSpeed : 1.0;

    /// <summary>Text of the weak-point switch: the tags it will target, or how many mistakes are still needed.</summary>
    public static string WeakPointsText(bool available, IReadOnlyList<WritingSkillTag> weakTags, int recentMistakes)
    {
        if (available)
            return "Practice my weak points: " + string.Join(", ", weakTags.Select(WritingSkillTags.Label));
        if (recentMistakes < WritingHistoryRules.MinMistakesForWeakPoints)
            return $"Practice my weak points (after {WritingHistoryRules.MinMistakesForWeakPoints} mistakes · {recentMistakes} so far)";
        return "Practice my weak points (no grammar weak point yet)";
    }

    /// <summary>"today", "yesterday" or "N days ago".</summary>
    public static string RelativeDay(DateTimeOffset at, DateTimeOffset now)
    {
        var days = (now.Date - at.ToOffset(now.Offset).Date).Days;
        return days switch
        {
            <= 0 => "today",
            1 => "yesterday",
            _ => $"{days} days ago"
        };
    }

    /// <summary>One statistics chip, e.g. "Tense · 6" or "Articles · 4 · ✓2".</summary>
    public static string TagStatText(WritingTagStat stat) =>
        stat.Passed > 0
            ? $"{WritingSkillTags.Label(stat.Tag)} · {stat.Mistakes} · ✓{stat.Passed}"
            : $"{WritingSkillTags.Label(stat.Tag)} · {stat.Mistakes}";

    /// <summary>Line for a saved mistake, e.g. "\"go\" → \"went\"".</summary>
    public static string MistakeText(WritingMistake mistake) =>
        IssueText(new WritingIssue(mistake.Type, mistake.Original, mistake.Suggestion, mistake.Explanation));

    /// <summary>Line for a group of the same mistake, e.g. "\"go\" → \"went\" ×3".</summary>
    public static string MistakeGroupText(WritingMistakeGroup group) =>
        group.Count > 1 ? $"{MistakeText(group.Latest)} ×{group.Count}" : MistakeText(group.Latest);

    public static string IssueText(WritingIssue issue)
    {
        if (issue.Original.Length == 0)
            return issue.Suggestion.Length == 0 ? "(missing)" : $"(missing) → \"{issue.Suggestion}\"";
        return issue.Suggestion.Length == 0 ? $"\"{issue.Original}\"" : $"\"{issue.Original}\" → \"{issue.Suggestion}\"";
    }

    public static string VocabularyLine(WritingVocabularyHint hint) =>
        string.IsNullOrWhiteSpace(hint.PartOfSpeech)
            ? $"{hint.Vietnamese} → {hint.English}"
            : $"{hint.Vietnamese} → {hint.English} ({hint.PartOfSpeech})";

    /// <summary>Splits the answer into plain and highlighted segments in order. Out-of-range highlights are ignored.</summary>
    public static IReadOnlyList<AnswerSegment> SplitAnswer(string? answer, IReadOnlyList<WritingHighlight>? highlights)
    {
        var text = answer ?? string.Empty;
        var segments = new List<AnswerSegment>();
        var position = 0;
        foreach (var highlight in (highlights ?? []).OrderBy(h => h.Start))
        {
            if (highlight.Start < position || highlight.Length <= 0 || highlight.Start + highlight.Length > text.Length)
                continue;
            if (highlight.Start > position)
                segments.Add(new AnswerSegment(text[position..highlight.Start], null));
            segments.Add(new AnswerSegment(text.Substring(highlight.Start, highlight.Length), highlight.IssueIndex));
            position = highlight.Start + highlight.Length;
        }

        if (position < text.Length)
            segments.Add(new AnswerSegment(text[position..], null));
        return segments;
    }

    /// <summary>What Enter does in the answer box: check a new answer, or go to the next sentence after a review.</summary>
    public static bool EnterMeansNext(WritingSessionState state, string? lastAnswer, string? currentText) =>
        state == WritingSessionState.Reviewed
        && string.Equals((lastAnswer ?? string.Empty).Trim(), (currentText ?? string.Empty).Trim(), StringComparison.Ordinal);
}
