namespace PteFloatingSentence.Core;

public enum RoadmapStageState
{
    Upcoming = 0,
    Current = 1,
    Done = 2,
    ComingSoon = 3
}

/// <param name="QuizPassed">The lesson's quick check was answered at least two thirds right.</param>
/// <param name="DrillPassed">The latest 5-sentence drill passed (4/5 or better).</param>
public sealed record RoadmapTopicProgress(GrammarTopic Topic, bool QuizPassed, bool DrillPassed, GrammarTopicStatus Status)
{
    /// <summary>Learned, drilled and not repeatedly wrong right now.</summary>
    public bool Mastered => QuizPassed && DrillPassed && Status != GrammarTopicStatus.NeedsReview;
}

/// <summary>How far the learner is in a stage; see <see cref="RoadmapRules"/> for the targets.</summary>
/// <param name="CompletedBefore">The stage was finished earlier and saved, so it stays done.</param>
public sealed record RoadmapStageProgress(
    RoadmapStage Stage,
    IReadOnlyList<RoadmapTopicProgress> Topics,
    int Sentences,
    int? RecentAverage,
    bool CompletedBefore = false)
{
    public int LessonsDone => Topics.Count(topic => topic.QuizPassed);
    public int DrillsPassed => Topics.Count(topic => topic.DrillPassed);
    public int DrillsNeeded => RoadmapRules.DrillsNeeded(Topics.Count);
    public IReadOnlyList<GrammarTopic> NeedsReview => Topics.Where(topic => topic.Status == GrammarTopicStatus.NeedsReview).Select(topic => topic.Topic).ToList();

    public bool LessonsComplete => Topics.Count > 0 && LessonsDone == Topics.Count;
    public bool DrillsComplete => DrillsPassed >= DrillsNeeded;
    public bool SentencesComplete => Sentences >= RoadmapRules.RequiredSentences && RecentAverage >= RoadmapRules.PassScore;
    /// <summary>Meets every target right now.</summary>
    public bool MeetsTargets => Stage.IsAvailable && LessonsComplete && DrillsComplete && SentencesComplete && NeedsReview.Count == 0;

    /// <summary>Done now, or finished earlier: a finished stage does not reopen when old entries leave the logs.</summary>
    public bool IsComplete => Stage.IsAvailable && (CompletedBefore || MeetsTargets);

    /// <summary>0-100: the average of lessons, drills, sentence count and average score, each capped at 100%.</summary>
    public int Percent
    {
        get
        {
            if (!Stage.IsAvailable)
                return 0;
            if (IsComplete)
                return 100;
            var lessons = Topics.Count == 0 ? 0 : (double)LessonsDone / Topics.Count;
            var drills = Math.Min(1, (double)DrillsPassed / Math.Max(1, DrillsNeeded));
            var sentences = Math.Min(1, (double)Sentences / RoadmapRules.RequiredSentences);
            var average = RecentAverage is null ? 0 : Math.Min(1, (double)RecentAverage.Value / RoadmapRules.PassScore);
            return Math.Min(99, (int)Math.Floor((lessons + drills + sentences + average) / 4 * 100));
        }
    }
}

public enum RoadmapStepKind
{
    Review = 0,
    Learn = 1,
    Drill = 2,
    Apply = 3
}

/// <summary>One step of today's session. Learn opens a lesson; the others are sentences on the Writing page.</summary>
public sealed record RoadmapStep(
    RoadmapStepKind Kind,
    GrammarTopic? Topic,
    RoadmapPractice Practice,
    int Sentences,
    IReadOnlyList<WritingSkillTag> FocusTags);

/// <summary>Progress through the roadmap, the current stage and the daily session plan. UI-free.</summary>
public static class RoadmapRules
{
    public const int PassScore = 75;
    public const int RequiredSentences = 15;
    public const int ReviewSentences = 3;
    public const int ApplySentences = 8;

