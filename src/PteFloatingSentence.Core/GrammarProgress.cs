namespace PteFloatingSentence.Core;

public enum GrammarTopicStatus
{
    NotStarted = 0,
    NeedsReview = 1,
    Improving = 2,
    Good = 3
}

/// <summary>Result of a practice drill: <see cref="Passed"/> of <see cref="Total"/> sentences had no mistake on the topic.</summary>
public sealed record GrammarDrillResult(DateTimeOffset At, string TopicKey, int Passed, int Total)
{
    /// <summary>A drill passes at 80% or better.</summary>
    public bool IsPass => Total > 0 && Passed * 5 >= Total * 4;
}

/// <param name="RecentMistakes">Mistakes on the topic in the last 30 days.</param>
/// <param name="MistakesSinceDrill">Of those, the mistakes made after the latest drill (all of them without a drill).</param>
public sealed record GrammarTopicStat(
    GrammarTopic Topic,
    int RecentMistakes,
    int MistakesSinceDrill,
    GrammarDrillResult? LastDrill,
    GrammarTopicStatus Status,
    DateTimeOffset? LastMistake);

/// <summary>Which grammar the learner keeps getting wrong, and how drills change that.</summary>
public static class GrammarProgressRules
{
    /// <summary>Mistakes on a topic since the last drill that make it "needs review" and show a Learn button.</summary>
    public const int RepeatThreshold = 3;
    public const int MaxRecommendations = 5;
    public const int MaxDrills = 200;
    public const int DrillLength = 5;

    /// <summary>
    /// Status of one topic: Good after a passed drill with no new mistakes; Needs review with 3+ mistakes since the
    /// last drill; Improving with fewer mistakes since the drill or after a failed drill; otherwise Not started.
    /// </summary>
    public static GrammarTopicStat StatFor(WritingHistory history, GrammarTopic topic, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(topic);
        var since = now.AddDays(-WritingHistoryRules.StatsWindowDays);
        var mistakes = history.Mistakes
            .Where(mistake => mistake.At >= since && string.Equals(mistake.TopicKey, topic.Key, StringComparison.Ordinal))
            .ToList();
        var lastDrill = history.Drills
            .Where(drill => string.Equals(drill.TopicKey, topic.Key, StringComparison.Ordinal))
            .OrderByDescending(drill => drill.At)
            .FirstOrDefault();
        var sinceDrill = lastDrill is null ? mistakes.Count : mistakes.Count(mistake => mistake.At > lastDrill.At);

        var status = lastDrill is { IsPass: true } && sinceDrill == 0 ? GrammarTopicStatus.Good
            : sinceDrill >= RepeatThreshold ? GrammarTopicStatus.NeedsReview
            : sinceDrill > 0 || lastDrill is not null ? GrammarTopicStatus.Improving
            : GrammarTopicStatus.NotStarted;

        return new GrammarTopicStat(
            topic,
            mistakes.Count,
            sinceDrill,
            lastDrill,
            status,
            mistakes.Count == 0 ? null : mistakes.Max(mistake => mistake.At));
    }

    /// <summary>Stats for every built-in topic that has mistakes or drills.</summary>
    public static IReadOnlyList<GrammarTopicStat> Stats(WritingHistory history, DateTimeOffset now) =>
        GrammarTopics.All
            .Select(topic => StatFor(history, topic, now))
            .Where(stat => stat.Status != GrammarTopicStatus.NotStarted)
            .ToList();

    /// <summary>Topics to study next: needs review first, then improving; the most mistakes since the last drill first.</summary>
    public static IReadOnlyList<GrammarTopicStat> Recommendations(WritingHistory history, DateTimeOffset now) =>
        Stats(history, now)
            .Where(stat => stat.Status is GrammarTopicStatus.NeedsReview || (stat.Status is GrammarTopicStatus.Improving && stat.RecentMistakes > 0))
            .OrderBy(stat => stat.Status == GrammarTopicStatus.NeedsReview ? 0 : 1)
            .ThenByDescending(stat => stat.MistakesSinceDrill)
            .ThenByDescending(stat => stat.LastMistake)
            .Take(MaxRecommendations)
            .ToList();

    /// <summary>The learner's own mistakes on a topic, repeated ones grouped, newest first.</summary>
    public static IReadOnlyList<WritingMistakeGroup> MistakesFor(WritingHistory history, string topicKey, int count) =>
        WritingHistoryRules.RecentMistakeGroups(
            history with { Mistakes = history.Mistakes.Where(mistake => string.Equals(mistake.TopicKey, topicKey, StringComparison.Ordinal)).ToList() },
            count);

    /// <summary>Topics among <paramref name="issues"/> that the learner keeps getting wrong (needs review), most repeated first.</summary>
    public static IReadOnlyList<GrammarTopicStat> RepeatedTopics(WritingHistory history, IEnumerable<WritingIssue>? issues, DateTimeOffset now) =>
        (issues ?? [])
            .Select(issue => GrammarTopics.Find(issue?.TopicKey))
            .OfType<GrammarTopic>()
            .DistinctBy(topic => topic.Key)
            .Select(topic => StatFor(history, topic, now))
            .Where(stat => stat.Status == GrammarTopicStatus.NeedsReview)
            .OrderByDescending(stat => stat.MistakesSinceDrill)
            .ToList();

    public static WritingHistory RecordDrill(WritingHistory history, GrammarDrillResult result)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(result.TopicKey) || result.Total <= 0)
            return history;

        var drills = history.Drills.Append(result with { Passed = Math.Clamp(result.Passed, 0, result.Total) }).ToList();
        return history with { Drills = drills.Count <= MaxDrills ? drills : drills.Skip(drills.Count - MaxDrills).ToList() };
    }

    /// <summary>True when <paramref name="feedback"/> has no mistake on <paramref name="topic"/>; a custom topic needs a score of 80+.</summary>
    public static bool PassesTopic(WritingFeedback feedback, GrammarTopic topic)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(topic);
        var issues = (feedback.Issues ?? []).Where(issue => issue is not null && issue.Type != WritingIssueType.Punctuation).ToList();
        if (topic.IsCustom && issues.All(issue => issue.TopicKey != topic.Key))
            return feedback.Score >= 80;
        return issues.All(issue => !string.Equals(issue.TopicKey, topic.Key, StringComparison.Ordinal));
    }
}
