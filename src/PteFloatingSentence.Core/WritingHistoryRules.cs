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
        // Checking the same sentence again must not record the same mistake twice.
        var known = history.Mistakes
            .Where(mistake => string.Equals(mistake.Source, Clip(source), StringComparison.Ordinal))
            .Select(SameMistakeKey)
            .ToHashSet(StringComparer.Ordinal);
        var added = (feedback.Issues ?? [])
            .Where(issue => issue is not null && issue.Type != WritingIssueType.Punctuation && !WritingFeedbackRules.IsFormattingOnly(issue))
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
                Clip(feedback.CorrectedSentence))
            {
                TopicKey = GrammarTopics.Find(issue.TopicKey)?.Key
            })
            .Where(mistake => known.Add(SameMistakeKey(mistake)))
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

    /// <summary>
    /// Repeated mistakes grouped by tag, original and suggestion (ignoring case), the most recently made first.
    /// </summary>
    public static IReadOnlyList<WritingMistakeGroup> RecentMistakeGroups(WritingHistory history, int count) =>
        history.Mistakes
            .GroupBy(SameMistakeKey, StringComparer.Ordinal)
            .Select(group => new WritingMistakeGroup(group.MaxBy(mistake => mistake.At)!, group.Count()))
            .OrderByDescending(group => group.Latest.At)
            .Take(Math.Max(0, count))
            .ToList();

    private static string SameMistakeKey(WritingMistake mistake) =>
        $"{mistake.Tag}\u001f{mistake.Original.Trim().ToLowerInvariant()}\u001f{mistake.Suggestion.Trim().ToLowerInvariant()}";

    /// <summary>Drops broken entries and keeps the newest items within the caps.</summary>
    public static WritingHistory Normalize(WritingHistory? history)
    {
        if (history is null)
            return WritingHistory.Empty;

        var mistakes = (history.Mistakes ?? [])
            .Where(mistake => mistake is not null && Enum.IsDefined(mistake.Tag) && Enum.IsDefined(mistake.Type) && Enum.IsDefined(mistake.Mode))
            // Older versions saved capitalization-only fixes such as "i" → "I" as grammar mistakes.
            .Where(mistake => mistake.Type != WritingIssueType.Punctuation
                && !WritingFeedbackRules.IsFormattingOnly(new WritingIssue(mistake.Type, mistake.Original ?? string.Empty, mistake.Suggestion ?? string.Empty, string.Empty)))
            .Select(mistake => mistake with
            {
                Original = Clip(mistake.Original),
                Suggestion = Clip(mistake.Suggestion),
                Explanation = Clip(mistake.Explanation),
                Source = Clip(mistake.Source),
                Answer = Clip(mistake.Answer),
                Corrected = Clip(mistake.Corrected),
                TopicKey = GrammarTopics.Find(mistake.TopicKey)?.Key
            });
        var reviews = (history.Reviews ?? []).Where(review => review is not null && Enum.IsDefined(review.Tag));
        var drills = (history.Drills ?? [])
            .Where(drill => drill is not null && !string.IsNullOrWhiteSpace(drill.TopicKey) && drill.Total is > 0 and <= 50 && drill.Passed >= 0 && drill.Passed <= drill.Total)
            .ToList();
        var checks = (history.Checks ?? [])
            .Where(check => check is not null && Enum.IsDefined(check.Mode) && Enum.IsDefined(check.Level) && check.Score is >= 0 and <= 100)
            .Select(check => check with { FocusTopicKey = GrammarTopics.Resolve(check.FocusTopicKey)?.Key })
            .ToList();
        var quizzes = (history.Quizzes ?? [])
            .Where(quiz => quiz is not null && !string.IsNullOrWhiteSpace(quiz.TopicKey) && quiz.Total is > 0 and <= 20 && quiz.Correct >= 0 && quiz.Correct <= quiz.Total)
            .ToList();
        var completed = (history.CompletedStages ?? [])
            .Where(completion => completion is not null && WritingRoadmap.Find(completion.StageKey) is { IsAvailable: true })
            .DistinctBy(completion => completion.StageKey)
            .ToList();
        return new WritingHistory
        {
            Mistakes = KeepNewest(mistakes, MaxMistakes),
            Reviews = KeepNewest(reviews, MaxReviews),
            Drills = Newest(drills, GrammarProgressRules.MaxDrills),
            Checks = Newest(checks, MaxChecks),
            Quizzes = Newest(quizzes, MaxQuizzes),
            RoadmapStageKey = WritingRoadmap.Find(history.RoadmapStageKey) is { IsAvailable: true } stage ? stage.Key : null,
            CompletedStages = completed,
            ActiveDays = ActiveDaysOf(history.ActiveDays ?? [])
        };
    }

    public const int MaxChecks = 1000;
    public const int MaxQuizzes = 300;
    public const int MaxActiveDays = 400;

    /// <summary>
    /// Logs the score of a checked answer (kept for progress, newest <see cref="MaxChecks"/>) and its day for the
    /// streak (newest <see cref="MaxActiveDays"/>).
    /// </summary>
    public static WritingHistory RecordCheckScore(WritingHistory history, WritingCheckRecord check)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(check);
        var record = check with { Score = Math.Clamp(check.Score, 0, 100) };
        return history with
        {
            Checks = Newest(history.Checks.Append(record).ToList(), MaxChecks),
            ActiveDays = ActiveDaysOf(history.ActiveDays.Append(DayOf(record.At)))
        };
    }

    /// <summary>The local calendar day something happened, in the offset it was recorded with.</summary>
    public static DateOnly DayOf(DateTimeOffset at) => DateOnly.FromDateTime(at.Date);

    /// <summary>Empties the mistake notebook (mistakes and weak-point results) and keeps lessons, drills and roadmap progress.</summary>
    public static WritingHistory ClearMistakes(WritingHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return history with { Mistakes = [], Reviews = [] };
    }

    private static IReadOnlyList<DateOnly> ActiveDaysOf(IEnumerable<DateOnly> days)
    {
        var sorted = days.Distinct().Order().ToList();
        return Newest(sorted, MaxActiveDays);
    }

    /// <summary>Logs a finished quick check of a lesson.</summary>
    public static WritingHistory RecordQuiz(WritingHistory history, GrammarQuizResult result)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(result.TopicKey) || result.Total <= 0)
            return history;
        var clamped = result with { Correct = Math.Clamp(result.Correct, 0, result.Total) };
        return history with { Quizzes = Newest(history.Quizzes.Append(clamped).ToList(), MaxQuizzes) };
    }

    private static IReadOnlyList<T> Newest<T>(List<T> items, int max) =>
        items.Count <= max ? items : items.Skip(items.Count - max).ToList();

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