    /// <summary>Drills a stage needs: two thirds of its topics, rounded up.</summary>
    public static int DrillsNeeded(int topicCount) => topicCount == 0 ? 0 : (int)Math.Ceiling(topicCount * 2 / 3.0);

    /// <summary>True when a check was practice for <paramref name="stage"/>: one of its exercises, or a drill of one of its topics.</summary>
    public static bool BelongsTo(WritingCheckRecord check, RoadmapStage stage) =>
        stage.Practices.Any(practice => practice.Mode == check.Mode && practice.Level == check.Level)
        || (check.FocusTopicKey is not null && stage.TopicKeys.Contains(check.FocusTopicKey, StringComparer.Ordinal));

    public static RoadmapStageProgress Progress(WritingHistory history, RoadmapStage stage, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(stage);
        var topics = stage.Topics
            .Select(topic =>
            {
                var stat = GrammarProgressRules.StatFor(history, topic, now);
                return new RoadmapTopicProgress(
                    topic,
                    history.Quizzes.Any(quiz => quiz.TopicKey == topic.Key && quiz.IsPass),
                    stat.LastDrill is { IsPass: true },
                    stat.Status);
            })
            .ToList();
        var checks = history.Checks.Where(check => BelongsTo(check, stage)).ToList();
        var recent = checks.TakeLast(RequiredSentences).ToList();
        int? average = recent.Count == 0 ? null : (int)Math.Round(recent.Average(check => check.Score), MidpointRounding.AwayFromZero);
        var completedBefore = history.CompletedStages.Any(completion => string.Equals(completion.StageKey, stage.Key, StringComparison.Ordinal));
        return new RoadmapStageProgress(stage, topics, checks.Count, average, completedBefore);
    }

