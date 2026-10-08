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
}
