using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

/// <summary>A piece of the learner's answer; <see cref="IssueIndex"/> is set when the piece is highlighted.</summary>
public readonly record struct AnswerSegment(string Text, int? IssueIndex);

/// <summary>WPF-free text and color choices for the Writing page.</summary>
public static class WritingPresentation
{
    public const string NoCoachMessage = "Configure your Gemini API key in Settings first.";

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
    public static string SetupSummary(IReadOnlyList<string>? topicNames, WritingLevel level, WritingExplanationLanguage language)
    {
        var names = (topicNames ?? []).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        var topics = names.Count switch
        {
            0 => "Mixed topics",
            <= SummaryTopicCount => string.Join(", ", names),
            _ => string.Join(", ", names.Take(SummaryTopicCount)) + $" +{names.Count - SummaryTopicCount} more"
        };
        var explain = language == WritingExplanationLanguage.Vietnamese ? "Tiếng Việt" : "English";
        return $"{topics} · {LevelLabel(level)} · Explain in {explain}";
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

    public static string PromptMeta(WritingPrompt prompt) => $"{prompt.Topic} · {LevelLabel(prompt.Level)}";

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
