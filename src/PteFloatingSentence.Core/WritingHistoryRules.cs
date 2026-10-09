namespace PteFloatingSentence.Core;

public static class WritingHistoryRules
{
    public const int MaxMistakes = 500;
    public const int MaxReviews = 300;
    public const int StatsWindowDays = 30;
    public const int MinMistakesForWeakPoints = 5;
    public const int MaxFocusTags = 3;
    public const int MaxTextLength = 400;

    /// <summary>
    /// Records every issue of a check except punctuation and capitalization, which never count as mistakes.
    /// Keeps the newest <see cref="MaxMistakes"/>.
    /// </summary>
    public static WritingHistory RecordFeedback(
        WritingHistory history,
        WritingPracticeMode mode,
        string source,
        string answer,
        WritingFeedback feedback,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(feedback);
        var added = (feedback.Issues ?? [])
            .Where(issue => issue is not null && issue.Type != WritingIssueType.Punctuation)
            .Select(issue => new WritingMistake(
                now,
                mode,
                issue.Type,
                issue.Tag,
                Clip(issue.Original),
                Clip(issue.Suggestion),
                Clip(issue.Explanation),
                Clip(source),
                Clip(answer),
                Clip(feedback.CorrectedSentence)))
            .ToList();
        if (added.Count == 0)
            return history;

        return history with { Mistakes = KeepNewest(history.Mistakes.Concat(added), MaxMistakes) };
    }

    /// <summary>
    /// Records whether a weak-point sentence was answered without a mistake of each focus tag.
    /// </summary>
    public static WritingHistory RecordFocusResult(
        WritingHistory history,
        IReadOnlyList<WritingSkillTag>? focusTags,
        WritingFeedback feedback,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(feedback);
        var tags = (focusTags ?? []).Distinct().ToList();
        if (tags.Count == 0)
            return history;

        var missed = (feedback.Issues ?? []).Where(issue => issue is not null).Select(issue => issue.Tag).ToHashSet();
        var reviews = tags.Select(tag => new WritingTagReview(now, tag, !missed.Contains(tag)));
        return history with { Reviews = KeepNewest(history.Reviews.Concat(reviews), MaxReviews) };
    }

    /// <summary>
    /// Mistake counts per tag over the last <see cref="StatsWindowDays"/> days, weakest first.
    /// Each passed weak-point sentence cancels one mistake in <see cref="WritingTagStat.Weight"/>.
    /// </summary>
    public static IReadOnlyList<WritingTagStat> TagStats(WritingHistory history, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);
        var since = now.AddDays(-StatsWindowDays);
        var passes = history.Reviews
            .Where(review => review.Passed && review.At >= since)
            .GroupBy(review => review.Tag)
            .ToDictionary(group => group.Key, group => group.Count());

        return history.Mistakes
            .Where(mistake => mistake.At >= since)
            .GroupBy(mistake => mistake.Tag)
            .Select(group =>
            {
                var passed = passes.GetValueOrDefault(group.Key);
                var count = group.Count();
                return new WritingTagStat(group.Key, count, passed, Math.Max(0, count - passed), group.Max(mistake => mistake.At));
            })
            .OrderByDescending(stat => stat.Weight)
            .ThenByDescending(stat => stat.Mistakes)
            .ThenByDescending(stat => stat.LastSeen)
            .ThenBy(stat => stat.Tag)
            .ToList();
    }

    /// <summary>Up to <see cref="MaxFocusTags"/> targetable tags the learner is still weak at, weakest first.</summary>
    public static IReadOnlyList<WritingSkillTag> WeakTags(WritingHistory history, DateTimeOffset now) =>
        TagStats(history, now)
            .Where(stat => stat.Weight > 0 && WritingSkillTags.IsTargetable(stat.Tag))
            .Take(MaxFocusTags)
            .Select(stat => stat.Tag)
            .ToList();

    /// <summary>Mistakes inside the statistics window.</summary>
    public static int RecentMistakeCount(WritingHistory history, DateTimeOffset now) =>
        history.Mistakes.Count(mistake => mistake.At >= now.AddDays(-StatsWindowDays));

    /// <summary>Weak-point practice needs enough recent mistakes and at least one tag a sentence can target.</summary>
    public static bool CanPracticeWeakPoints(WritingHistory history, DateTimeOffset now) =>
        RecentMistakeCount(history, now) >= MinMistakesForWeakPoints && WeakTags(history, now).Count > 0;

    /// <summary>The newest mistakes first.</summary>
    public static IReadOnlyList<WritingMistake> RecentMistakes(WritingHistory history, int count) =>
        history.Mistakes.OrderByDescending(mistake => mistake.At).Take(Math.Max(0, count)).ToList();

    /// <summary>Drops broken entries and keeps the newest items within the caps.</summary>
    public static WritingHistory Normalize(WritingHistory? history)
    {
        if (history is null)
            return WritingHistory.Empty;

        var mistakes = (history.Mistakes ?? [])
            .Where(mistake => mistake is not null && Enum.IsDefined(mistake.Tag) && Enum.IsDefined(mistake.Type) && Enum.IsDefined(mistake.Mode))
            .Select(mistake => mistake with
            {
                Original = Clip(mistake.Original),
                Suggestion = Clip(mistake.Suggestion),
                Explanation = Clip(mistake.Explanation),
                Source = Clip(mistake.Source),
                Answer = Clip(mistake.Answer),
                Corrected = Clip(mistake.Corrected)
            });
        var reviews = (history.Reviews ?? []).Where(review => review is not null && Enum.IsDefined(review.Tag));
        return new WritingHistory
        {
            Mistakes = KeepNewest(mistakes, MaxMistakes),
            Reviews = KeepNewest(reviews, MaxReviews)
        };
    }

    private static IReadOnlyList<WritingMistake> KeepNewest(IEnumerable<WritingMistake> items, int max)
    {
        var list = items.ToList();
        return list.Count <= max ? list : list.Skip(list.Count - max).ToList();
    }

    private static IReadOnlyList<WritingTagReview> KeepNewest(IEnumerable<WritingTagReview> items, int max)
    {
        var list = items.ToList();
        return list.Count <= max ? list : list.Skip(list.Count - max).ToList();
    }

    private static string Clip(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        return value.Length <= MaxTextLength ? value : value[..MaxTextLength];
    }
}
