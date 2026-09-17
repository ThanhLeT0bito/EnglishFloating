using PteFloatingSentence.Core;

namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class StudyListRulesTests
{
    [TestMethod]
    public void CreateDefault_UsesLegacySentenceInMyFirstList()
    {
        var settings = StudyListRules.CreateDefault("A useful practice sentence.");

        Assert.AreEqual(2, settings.Version);
        Assert.AreEqual("My first list", settings.StudyLists.Single().Name);
        Assert.AreEqual(10, settings.StudyLists.Single().TargetSentenceCount);
        Assert.AreEqual("A useful practice sentence.", settings.StudyLists.Single().Sentences.Single().Text);
        Assert.IsTrue(settings.ShowSentenceOverlay);
        Assert.IsTrue(settings.ShowVocabularyCards);
    }

    [TestMethod]
    public void DisplayPreferences_DefaultToTrue()
    {
        var settings = new AppSettings();

        Assert.IsTrue(settings.ShowSentenceOverlay);
        Assert.IsTrue(settings.ShowVocabularyCards);
    }

    [TestMethod]
    public void ActiveList_InvalidActiveId_UsesFirstList()
    {
        var first = CreateList("First");
        var second = CreateList("Second");
        var settings = new AppSettings { StudyLists = [first, second], ActiveListId = Guid.NewGuid() };

        var active = StudyListRules.ActiveList(settings);

        Assert.AreEqual(first.Id, active.Id);
    }

    [TestMethod]
    public void Normalize_ClampsSentenceIndexes()
    {
        var tooHigh = CreateList("High", currentSentenceIndex: 8, sentences: [Sentence("one"), Sentence("two")]);
        var tooLow = CreateList("Low", currentSentenceIndex: -1, sentences: [Sentence("three")]);

        var normalized = StudyListRules.Normalize(new AppSettings { StudyLists = [tooHigh, tooLow], ActiveListId = tooHigh.Id });

        Assert.AreEqual(1, normalized.StudyLists[0].CurrentSentenceIndex);
        Assert.AreEqual(0, normalized.StudyLists[1].CurrentSentenceIndex);
    }

    [TestMethod]
    public void MoveCurrentSentence_EmptyActiveList_ReturnsUnchangedSettings()
    {
        var emptyList = CreateList("Empty", currentSentenceIndex: 0, sentences: []);
        var settings = new AppSettings { StudyLists = [emptyList], ActiveListId = emptyList.Id };

        var moved = StudyListRules.MoveCurrentSentence(settings, 1);

        Assert.AreSame(settings, moved);
    }

    [TestMethod]
    public void ValidateList_TargetBelowOne_ReturnsInvalid()
    {
        var result = StudyListRules.ValidateList(CreateList("Practice", targetSentenceCount: 0));

        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void ValidateList_InvalidSentence_ReturnsSentenceValidationError()
    {
        var result = StudyListRules.ValidateList(CreateList("Practice", sentences: [Sentence("")]));

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Enter a sentence.", result.Error);
    }

    [TestMethod]
    public void Normalize_RepairsMissingAndDuplicateIds()
    {
        var duplicateId = Guid.NewGuid();
        var duplicatedSentenceId = Guid.NewGuid();
        var first = new StudyList(duplicateId, "One", 10, 0, [new StudySentence(duplicatedSentenceId, "one")]);
        var second = new StudyList(duplicateId, "Two", 10, 0, [new StudySentence(duplicatedSentenceId, "two"), new StudySentence(Guid.Empty, "three")]);

        var normalized = StudyListRules.Normalize(new AppSettings { StudyLists = [first, second], ActiveListId = duplicateId });
        var lists = normalized.StudyLists;
        var sentenceIds = lists.SelectMany(list => list.Sentences).Select(sentence => sentence.Id).ToList();

        Assert.AreEqual(2, lists.Select(list => list.Id).Distinct().Count());
        Assert.IsTrue(lists.All(list => list.Id != Guid.Empty));
        Assert.AreEqual(sentenceIds.Count, sentenceIds.Distinct().Count());
        Assert.IsTrue(sentenceIds.All(id => id != Guid.Empty));
    }

    [TestMethod]
    public void MoveCurrentSentence_WrapsWithoutChangingCompletion()
    {
        var moved = StudyListRules.MoveCurrentSentence(SettingsWith("one", "two"), -1);
        var list = StudyListRules.ActiveList(moved);

        Assert.AreEqual(1, list.CurrentSentenceIndex);
        Assert.IsFalse(list.Sentences[1].IsCompleted);
    }

    private static AppSettings SettingsWith(params string[] sentenceTexts)
    {
        var list = CreateList("Practice", sentences: sentenceTexts.Select(Sentence).ToList());
        return new AppSettings { StudyLists = [list], ActiveListId = list.Id };
    }

    private static StudyList CreateList(
        string name,
        int targetSentenceCount = 10,
        int currentSentenceIndex = 0,
        IReadOnlyList<StudySentence>? sentences = null) =>
        new(Guid.NewGuid(), name, targetSentenceCount, currentSentenceIndex, sentences ?? []);

    private static StudySentence Sentence(string text) => new(Guid.NewGuid(), text);
}
