namespace PteFloatingSentence.Core;

public static class WritingFeedbackRules
{
    public const int CorrectThreshold = 90;
    public const int AlmostThreshold = 60;

    public static int ClampScore(int score) => Math.Clamp(score, 0, 100);

    public static WritingVerdict VerdictFor(int score) => ClampScore(score) switch
    {
        >= CorrectThreshold => WritingVerdict.Correct,
        >= AlmostThreshold => WritingVerdict.Almost,
        _ => WritingVerdict.NeedsWork
    };

    /// <summary>Maps an AI issue type such as "word_order", "Word order" or "wordOrder". Unknown values become Grammar.</summary>
    public static WritingIssueType ParseIssueType(string? value)
    {
        var key = new string((value ?? string.Empty).Where(char.IsLetter).ToArray()).ToLowerInvariant();
        return key switch
        {
            "grammar" => WritingIssueType.Grammar,
            "vocabulary" or "wordchoice" => WritingIssueType.Vocabulary,
            "spelling" => WritingIssueType.Spelling,
            "wordorder" => WritingIssueType.WordOrder,
            "missingmeaning" or "missing" => WritingIssueType.MissingMeaning,
            "extrameaning" or "extra" => WritingIssueType.ExtraMeaning,
            "punctuation" or "capitalization" or "capitalisation" => WritingIssueType.Punctuation,
            _ => WritingIssueType.Grammar
        };
    }

    /// <summary>
    /// Clamps the score, applies the punctuation-only rule (punctuation and capitalization never lower
    /// the score) and derives the verdict from the score.
    /// </summary>
    public static WritingFeedback Normalize(WritingFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        var issues = (feedback.Issues ?? []).Where(issue => issue is not null).Select(ReclassifyFormatting).ToList();
        var score = ClampScore(feedback.Score);
        if (issues.Count > 0 && issues.All(issue => issue.Type == WritingIssueType.Punctuation))
            score = 100;

        return feedback with
        {
            Score = score,
            Verdict = VerdictFor(score),
            Issues = issues
        };
    }

    /// <summary>
    /// True when the original and the suggestion have the same words and differ only in capitalization or
    /// punctuation, e.g. "i" → "I". Such issues are formatting, whatever type the AI gave them.
    /// </summary>
    public static bool IsFormattingOnly(WritingIssue issue) =>
        issue.Original.Trim().Length > 0
        && issue.Suggestion.Trim().Length > 0
        && WritingTextMatch.SameWords(issue.Original, issue.Suggestion);

    private static WritingIssue ReclassifyFormatting(WritingIssue issue) =>
        issue.Type != WritingIssueType.Punctuation && IsFormattingOnly(issue)
            ? issue with { Type = WritingIssueType.Punctuation, Tag = WritingSkillTag.Other }
            : issue;

    /// <summary>
    /// Finds each issue's <see cref="WritingIssue.Original"/> text in the answer, left to right, ignoring case.
    /// Each issue takes the first occurrence that does not overlap an earlier highlight. Text that starts or ends
    /// with a letter or digit must match on word boundaries. Issues that cannot be placed are not highlighted.
    /// </summary>
    public static IReadOnlyList<WritingHighlight> MapHighlights(string? answer, IReadOnlyList<WritingIssue>? issues)
    {
        var text = answer ?? string.Empty;
        var highlights = new List<WritingHighlight>();
        if (text.Length == 0 || issues is null)
            return highlights;

        for (var issueIndex = 0; issueIndex < issues.Count; issueIndex++)
        {
            var original = issues[issueIndex]?.Original?.Trim();
            if (string.IsNullOrEmpty(original))
                continue;

            var from = 0;
            while (from <= text.Length - original.Length)
            {
                var start = text.IndexOf(original, from, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                    break;

                var candidate = new WritingHighlight(start, original.Length, issueIndex);
                if (IsOnWordBoundaries(text, start, original) && !highlights.Any(existing => Overlaps(existing, candidate)))
                {
                    highlights.Add(candidate);
                    break;
                }

                from = start + 1;
            }
        }

        return highlights.OrderBy(highlight => highlight.Start).ToList();
    }

    private static bool IsOnWordBoundaries(string text, int start, string original)
    {
        var end = start + original.Length;
        var startOk = !char.IsLetterOrDigit(original[0]) || start == 0 || !char.IsLetterOrDigit(text[start - 1]);
        var endOk = !char.IsLetterOrDigit(original[^1]) || end == text.Length || !char.IsLetterOrDigit(text[end]);
        return startOk && endOk;
    }

    private static bool Overlaps(WritingHighlight a, WritingHighlight b) =>
        a.Start < b.Start + b.Length && b.Start < a.Start + a.Length;
}
