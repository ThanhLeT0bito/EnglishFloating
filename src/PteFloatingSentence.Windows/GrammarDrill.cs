using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

/// <summary>
/// A short practice run on one grammar topic: <see cref="Length"/> checked sentences, each passed when the answer has
/// no mistake on the topic. Each sentence counts once, even when it is checked again. UI-independent.
/// </summary>
public sealed class GrammarDrill
{
    private readonly List<bool> _results = [];
    private WritingPrompt? _countedPrompt;

    public GrammarDrill(GrammarTopic topic, int length = GrammarProgressRules.DrillLength)
    {
        Topic = topic ?? throw new ArgumentNullException(nameof(topic));
        Length = Math.Max(1, length);
    }

    public GrammarTopic Topic { get; }
    public int Length { get; }
    public IReadOnlyList<bool> Results => _results;
    public int Done => _results.Count;
    public int Passed => _results.Count(passed => passed);
    public bool IsComplete => _results.Count >= Length;

    /// <summary>Practice modes that write English and are checked by the AI; dictation and EN → VI drill in Translate.</summary>
    public static WritingPracticeMode ModeFor(WritingPracticeMode chosen) =>
        chosen is WritingPracticeMode.Dictation or WritingPracticeMode.Reverse ? WritingPracticeMode.Translate : chosen;

    /// <summary>Counts the first check of <paramref name="prompt"/>. Returns true when this check completed the drill.</summary>
    public bool Record(WritingPrompt prompt, WritingFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(feedback);
        if (IsComplete || ReferenceEquals(prompt, _countedPrompt))
            return false;

        _countedPrompt = prompt;
        _results.Add(GrammarProgressRules.PassesTopic(feedback, Topic));
        return IsComplete;
    }

    public GrammarDrillResult ToResult(DateTimeOffset at) => new(at, Topic.Key, Passed, Done);
}
