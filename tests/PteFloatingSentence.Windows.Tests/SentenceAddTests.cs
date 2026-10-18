using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

/// <summary>Adding many sentences at once, and the AI split of pasted text.</summary>
[TestClass]
public sealed class SentenceAddTests
{
    private static readonly string TwentyOneWords = string.Join(' ', Enumerable.Range(1, 21).Select(i => $"word{i}")) + ".";

    [TestMethod]
    public void AddSentences_AddsInOrderSkipsDuplicatesAndReturnsWhatFailed()
    {
        var draft = new StudyListDraft(AppSettings.Default, _ => { });
        var before = draft.SelectedList.Sentences.Count;
        var existing = draft.SelectedList.Sentences.FirstOrDefault()?.Text;
        var selectedBefore = draft.SelectedSentenceId;
        var index = draft.SelectedList.CurrentSentenceIndex;

        var texts = new List<string> { "There was a lot of traffic this morning.", TwentyOneWords, "there was a LOT of  traffic this morning.", "Please raise your hand." };
        if (existing is not null)
            texts.Add(existing);
        var outcome = draft.AddSentences(texts);

        Assert.AreEqual(2, outcome.Added);
        Assert.AreEqual(existing is null ? 1 : 2, outcome.Duplicates);
        CollectionAssert.AreEqual(new[] { TwentyOneWords }, outcome.Rejected.ToArray());
        Assert.AreEqual("Use 20 words or fewer.", outcome.FirstError);
        var sentences = draft.SelectedList.Sentences;
        Assert.AreEqual(before + 2, sentences.Count);
        Assert.AreEqual("There was a lot of traffic this morning.", sentences[^2].Text);
        Assert.AreEqual("Please raise your hand.", sentences[^1].Text);
        Assert.AreEqual(selectedBefore, draft.SelectedSentenceId, "the selection stays");
        Assert.AreEqual(index, draft.SelectedList.CurrentSentenceIndex, "the widget's sentence stays");
    }

    [TestMethod]
    public void AddSentencesMessage_SaysWhatHappened()
    {
        Assert.AreEqual("Added 3 sentences.", new AddSentencesResult(3, 0, [], null).Message);
        Assert.AreEqual("Added 1 sentence · 2 were already in the list · 1 not added: Use 20 words or fewer. Fix it in the box and add again.",
            new AddSentencesResult(1, 2, ["x"], "Use 20 words or fewer.").Message);
    }

    [TestMethod]
    public async Task AiSplitter_SendsTheTextAndReturnsCleanLines()
    {
        var client = new FakeClient("{\"sentences\":[\"  Stock predictions can be very capricious.  \",\"\",\"We are   looking for new ways.\"]}");
        var splitter = new AiSentenceSplitter(client);

        var sentences = await splitter.SplitAsync("1. Stock predictions can be very capri-\ncious. 2. We are looking for new ways.");

        CollectionAssert.AreEqual(new[] { "Stock predictions can be very capricious.", "We are looking for new ways." }, sentences.ToArray());
        StringAssert.Contains(client.UserText!, "capri-");
        StringAssert.Contains(client.SystemText!, "Do not paraphrase");
    }

    [TestMethod]
    public async Task AiSplitter_RejectsEmptyTooLongAndBrokenAnswers()
    {
        var splitter = new AiSentenceSplitter(new FakeClient("{\"sentences\":[]}"));

        Assert.AreEqual(AiSentenceSplitter.EmptyMessage, (await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => splitter.SplitAsync("  12 "))).Message);
        Assert.AreEqual(AiSentenceSplitter.TooLongMessage, (await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => splitter.SplitAsync(new string('a', AiSentenceSplitter.MaxInputLength + 1)))).Message);
        Assert.AreEqual(AiSentenceSplitter.InvalidMessage, (await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => splitter.SplitAsync("Some text."))).Message);
        Assert.AreEqual(AiSentenceSplitter.InvalidMessage, Assert.ThrowsException<InvalidOperationException>(() => AiSentenceSplitter.Parse("not json")).Message);
    }

    private sealed class FakeClient(string answer) : IJsonLlmClient
    {
        public string ProviderName => "Fake";
        public bool HasKey => true;
        public string? SystemText { get; private set; }
        public string? UserText { get; private set; }

        public Task<string> GenerateJsonAsync(string systemInstruction, string userText, object responseSchema, double? temperature = null, CancellationToken cancellationToken = default)
        {
            SystemText = systemInstruction;
            UserText = userText;
            return Task.FromResult(answer);
        }

    }
}
