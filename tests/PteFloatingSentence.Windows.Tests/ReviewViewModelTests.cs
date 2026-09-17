using PteFloatingSentence.Core;
using PteFloatingSentence.Windows;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public class ReviewViewModelTests
{
    [TestMethod]
    public void ReviewViewModel_ProducesDeterministicReviewSummaries()
    {
        var vocab1 = new VocabularyItem(Guid.NewGuid(), "phrase one", "phrase one", Status: VocabularyStatus.Ready);
        var vocab2 = new VocabularyItem(Guid.NewGuid(), "phrase two", "phrase two", Status: VocabularyStatus.Pending);
        var vocab3 = new VocabularyItem(Guid.NewGuid(), "phrase three", "phrase three", Status: VocabularyStatus.Failed);

        var sentence1 = new StudySentence(Guid.NewGuid(), "Sentence one.", IsCompleted: true, Vocabulary: [vocab1, vocab2]);
        var sentence2 = new StudySentence(Guid.NewGuid(), "Sentence two.", IsCompleted: false, Vocabulary: [vocab3]);
        var sentence3 = new StudySentence(Guid.NewGuid(), "Sentence three.", IsCompleted: true, Vocabulary: []);

        var list = new StudyList(Guid.NewGuid(), "Vocabulary & Sentences", 10, 0, [sentence1, sentence2, sentence3]);
        var settings = AppSettings.Default with
        {
            StudyLists = [list],
            ActiveListId = list.Id
        };

        var vm = new ReviewViewModel(settings);

        Assert.IsTrue(vm.HasData);
        Assert.AreEqual(1, vm.Summaries.Count);

        var summary = vm.Summaries[0];
        Assert.AreEqual("Vocabulary & Sentences", summary.ListName);
        Assert.AreEqual(2, summary.CompletedSentenceCount);
        Assert.AreEqual(3, summary.TotalSentenceCount);
        Assert.AreEqual(3, summary.VocabularyCount);
        Assert.AreEqual(2, summary.PendingOrFailedCount);
        Assert.IsTrue(summary.IsActive);
    }

    [TestMethod]
    public void ReviewViewModel_EmptyStudyData_ReportsNoData()
    {
        var emptyList = new StudyList(Guid.NewGuid(), "Empty", 10, 0, []);
        var settings = AppSettings.Default with
        {
            StudyLists = [emptyList],
            ActiveListId = emptyList.Id
        };

        var vm = new ReviewViewModel(settings);

        Assert.IsFalse(vm.HasData);
        StringAssert.Contains(vm.EmptyStateMessage, "No study data");
    }

    [TestMethod]
    public void ReviewViewModel_DoesNotMutateAppSettings()
    {
        var settings = AppSettings.Default;
        var originalSentence = settings.Sentence;
        var originalActiveListId = settings.ActiveListId;

        var vm = new ReviewViewModel(settings);

        Assert.AreEqual(originalSentence, settings.Sentence);
        Assert.AreEqual(originalActiveListId, settings.ActiveListId);
    }
}
