using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public sealed class VocabularyWorkflowTests
{
    [TestMethod]
    public async Task AddAsync_SavesPendingLocally_BeforeExplainerCompletes()
    {
        var sentence = new StudySentence(Guid.NewGuid(), "This is a meaningful test phrase in action.");
        var list = new StudyList(Guid.NewGuid(), "List", 10, 0, [sentence]);
        var settings = new AppSettings { ActiveListId = list.Id, StudyLists = [list] };

        var tcs = new TaskCompletionSource<VocabularyExplanation>();
        var explainer = new TestExplainer(_ => tcs.Task);

        var savedSnapshots = new List<AppSettings>();
        using var workflow = new VocabularyWorkflow(explainer, () => settings, s =>
        {
            settings = s;
            savedSnapshots.Add(s);
        });

        var addOperation = workflow.AddAsync(sentence, "test phrase");

        // First save must happen immediately with Pending state
        Assert.IsTrue(savedSnapshots.Count >= 1);
        var firstSavedSentence = savedSnapshots[0].StudyLists[0].Sentences[0];
        Assert.AreEqual(1, firstSavedSentence.Vocabulary.Count);
        Assert.AreEqual("test phrase", firstSavedSentence.Vocabulary[0].Phrase);
        Assert.AreEqual(VocabularyStatus.Pending, firstSavedSentence.Vocabulary[0].Status);

        // Complete explanation
        tcs.SetResult(new VocabularyExplanation("A trial phrase.", "This is a test phrase.", "/tɛst freɪz/"));
        await addOperation;

        // Second save has Ready status
        var lastSentence = settings.StudyLists[0].Sentences[0];
        Assert.AreEqual(VocabularyStatus.Ready, lastSentence.Vocabulary[0].Status);
        Assert.AreEqual("A trial phrase.", lastSentence.Vocabulary[0].Meaning);
    }

    [TestMethod]
    public async Task AddAsync_DuplicatePhrase_DoesNotAddSecondItem()
    {
        var existing = VocabularyRules.CreatePending("test phrase");
        var sentence = new StudySentence(Guid.NewGuid(), "This is a test phrase.", Vocabulary: [existing]);
        var list = new StudyList(Guid.NewGuid(), "List", 10, 0, [sentence]);
        var settings = new AppSettings { ActiveListId = list.Id, StudyLists = [list] };

        var explainer = new TestExplainer(_ => Task.FromResult(new VocabularyExplanation("M", "E", "P")));
        using var workflow = new VocabularyWorkflow(explainer, () => settings, s => settings = s);

        var result = await workflow.AddAsync(sentence, "  TEST   PHRASE  ");

        Assert.IsNull(result);
        Assert.AreEqual(1, settings.StudyLists[0].Sentences[0].Vocabulary.Count);
    }

    [TestMethod]
    public async Task AddAsync_ExplainerFails_SetsStatusFailedAndPreservesItem()
    {
        var sentence = new StudySentence(Guid.NewGuid(), "A tricky scenario.");
        var list = new StudyList(Guid.NewGuid(), "List", 10, 0, [sentence]);
        var settings = new AppSettings { ActiveListId = list.Id, StudyLists = [list] };

        var explainer = new TestExplainer(_ => Task.FromException<VocabularyExplanation>(new HttpRequestException("Network failure")));
        using var workflow = new VocabularyWorkflow(explainer, () => settings, s => settings = s);

        await workflow.AddAsync(sentence, "tricky");

        var currentSentence = settings.StudyLists[0].Sentences[0];
        Assert.AreEqual(1, currentSentence.Vocabulary.Count);
        var item = currentSentence.Vocabulary[0];
        Assert.AreEqual(VocabularyStatus.Failed, item.Status);
        Assert.IsNotNull(item.LastError);
    }

    [TestMethod]
    public async Task RetryAsync_FailedItem_TransitionsToReadyOnSuccess()
    {
        var failedItem = VocabularyRules.CreatePending("tricky") with
        {
            Status = VocabularyStatus.Failed,
            LastError = "Network failure"
        };
        var sentence = new StudySentence(Guid.NewGuid(), "A tricky scenario.", Vocabulary: [failedItem]);
        var list = new StudyList(Guid.NewGuid(), "List", 10, 0, [sentence]);
        var settings = new AppSettings { ActiveListId = list.Id, StudyLists = [list] };

        var explainer = new TestExplainer(_ => Task.FromResult(new VocabularyExplanation("Difficult.", "A tricky question.", "/ˈtrɪki/")));
        using var workflow = new VocabularyWorkflow(explainer, () => settings, s => settings = s);

        await workflow.RetryAsync(sentence.Id, failedItem.Id);

        var currentSentence = settings.StudyLists[0].Sentences[0];
        var item = currentSentence.Vocabulary[0];
        Assert.AreEqual(VocabularyStatus.Ready, item.Status);
        Assert.AreEqual("Difficult.", item.Meaning);
        Assert.IsNull(item.LastError);
    }

    [TestMethod]
    public void SetHidden_TogglesItemHiddenStateAndSaves()
    {
        var item = VocabularyRules.CreatePending("tricky") with { IsHidden = false };
        var sentence = new StudySentence(Guid.NewGuid(), "A tricky scenario.", Vocabulary: [item]);
        var list = new StudyList(Guid.NewGuid(), "List", 10, 0, [sentence]);
        var settings = new AppSettings { ActiveListId = list.Id, StudyLists = [list] };

        var explainer = new TestExplainer(_ => Task.FromResult(new VocabularyExplanation("M", "E", "P")));
        using var workflow = new VocabularyWorkflow(explainer, () => settings, s => settings = s);

        workflow.SetHidden(sentence.Id, item.Id, true);

        var currentSentence = settings.StudyLists[0].Sentences[0];
        Assert.IsTrue(currentSentence.Vocabulary[0].IsHidden);
    }

    private sealed class TestExplainer : IVocabularyExplainer
    {
        private readonly Func<string, Task<VocabularyExplanation>> _handler;

        public TestExplainer(Func<string, Task<VocabularyExplanation>> handler)
        {
            _handler = handler;
        }

        public Task<VocabularyExplanation> ExplainAsync(string phrase, string sourceSentence, CancellationToken cancellationToken = default)
        {
            return _handler(phrase);
        }
    }
}