    /// <summary>
    /// Saves every available stage that meets its targets now, so it stays done later even when its sentences,
    /// drills or quick checks leave the capped logs, or later practice lowers its average.
    /// </summary>
    public static WritingHistory RecordCompletions(WritingHistory history, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);
        var done = history.CompletedStages.Select(completion => completion.StageKey).ToHashSet(StringComparer.Ordinal);
        var added = WritingRoadmap.All
            .Where(stage => stage.IsAvailable && !done.Contains(stage.Key) && Progress(history, stage, now).MeetsTargets)
            .Select(stage => new RoadmapStageCompletion(stage.Key, now))
            .ToList();
        return added.Count == 0 ? history : history with { CompletedStages = history.CompletedStages.Concat(added).ToList() };
    }

    /// <summary>Progress of every stage with its state: done, current, upcoming or coming soon.</summary>
    public static IReadOnlyList<(RoadmapStageProgress Progress, RoadmapStageState State)> Overview(WritingHistory history, DateTimeOffset now)
    {
        var progress = WritingRoadmap.All.Select(stage => Progress(history, stage, now)).ToList();
        var current = CurrentStage(history, progress);
        return progress
            .Select(item => (item, !item.Stage.IsAvailable ? RoadmapStageState.ComingSoon
                : item.Stage.Key == current.Key ? RoadmapStageState.Current
                : item.IsComplete ? RoadmapStageState.Done
                : RoadmapStageState.Upcoming))
            .ToList();
    }

    /// <summary>The stage the learner chose, otherwise the first available stage that is not complete.</summary>
    public static RoadmapStage CurrentStage(WritingHistory history, DateTimeOffset now) =>
        CurrentStage(history, WritingRoadmap.All.Select(stage => Progress(history, stage, now)).ToList());

    private static RoadmapStage CurrentStage(WritingHistory history, IReadOnlyList<RoadmapStageProgress> progress)
    {
        if (WritingRoadmap.Find(history.RoadmapStageKey) is { IsAvailable: true } chosen)
            return chosen;
        return progress.FirstOrDefault(item => item.Stage.IsAvailable && !item.IsComplete)?.Stage
            ?? WritingRoadmap.All.Last(stage => stage.IsAvailable);
    }

    public static WritingHistory ChooseStage(WritingHistory history, string? stageKey) =>
        history with { RoadmapStageKey = WritingRoadmap.Find(stageKey) is { IsAvailable: true } stage ? stage.Key : null };

    /// <summary>
    /// Today's session for <paramref name="progress"/>: review the grammar the learner keeps getting wrong (if any),
    /// learn the next topic whose lesson is not passed, drill the next topic that is not mastered, then apply the
    /// stage in its exercises (the main one, rotating daily through the stage's exercises).
    /// </summary>
    public static IReadOnlyList<RoadmapStep> TodayPlan(WritingHistory history, RoadmapStageProgress progress, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(progress);
        var stage = progress.Stage;
        if (!stage.IsAvailable)
            return [];

        var main = stage.Practices[0];
        // Writing exercises only: drills and reviews need English answers checked by the AI.
        var writing = main.Mode is WritingPracticeMode.Dictation or WritingPracticeMode.Reverse
            ? new RoadmapPractice(WritingPracticeMode.Translate, main.Level)
            : main;
        var steps = new List<RoadmapStep>();

        // Every topic that needs review, not only the top recommendations, so the stage's own topic comes first.
        var weakTopic = GrammarProgressRules.Stats(history, now)
            .Where(stat => stat.Status == GrammarTopicStatus.NeedsReview)
            .OrderBy(stat => stage.TopicKeys.Contains(stat.Topic.Key, StringComparer.Ordinal) ? 0 : 1)
            .ThenByDescending(stat => stat.MistakesSinceDrill)
            .ThenByDescending(stat => stat.LastMistake)
            .Select(stat => stat.Topic)
            .FirstOrDefault();
        if (weakTopic is not null)
        {
            steps.Add(new RoadmapStep(RoadmapStepKind.Review, weakTopic, writing, ReviewSentences, []));
        }
        else if (WritingHistoryRules.CanPracticeWeakPoints(history, now))
        {
            steps.Add(new RoadmapStep(RoadmapStepKind.Review, null, writing, ReviewSentences, WritingHistoryRules.WeakTags(history, now)));
        }

        var learn = progress.Topics.FirstOrDefault(topic => !topic.QuizPassed)?.Topic;
        if (learn is not null)
            steps.Add(new RoadmapStep(RoadmapStepKind.Learn, learn, writing, 0, []));

        var drill = learn
            ?? progress.Topics.FirstOrDefault(topic => !topic.Mastered)?.Topic
            ?? progress.Topics.OrderBy(topic => topic.Status == GrammarTopicStatus.Good ? 1 : 0).Select(topic => topic.Topic).FirstOrDefault();
        if (drill is not null && drill.Key != weakTopic?.Key)
            steps.Add(new RoadmapStep(RoadmapStepKind.Drill, drill, writing, GrammarProgressRules.DrillLength, []));

        var apply = stage.Practices[DayIndex(now) % stage.Practices.Count];
        steps.Add(new RoadmapStep(RoadmapStepKind.Apply, null, apply, ApplySentences, []));
        return steps;
    }

    /// <summary>
    /// Days in a row (ending today, or yesterday when nothing was checked yet today) with at least one check. Each
    /// check counts on the local date it was made, so a change of time zone or daylight saving does not move it.
    /// </summary>
    public static int Streak(WritingHistory history, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);
        var days = history.ActiveDays.Concat(history.Checks.Select(check => WritingHistoryRules.DayOf(check.At))).ToHashSet();
        var day = WritingHistoryRules.DayOf(now);
        if (!days.Contains(day))
            day = day.AddDays(-1);
        var streak = 0;
        while (days.Contains(day))
        {
            streak++;
            day = day.AddDays(-1);
        }

        return streak;
    }

    public static int ChecksToday(WritingHistory history, DateTimeOffset now) =>
        history.Checks.Count(check => WritingHistoryRules.DayOf(check.At) == WritingHistoryRules.DayOf(now));

    private static int DayIndex(DateTimeOffset now) => (int)(now.Date - DateTime.UnixEpoch.Date).TotalDays;
}
