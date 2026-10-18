using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public class StudyListDraftWritingTests
{
    [TestMethod]
    public void WritingPreferences_AreEditedAndSaved()
    {
        AppSettings? saved = null;
        var draft = new StudyListDraft(AppSettings.Default, settings => saved = settings);

        var added = draft.AddWritingTopic("Job interviews");
        var key = draft.Settings.CustomWritingTopics.Single().Key;
        draft.ToggleWritingTopic(key);
        draft.ToggleWritingTopic("it-work");
        draft.SetWritingLevel(WritingLevel.Hard);
        draft.SetWritingExplanationLanguage(WritingExplanationLanguage.Vietnamese);
        draft.Save();

        Assert.IsTrue(added.IsValid);
        Assert.IsNotNull(saved);
        CollectionAssert.AreEqual(new[] { key, "it-work" }, saved!.SelectedWritingTopicKeys.ToArray());
        Assert.AreEqual(WritingLevel.Hard, saved.WritingLevel);
        Assert.AreEqual(WritingExplanationLanguage.Vietnamese, saved.WritingExplanationLanguage);
    }

    [TestMethod]
    public void AddWritingTopic_InvalidNameLeavesDraftUnchanged()
    {
        var draft = new StudyListDraft(AppSettings.Default, _ => { });

        var result = draft.AddWritingTopic("Working in IT");

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(0, draft.Settings.CustomWritingTopics.Count);
    }

    [TestMethod]
    public void RemoveWritingTopic_ClearsSelection()
    {
        var draft = new StudyListDraft(AppSettings.Default, _ => { });
        draft.AddWritingTopic("Job interviews");
        var key = draft.Settings.CustomWritingTopics.Single().Key;
        draft.ToggleWritingTopic(key);

        draft.RemoveWritingTopic(key);

        Assert.AreEqual(0, draft.Settings.CustomWritingTopics.Count);
        Assert.AreEqual(0, draft.Settings.SelectedWritingTopicKeys.Count);
    }

    [TestMethod]
    public void UpdateSettingsFromApp_CopiesWritingPreferences()
    {
        var draft = new StudyListDraft(AppSettings.Default, _ => { });
        var app = WritingTopicRules.ToggleSelection(AppSettings.Default, "travel") with { WritingLevel = WritingLevel.Easy };

        draft.UpdateSettingsFromApp(app);

        CollectionAssert.AreEqual(new[] { "travel" }, draft.Settings.SelectedWritingTopicKeys.ToArray());
        Assert.AreEqual(WritingLevel.Easy, draft.Settings.WritingLevel);
    }
    [TestMethod]
    public void AddSentenceToList_AppendsWithoutChangingSelection()
    {
        var draft = new StudyListDraft(AppSettings.Default, _ => { });
        var other = draft.CreateList();
        var first = draft.Settings.StudyLists[0];
        draft.SelectList(first.Id);

        var result = draft.AddSentenceToList(other.Id, "  There was a traffic jam this morning.  ");

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(first.Id, draft.SelectedListId);
        var saved = draft.Settings.StudyLists.Single(list => list.Id == other.Id).Sentences.Single();
        Assert.AreEqual("There was a traffic jam this morning.", saved.Text);
    }

    [TestMethod]
    public void AddSentenceToList_RejectsDuplicateTooLongAndUnknownList()
    {
        var draft = new StudyListDraft(AppSettings.Default, _ => { });
        var listId = draft.Settings.StudyLists[0].Id;
        draft.AddSentenceToList(listId, "I got to the office late.");

        var duplicate = draft.AddSentenceToList(listId, "i got to the office   late.");
        var tooLong = draft.AddSentenceToList(listId, string.Join(' ', Enumerable.Repeat("word", 21)));
        var unknown = draft.AddSentenceToList(Guid.NewGuid(), "Hello there.");

        Assert.AreEqual("This sentence is already in the list.", duplicate.Error);
        Assert.IsFalse(tooLong.IsValid);
        Assert.AreEqual("Choose a study list.", unknown.Error);
        Assert.AreEqual(2, draft.Settings.StudyLists[0].Sentences.Count);
    }

    [TestMethod]
    public void AiProviderAndKeyFlags_AreEditedInDraft()
    {
        var draft = new StudyListDraft(AppSettings.Default, _ => { });

        draft.SetAiProvider(AiProvider.Claude);
        draft.SetProviderKeyConfigured(AiProvider.Claude, true);
        draft.SetAiProvider((AiProvider)99);

        Assert.AreEqual(AiProvider.Gemini, draft.Settings.AiProvider);
        Assert.IsTrue(draft.Settings.ClaudeApiKeyConfigured);
    }
}
