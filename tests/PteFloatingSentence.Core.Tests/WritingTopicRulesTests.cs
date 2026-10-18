namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class WritingTopicRulesTests
{
    [TestMethod]
    public void Defaults_NoCustomTopicsNothingSelectedMediumEnglish()
    {
        var settings = AppSettings.Default;

        Assert.AreEqual(0, settings.CustomWritingTopics.Count);
        Assert.AreEqual(0, settings.SelectedWritingTopicKeys.Count);
        Assert.AreEqual(WritingLevel.Medium, settings.WritingLevel);
        Assert.AreEqual(WritingExplanationLanguage.English, settings.WritingExplanationLanguage);
        Assert.AreEqual(WritingTopics.BuiltIn.Count, WritingTopicRules.AllTopics(settings).Count);
    }

    [TestMethod]
    public void BuiltInTopics_HaveUniqueKeysAndNames()
    {
        Assert.AreEqual(WritingTopics.BuiltIn.Count, WritingTopics.BuiltIn.Select(t => t.Key).Distinct().Count());
        Assert.AreEqual(WritingTopics.BuiltIn.Count, WritingTopics.BuiltIn.Select(t => t.Name.ToLowerInvariant()).Distinct().Count());
        Assert.IsTrue(WritingTopics.BuiltIn.Any(t => t.Name == "Working in IT"));
        Assert.IsTrue(WritingTopics.BuiltIn.All(t => !WritingTopics.IsCustomKey(t.Key)));
    }

    [TestMethod]
    public void AddCustom_TrimsCollapsesSpacesAndAppendsAfterBuiltIns()
    {
        var (settings, result) = WritingTopicRules.AddCustom(AppSettings.Default, "  Job   interviews ");

        Assert.IsTrue(result.IsValid);
        var topic = settings.CustomWritingTopics.Single();
        Assert.AreEqual("Job interviews", topic.Name);
        Assert.IsTrue(WritingTopics.IsCustomKey(topic.Key));
        Assert.AreEqual(topic, WritingTopicRules.AllTopics(settings)[^1]);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public void AddCustom_RejectsEmptyName(string? name)
    {
        var (settings, result) = WritingTopicRules.AddCustom(AppSettings.Default, name);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Enter a topic name.", result.Error);
        Assert.AreEqual(0, settings.CustomWritingTopics.Count);
    }

    [TestMethod]
    public void AddCustom_RejectsNameLongerThanLimit()
    {
        var (_, result) = WritingTopicRules.AddCustom(AppSettings.Default, new string('a', WritingTopicRules.MaxNameLength + 1));

        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void AddCustom_RejectsDuplicateOfBuiltInOrCustomIgnoringCase()
    {
        var (_, builtIn) = WritingTopicRules.AddCustom(AppSettings.Default, "working in it");
        var (settings, _) = WritingTopicRules.AddCustom(AppSettings.Default, "Job interviews");
        var (_, custom) = WritingTopicRules.AddCustom(settings, "JOB INTERVIEWS");

        Assert.AreEqual("This topic already exists.", builtIn.Error);
        Assert.AreEqual("This topic already exists.", custom.Error);
    }

    [TestMethod]
    public void AddCustom_RejectsTopicBeyondLimit()
    {
        var settings = AppSettings.Default;
        for (var i = 0; i < WritingTopicRules.MaxCustomTopics; i++)
            settings = WritingTopicRules.AddCustom(settings, $"Topic {i}").Settings;

        var (after, result) = WritingTopicRules.AddCustom(settings, "One too many");

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(WritingTopicRules.MaxCustomTopics, after.CustomWritingTopics.Count);
    }

    [TestMethod]
    public void ToggleSelection_AddsThenRemovesKnownKeysAndIgnoresUnknown()
    {
        var selected = WritingTopicRules.ToggleSelection(AppSettings.Default, "it-work");
        selected = WritingTopicRules.ToggleSelection(selected, "traffic");
        selected = WritingTopicRules.ToggleSelection(selected, "not-a-topic");

        CollectionAssert.AreEqual(new[] { "it-work", "traffic" }, selected.SelectedWritingTopicKeys.ToArray());

        var unselected = WritingTopicRules.ToggleSelection(selected, "it-work");
        CollectionAssert.AreEqual(new[] { "traffic" }, unselected.SelectedWritingTopicKeys.ToArray());
    }

    [TestMethod]
    public void SelectedTopics_ReturnsCatalogOrder()
    {
        var settings = WritingTopicRules.ToggleSelection(AppSettings.Default, "traffic");
        settings = WritingTopicRules.ToggleSelection(settings, "it-work");

        var names = WritingTopicRules.SelectedTopics(settings).Select(t => t.Name).ToArray();

        CollectionAssert.AreEqual(new[] { "Working in IT", "Traffic & transport" }, names);
    }

    [TestMethod]
    public void RemoveCustom_DropsTopicAndItsSelectionButNeverBuiltIns()
    {
        var (settings, _) = WritingTopicRules.AddCustom(AppSettings.Default, "Job interviews");
        var key = settings.CustomWritingTopics.Single().Key;
        settings = WritingTopicRules.ToggleSelection(settings, key);
        settings = WritingTopicRules.ToggleSelection(settings, "travel");

        var removed = WritingTopicRules.RemoveCustom(settings, key);
        var builtInAttempt = WritingTopicRules.RemoveCustom(removed, "travel");

        Assert.AreEqual(0, removed.CustomWritingTopics.Count);
        CollectionAssert.AreEqual(new[] { "travel" }, removed.SelectedWritingTopicKeys.ToArray());
        CollectionAssert.AreEqual(new[] { "travel" }, builtInAttempt.SelectedWritingTopicKeys.ToArray());
        Assert.AreEqual(WritingTopics.BuiltIn.Count, WritingTopicRules.AllTopics(builtInAttempt).Count);
    }

    [TestMethod]
    public void Normalize_RepairsInvalidPersistedValues()
    {
        var settings = AppSettings.Default with
        {
            CustomWritingTopics =
            [
                new WritingTopic("custom:a", "  Job  interviews "),
                new WritingTopic("custom:a", "Duplicate key"),
                new WritingTopic("custom:b", "job interviews"),
                new WritingTopic("custom:c", "Travel"),
                new WritingTopic("travel", "Built-in key"),
                new WritingTopic("custom:d", ""),
                null!
            ],
            SelectedWritingTopicKeys = ["custom:a", "custom:b", "custom:a", "unknown", null!, "health"],
            WritingLevel = (WritingLevel)42,
            WritingExplanationLanguage = (WritingExplanationLanguage)7
        };

        var normalized = WritingTopicRules.Normalize(settings);

        Assert.AreEqual(1, normalized.CustomWritingTopics.Count);
        Assert.AreEqual(new WritingTopic("custom:a", "Job interviews"), normalized.CustomWritingTopics[0]);
        CollectionAssert.AreEqual(new[] { "custom:a", "health" }, normalized.SelectedWritingTopicKeys.ToArray());
        Assert.AreEqual(WritingLevel.Medium, normalized.WritingLevel);
        Assert.AreEqual(WritingExplanationLanguage.English, normalized.WritingExplanationLanguage);
    }

    [TestMethod]
    public void Normalize_TreatsNullListsAsEmpty()
    {
        var normalized = WritingTopicRules.Normalize(AppSettings.Default with
        {
            CustomWritingTopics = null!,
            SelectedWritingTopicKeys = null!
        });

        Assert.AreEqual(0, normalized.CustomWritingTopics.Count);
        Assert.AreEqual(0, normalized.SelectedWritingTopicKeys.Count);
    }
}
