using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows.Infrastructure;

/// <param name="TopicNames">Selected topic names; empty means mix the built-in topics.</param>
/// <param name="Language">Language of the grammar tip.</param>
/// <param name="RecentVietnamese">Sentences already practiced in this session, to avoid repeats.</param>
public sealed record WritingRequest(
    IReadOnlyList<string> TopicNames,
    WritingLevel Level,
    WritingExplanationLanguage Language,
    IReadOnlyList<string> RecentVietnamese);

public interface IWritingCoach
{
    /// <summary>Generates a Vietnamese sentence with a hidden English reference and progressive tips.</summary>
    Task<WritingPrompt> GenerateAsync(WritingRequest request, CancellationToken cancellationToken = default);

    /// <summary>Checks the learner's English translation of <paramref name="prompt"/>.</summary>
    Task<WritingFeedback> CheckAsync(
        WritingPrompt prompt,
        string translation,
        WritingExplanationLanguage language,
        CancellationToken cancellationToken = default);
}
