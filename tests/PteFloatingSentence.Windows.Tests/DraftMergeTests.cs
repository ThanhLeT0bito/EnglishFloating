using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows.Tests;

/// <summary>Changes the app makes while Settings is open are merged into the draft without losing unsaved edits.</summary>
[TestClass]
public sealed class DraftMergeTests
{
    private static readonly Guid ListA = Guid.NewGuid();
    private static readonly Guid ListB = Guid.NewGuid();

    private static AppSettings TwoLists() => StudyListRules.Normalize(AppSettings.Default with
    {
        StudyLists =
        [
            new StudyList(ListA, "List A", 10, 0, [new StudySentence(Guid.NewGuid(), "First sentence.")]),
            new StudyList(ListB, "List B", 10, 0, [new StudySentence(Guid.NewGuid(), "Second sentence.")])
        ],
        ActiveListId = ListA
    });

    /// <summary>What App does when the floating window saves a sentence: a separate draft, then the merged settings.</summary>
    private static AppSettings AppAddsSentence(AppSettings app, Guid listId, string text)
    {
        AppSettings? saved = null;
        var other = new StudyListDraft(app, settings => saved = settings);
        Assert.IsTrue(other.AddSentenceToList(listId, text).IsValid);
        other.Save();
        return saved!;
    }

    [TestMethod]
    public void SentenceSavedFromTheFloatingWindow_KeepsTheUnsavedEditsOfAnotherList()
    {
        var app = TwoLists();
        var draft = new StudyListDraft(app, _ => { });
        draft.SelectList(ListB);
        draft.UpdateSelectedList("Renamed here", 12);

        draft.UpdateSettingsFromApp(AppAddsSentence(app, ListA, "Saved from the floating window."));

        var lists = draft.Settings.StudyLists;
        Assert.AreEqual("Renamed here", lists.Single(list => list.Id == ListB).Name);
        Assert.AreEqual(12, lists.Single(list => list.Id == ListB).TargetSentenceCount);
        Assert.AreEqual(2, lists.Single(list => list.Id == ListA).Sentences.Count);
        Assert.AreEqual(ListB, draft.SelectedListId);
    }

    [TestMethod]
    public void BothChangedTheSameList_KeepsTheEditHereAndTheAppsNewSentence()
    {
        var app = TwoLists();
        var draft = new StudyListDraft(app, _ => { });
        draft.SelectList(ListA);
        Assert.IsTrue(draft.AddSentence("Added in Settings, not saved yet.").IsValid);

        draft.UpdateSettingsFromApp(AppAddsSentence(app, ListA, "Saved from the floating window."));

        CollectionAssert.AreEqual(
            new[] { "First sentence.", "Added in Settings, not saved yet.", "Saved from the floating window." },
            draft.Settings.StudyLists.Single(list => list.Id == ListA).Sentences.Select(sentence => sentence.Text).ToArray());
    }

    [TestMethod]
    public void AppChangesToExistingSentences_SurviveAnEditOfTheSameList()
    {
        var app = TwoLists();
        var draft = new StudyListDraft(app, _ => { });
        draft.SelectList(ListA);
        Assert.IsTrue(draft.AddSentence("Added in Settings.").IsValid);
        var first = app.StudyLists.Single(list => list.Id == ListA).Sentences[0];

        // The floating window explains a word of the first sentence and marks it completed.
        var vocabulary = new VocabularyItem(Guid.NewGuid(), "first", "first", Meaning: "đầu tiên", Status: VocabularyStatus.Ready);
        var changed = app with
        {
            StudyLists = app.StudyLists.Select(list => list.Id != ListA ? list : list with
            {
                Sentences = [first with { IsCompleted = true, Vocabulary = [vocabulary] }]
            }).ToList()
        };
        draft.UpdateSettingsFromApp(changed);

        var merged = draft.Settings.StudyLists.Single(list => list.Id == ListA).Sentences;
        Assert.AreEqual(2, merged.Count);
        Assert.IsTrue(merged[0].IsCompleted);
        Assert.AreEqual(vocabulary, merged[0].Vocabulary.Single());
        Assert.AreEqual("Added in Settings.", merged[1].Text);
    }

    [TestMethod]
    public void Merge_DoesNotRepairAnInvalidUnsavedEdit()
    {
        var app = TwoLists();
        var draft = new StudyListDraft(app, _ => { });
        draft.SelectList(ListB);
        draft.UpdateSelectedList("", 10);

        draft.UpdateSettingsFromApp(app with { ShowVocabularyCards = false });

        Assert.IsFalse(draft.Validate().IsValid, "Save must still ask for a name");
    }

    [TestMethod]
    public void WritingOptions_TakeWhatEachSideChanged()
    {
        var app = TwoLists() with { WritingLevel = WritingLevel.Medium, WritingPracticeMode = WritingPracticeMode.Translate };
        var draft = new StudyListDraft(app, _ => { });
        draft.SetWritingPracticeMode(WritingPracticeMode.Combine);

        // The floating window changes the level only.
        draft.UpdateSettingsFromApp(app with { WritingLevel = WritingLevel.Hard });

        Assert.AreEqual(WritingLevel.Hard, draft.Settings.WritingLevel, "changed by the app");
        Assert.AreEqual(WritingPracticeMode.Combine, draft.Settings.WritingPracticeMode, "changed here, not by the app");

        // A later update that does not touch the mode keeps the edit here too.
        draft.UpdateSettingsFromApp(app with { WritingLevel = WritingLevel.Hard, ShowVocabularyCards = false });
        Assert.AreEqual(WritingPracticeMode.Combine, draft.Settings.WritingPracticeMode);
        Assert.IsFalse(draft.Settings.ShowVocabularyCards);
    }

    [TestMethod]
    public void ListRemovedByTheApp_IsDroppedUnlessEditedHere()
    {
        var app = TwoLists();
        var draft = new StudyListDraft(app, _ => { });
        draft.SelectList(ListB);

        draft.UpdateSettingsFromApp(app with { StudyLists = app.StudyLists.Where(list => list.Id != ListB).ToList() });

        Assert.IsFalse(draft.Settings.StudyLists.Any(list => list.Id == ListB));
        Assert.AreEqual(ListA, draft.SelectedListId, "the selection moves to a list that still exists");
    }

    [TestMethod]
    public void PenAction_IsSavedAndKeptThroughAMerge()
    {
        AppSettings? saved = null;
        var app = TwoLists();
        var draft = new StudyListDraft(app, settings => saved = settings);
        draft.SetPenAction(FloatingPenAction.ListPractice);

        draft.UpdateSettingsFromApp(app with { ShowVocabularyCards = false });
        Assert.AreEqual(FloatingPenAction.ListPractice, draft.Settings.PenAction);

        draft.Save();
        Assert.AreEqual(FloatingPenAction.ListPractice, SettingsUpdateMerger.MergeEditableFields(app, saved!).PenAction);
        Assert.AreEqual(FloatingPenAction.Writing, SettingsUpdateMerger.MergeEditableFields(app, saved! with { PenAction = (FloatingPenAction)7 }).PenAction);
    }

    [TestMethod]
    public void Validate_ChecksWithoutSaving()
    {
        var saves = 0;
        var draft = new StudyListDraft(TwoLists(), _ => saves++);
        draft.SelectList(ListA);
        draft.UpdateSelectedList("", 10);

        Assert.IsFalse(draft.Validate().IsValid);
        Assert.AreEqual(0, saves);
    }
}
