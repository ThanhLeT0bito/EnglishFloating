using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

/// <summary>WPF-free text and colors for the Roadmap page and today's session.</summary>
public static class RoadmapPresentation
{
    public static string PracticeLabel(RoadmapPractice practice) =>
        $"{WritingPresentation.ModeLabel(practice.Mode)} · {WritingPresentation.LevelLabel(practice.Level)}";

    public static string StageTitle(RoadmapStage stage) => $"Stage {stage.Number} · {stage.Title}";

    public static string StateLabel(RoadmapStageState state) => state switch
    {
        RoadmapStageState.Current => "Current",
        RoadmapStageState.Done => "Done",
        RoadmapStageState.ComingSoon => "Coming soon",
        _ => "Upcoming"
    };

    public static string StateColor(RoadmapStageState state) => state switch
    {
        RoadmapStageState.Current => "#FF3B82F6",
        RoadmapStageState.Done => "#FF34D399",
        RoadmapStageState.ComingSoon => "#FF475569",
        _ => "#FF64748B"
    };

    public static string StepTitle(RoadmapStep step) => step.Kind switch
    {
        RoadmapStepKind.Review when step.Topic is not null => $"Review: {step.Topic.Name}",
        RoadmapStepKind.Review => "Review your weak points",
        RoadmapStepKind.Learn => $"Learn: {step.Topic?.Name}",
        RoadmapStepKind.Drill => $"Drill: {step.Topic?.Name}",
        _ => $"Apply: {PracticeLabel(step.Practice)}"
    };

    /// <summary>One line of the plan, e.g. "Drill: Present simple · 5 sentences".</summary>
    public static string StepLine(RoadmapStep step) => step.Kind switch
    {
        RoadmapStepKind.Learn => $"{StepTitle(step)} · lesson + quick check",
        _ => $"{StepTitle(step)} · {step.Sentences} sentences"
    };

    public static string StepGlyph(RoadmapStepKind kind) => kind switch
    {
        RoadmapStepKind.Review => "\uE777",
        RoadmapStepKind.Learn => "\uE82D",
        RoadmapStepKind.Drill => "\uE7BE",
        _ => "\uE70F"
    };

    /// <summary>Rough length of the session in minutes (2 per sentence, 5 per lesson).</summary>
    public static int Minutes(IReadOnlyList<RoadmapStep> steps) =>
        Math.Max(5, steps.Sum(step => step.Kind == RoadmapStepKind.Learn ? 5 : (int)Math.Ceiling(step.Sentences * 1.5)));

    public static string StartButtonText(IReadOnlyList<RoadmapStep> steps) => $"Start today's session (~{Minutes(steps)} min)";

    /// <summary>Banner text during the session, e.g. "Today · Step 2 of 4 · Learn: Present perfect".</summary>
    public static string BannerText(RoadmapSession session)
    {
        var step = session.Current;
        var status = session.IsCurrentDone ? " · done ✓" : step.Kind switch
        {
            RoadmapStepKind.Learn => " · read the lesson, then answer the quick check",
            _ => string.Empty
        };
        return $"Today · Step {session.Index + 1} of {session.Steps.Count} · {StepTitle(step)}{status}";
    }

    public static string NextButtonText(RoadmapSession session) => session.IsLastStep ? "Finish" : "Next step →";

    public static string ChecklistLessons(RoadmapStageProgress progress) =>
        $"Lessons + quick check: {progress.LessonsDone}/{progress.Topics.Count}";

    public static string ChecklistDrills(RoadmapStageProgress progress) =>
        $"Drills passed (4/5+): {Math.Min(progress.DrillsPassed, progress.DrillsNeeded)}/{progress.DrillsNeeded}";

    public static string ChecklistSentences(RoadmapStageProgress progress) =>
        progress.RecentAverage is null
            ? $"Sentences in this stage: 0/{RoadmapRules.RequiredSentences} · need an average of {RoadmapRules.PassScore}"
            : $"Sentences in this stage: {Math.Min(progress.Sentences, RoadmapRules.RequiredSentences)}/{RoadmapRules.RequiredSentences} · average {progress.RecentAverage} (need {RoadmapRules.PassScore})";

    public static string ChecklistReview(RoadmapStageProgress progress) =>
        progress.NeedsReview.Count == 0
            ? "No grammar that needs review"
            : "Needs review: " + string.Join(", ", progress.NeedsReview.Select(topic => topic.Name));

    public static string TodayTitle(RoadmapStage stage) => $"Today · Stage {stage.Number} · {stage.Title}";

    public static string StageModeText(bool chosen) =>
        chosen ? "You chose this stage" : "Picked from your progress";

    public static string PercentText(RoadmapStageProgress progress) =>
        progress.Stage.IsAvailable ? $"{progress.Percent}% done" : "Coming soon";

    public static string ChecklistMark(bool done) => done ? "✓" : "○";

    /// <summary>Dot color of a topic: green mastered, amber needs review, blue started, gray not started.</summary>
    public static string TopicColor(RoadmapTopicProgress topic) =>
        topic.Status == GrammarTopicStatus.NeedsReview ? "#FFFBBF24"
        : topic.Mastered ? "#FF34D399"
        : topic.QuizPassed || topic.DrillPassed ? "#FF60A5FA"
        : "#FF64748B";

    public static string TopicTooltip(RoadmapTopicProgress topic) =>
        $"{topic.Topic.Name} · lesson {(topic.QuizPassed ? "✓" : "–")} · drill {(topic.DrillPassed ? "✓" : "–")}"
        + (topic.Status == GrammarTopicStatus.NeedsReview ? " · needs review" : string.Empty)
        + " · click to open the lesson";

    public static string PracticeTooltip(RoadmapPractice practice) =>
        $"Practice {ApplySentencesText} of {PracticeLabel(practice)}";

    private static string ApplySentencesText => $"{RoadmapRules.ApplySentences} sentences";

    public static string StreakText(int streak, int today) =>
        (streak == 0 ? "Start your streak today" : streak == 1 ? "🔥 1 day in a row" : $"🔥 {streak} days in a row")
        + (today == 0 ? string.Empty : today == 1 ? " · 1 sentence today" : $" · {today} sentences today");

    public static string SummaryText(RoadmapSessionSummary summary)
    {
        var average = summary.Average is null ? string.Empty : $" · average {summary.Average}";
        var mistakes = summary.NewMistakes == 1 ? "1 new mistake" : $"{summary.NewMistakes} new mistakes";
        return $"Session done: {summary.StepsDone}/{summary.StepsTotal} steps · {summary.Sentences} sentences{average} · {mistakes} · "
            + $"Stage {summary.Progress.Stage.Number} is {summary.Progress.Percent}% done.";
    }

    /// <summary>Summary line under a finished guided run in the Writing banner.</summary>
    public static string GuidedSummary(GuidedPractice run) =>
        run.Average is null ? $"{run.Title}: done." : $"{run.Title}: done · average {run.Average}.";

    public static string GuidedProgress(GuidedPractice run)
    {
        var label = PracticeLabel(run.Practice);
        var title = run.Title.Contains(label, StringComparison.Ordinal) ? run.Title : $"{run.Title} · {label}";
        return $"{title} · sentence {Math.Min(run.Done + 1, run.Length)} of {run.Length}"
            + (run.Average is null ? string.Empty : $" · average {run.Average}");
    }
}
