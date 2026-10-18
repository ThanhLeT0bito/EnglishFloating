using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

/// <summary>
/// A guided run of sentences for one roadmap step (review or apply): a fixed exercise and level, optionally focused on
/// a grammar topic or weak points. Each sentence counts once, on its first check. UI-independent.
/// </summary>
public sealed class GuidedPractice
{
    private readonly List<int> _scores = [];
    private WritingPrompt? _countedPrompt;

    public GuidedPractice(
        string title,
        RoadmapPractice practice,
        int length,
        GrammarTopic? topic = null,
        IReadOnlyList<WritingSkillTag>? focusTags = null,
        RoadmapStepKind kind = RoadmapStepKind.Apply)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Practice" : title.Trim();
        Practice = practice ?? throw new ArgumentNullException(nameof(practice));
        Length = Math.Max(1, length);
        Topic = topic;
        FocusTags = topic is null ? focusTags ?? [] : [];
        Kind = kind;
    }

    /// <summary>Builds the run for a review or apply step.</summary>
    public static GuidedPractice ForStep(RoadmapStep step) => new(
        RoadmapPresentation.StepTitle(step),
        step.Practice,
        step.Sentences,
        step.Topic,
        step.FocusTags,
        step.Kind);

    /// <summary>The roadmap step this run belongs to (review or apply).</summary>
    public RoadmapStepKind Kind { get; }
    public string Title { get; }
    public RoadmapPractice Practice { get; }
    public int Length { get; }
    public GrammarTopic? Topic { get; }
    public IReadOnlyList<WritingSkillTag> FocusTags { get; }
    public IReadOnlyList<int> Scores => _scores;
    public int Done => _scores.Count;
    public bool IsComplete => _scores.Count >= Length;
    public int? Average => _scores.Count == 0 ? null : (int)Math.Round(_scores.Average(), MidpointRounding.AwayFromZero);

    /// <summary>Counts the first check of <paramref name="prompt"/>. Returns true when this check completed the run.</summary>
    public bool Record(WritingPrompt prompt, WritingFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(feedback);
        if (IsComplete || ReferenceEquals(prompt, _countedPrompt))
            return false;

        _countedPrompt = prompt;
        _scores.Add(feedback.Score);
        return IsComplete;
    }
}
