using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

public sealed record ListReviewSummary(
    Guid ListId,
    string ListName,
    int CompletedSentenceCount,
    int TotalSentenceCount,
    int VocabularyCount,
    int PendingOrFailedCount,
    bool IsActive);

public sealed class ReviewViewModel
{
    public IReadOnlyList<ListReviewSummary> Summaries { get; }
    public bool HasData => Summaries.Count > 0 && Summaries.Any(s => s.TotalSentenceCount > 0);
    public string EmptyStateMessage => "No study data available to review. Add sentences in the study list editor.";

    public ReviewViewModel(AppSettings settings)
    {
        Summaries = (settings.StudyLists ?? [])
            .Select(list =>
            {
                var sentences = list.Sentences ?? [];
                var vocabItems = sentences.SelectMany(s => s.Vocabulary ?? []).ToList();
                return new ListReviewSummary(
                    ListId: list.Id,
                    ListName: list.Name,
                    CompletedSentenceCount: sentences.Count(s => s.IsCompleted),
                    TotalSentenceCount: sentences.Count,
                    VocabularyCount: vocabItems.Count,
                    PendingOrFailedCount: vocabItems.Count(v => v.Status is VocabularyStatus.Pending or VocabularyStatus.Failed),
                    IsActive: list.Id == settings.ActiveListId);
            })
            .ToList();
    }
}
