using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

/// <summary>What a finished (or ended) session achieved, measured from the notebook.</summary>
public sealed record RoadmapSessionSummary(int Sentences, int? Average, int NewMistakes, int StepsDone, int StepsTotal, RoadmapStageProgress Progress, int Streak);

/// <summary>
/// Today's guided session: an ordered list of roadmap steps, the current step and whether it is done.
/// The host runs each step (open a lesson, start a drill or a guided run) and reports completion. UI-independent.
/// </summary>
public sealed class RoadmapSession
{
    private readonly bool[] _done;

    public RoadmapSession(RoadmapStage stage, IReadOnlyList<RoadmapStep> steps, DateTimeOffset startedAt)
    {
        Stage = stage ?? throw new ArgumentNullException(nameof(stage));
        Steps = steps ?? throw new ArgumentNullException(nameof(steps));
        if (Steps.Count == 0)
            throw new ArgumentException("A session needs at least one step.", nameof(steps));
        StartedAt = startedAt;
        _done = new bool[Steps.Count];
    }

    public RoadmapStage Stage { get; }
    public IReadOnlyList<RoadmapStep> Steps { get; }
    public DateTimeOffset StartedAt { get; }
    public int Index { get; private set; }
    public bool IsFinished { get; private set; }
    public RoadmapStep Current => Steps[Math.Min(Index, Steps.Count - 1)];
    public bool IsCurrentDone => _done[Math.Min(Index, Steps.Count - 1)];
    public bool IsLastStep => Index >= Steps.Count - 1;
    public int StepsDone => _done.Count(done => done);

    public event Action? Changed;

    /// <summary>
    /// Marks a step of <paramref name="kind"/> (on <paramref name="topicKey"/>, when the step has a topic) done: the
    /// current step first, otherwise the next matching step not done yet, so work done ahead (e.g. a drill started from
    /// the lesson) counts when the session gets there. Returns false when no step matches.
    /// </summary>
    public bool Complete(RoadmapStepKind kind, string? topicKey = null)
    {
        if (IsFinished)
            return false;

        for (var index = Index; index < Steps.Count; index++)
        {
            if (!Matches(index, kind, topicKey))
                continue;

            _done[index] = true;
            Changed?.Invoke();
            return true;
        }

        return false;
    }

    private bool Matches(int index, RoadmapStepKind kind, string? topicKey)
    {
        var step = Steps[index];
        if (_done[index] || step.Kind != kind)
            return false;
        return topicKey is null || step.Topic is null || string.Equals(step.Topic.Key, topicKey, StringComparison.Ordinal);
    }

    /// <summary>Moves to the next step (done or skipped). Returns false and finishes after the last step.</summary>
    public bool Advance()
    {
        if (IsFinished)
            return false;
        if (IsLastStep)
        {
            Finish();
            return false;
        }

        Index++;
        Changed?.Invoke();
        return true;
    }

    public void Finish()
    {
        if (IsFinished)
            return;
        IsFinished = true;
        Changed?.Invoke();
    }

    /// <summary>Sentences, average score and new mistakes since the session started.</summary>
    public RoadmapSessionSummary Summarize(WritingHistory history, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);
        var checks = history.Checks.Where(check => check.At >= StartedAt).ToList();
        int? average = checks.Count == 0 ? null : (int)Math.Round(checks.Average(check => check.Score), MidpointRounding.AwayFromZero);
        return new RoadmapSessionSummary(
            checks.Count,
            average,
            history.Mistakes.Count(mistake => mistake.At >= StartedAt),
            StepsDone,
            Steps.Count,
            RoadmapRules.Progress(history, Stage, now),
            RoadmapRules.Streak(history, now));
    }
}
