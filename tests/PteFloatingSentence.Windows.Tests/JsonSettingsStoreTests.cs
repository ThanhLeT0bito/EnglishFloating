using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public class JsonSettingsStoreTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task LoadAsync_ReturnsNormalizedVersion2Settings_AfterSaveAsync()
    {
        var path = CreateSettingsPath();
        var store = new JsonSettingsStore(path);
        var saved = AppSettings.Default with { Sentence = "Practice makes progress.", FontSize = 42 };

        await store.SaveAsync(saved);

        var loaded = await store.LoadAsync();

        Assert.AreEqual(2, loaded.Version);
        Assert.AreEqual(saved.Sentence, loaded.Sentence);
        Assert.AreEqual(saved.FontSize, loaded.FontSize);
        Assert.AreEqual(saved.ActiveListId, loaded.ActiveListId);
        Assert.AreEqual(saved.StudyLists.Single().Sentences.Single().Text, loaded.StudyLists.Single().Sentences.Single().Text);
    }

    [TestMethod]
    public async Task LoadAsync_ReturnsDefaults_WhenFileContainsInvalidJson()
    {
        var path = CreateSettingsPath();
        var store = new JsonSettingsStore(path);
        await File.WriteAllTextAsync(path, "not-json");

        var loaded = await store.LoadAsync();

        Assert.AreEqual(2, loaded.Version);
        Assert.AreEqual("Right-click this sentence to open Settings.", loaded.Sentence);
        Assert.AreEqual(1, loaded.StudyLists.Count);
    }

    [TestMethod]
    public async Task LoadAsync_NormalizesEachInvalidFieldIndependently()
    {
        var path = CreateSettingsPath();
        var store = new JsonSettingsStore(path);
        await File.WriteAllTextAsync(path, """
            {
              "Version": 2,
              "Sentence": " ",
              "FontSize": 97,
              "TextColor": "blue",
              "BackgroundOpacity": 2,
              "Left": 321,
              "Top": 654
            }
            """);

        var settings = await store.LoadAsync();

        Assert.AreEqual(2, settings.Version);
        Assert.AreEqual(AppSettings.Default.Sentence, settings.Sentence);
        Assert.AreEqual(AppSettings.Default.FontSize, settings.FontSize);
        Assert.AreEqual(AppSettings.Default.TextColor, settings.TextColor);
        Assert.AreEqual(AppSettings.Default.BackgroundOpacity, settings.BackgroundOpacity);
        Assert.AreEqual(321d, settings.Left);
        Assert.AreEqual(654d, settings.Top);
    }

    [TestMethod]
    public async Task LoadAsync_UsesDefaultColor_WhenStoredColorIsNull()
    {
        var path = CreateSettingsPath();
        var store = new JsonSettingsStore(path);
        await File.WriteAllTextAsync(path, """
            {
              "Sentence": "Steady practice builds confidence.",
              "TextColor": null
            }
            """);

        var settings = await store.LoadAsync();

        Assert.AreEqual("Steady practice builds confidence.", settings.Sentence);
        Assert.AreEqual(AppSettings.Default.TextColor, settings.TextColor);
    }

    [TestMethod]
    public async Task LoadAsync_MigratesVersion1SettingsToAStudyList()
    {
        var path = CreateSettingsPath();
        var store = new JsonSettingsStore(path);
        await File.WriteAllTextAsync(path, """
            {
              "Sentence": "Legacy text.",
              "FontSize": 42,
              "TextColor": "#FFAABBCC",
              "BackgroundOpacity": 0.6,
              "Left": 321,
              "Top": 654
            }
            """);

        var loaded = await store.LoadAsync();

        Assert.AreEqual(2, loaded.Version);
        Assert.AreEqual("My first list", loaded.StudyLists.Single().Name);
        Assert.AreEqual(10, loaded.StudyLists.Single().TargetSentenceCount);
        Assert.AreEqual("Legacy text.", loaded.StudyLists.Single().Sentences.Single().Text);
        Assert.AreEqual(42d, loaded.FontSize);
        Assert.AreEqual("#FFAABBCC", loaded.TextColor);
        Assert.AreEqual(0.6d, loaded.BackgroundOpacity);
        Assert.AreEqual(321d, loaded.Left);
        Assert.AreEqual(654d, loaded.Top);
    }

    [TestMethod]
    public async Task SaveAsync_RoundTripsNormalizedVersion2StudyLists()
    {
        var path = CreateSettingsPath();
        var store = new JsonSettingsStore(path);
        var first = new StudyList(
            Guid.NewGuid(),
            "First",
            10,
            0,
            [new StudySentence(Guid.NewGuid(), "First sentence.")]);
        var second = new StudyList(
            Guid.NewGuid(),
            "Second",
            20,
            1,
            [new StudySentence(Guid.NewGuid(), "Completed sentence.", true), new StudySentence(Guid.NewGuid(), "Second sentence.")]);
        var saved = new AppSettings
        {
            Version = 2,
            Sentence = "Second sentence.",
            ActiveListId = second.Id,
            StudyLists = [first, second]
        };

        await store.SaveAsync(saved);

        var roundTripped = await store.LoadAsync();

        Assert.AreEqual(2, roundTripped.Version);
        Assert.AreEqual(2, roundTripped.StudyLists.Count);
        Assert.AreEqual(0, roundTripped.StudyLists[0].CurrentSentenceIndex);
        Assert.AreEqual(1, roundTripped.StudyLists[1].CurrentSentenceIndex);
        Assert.AreEqual(second.Id, roundTripped.ActiveListId);
        Assert.IsTrue(roundTripped.StudyLists[1].Sentences[0].IsCompleted);
        StringAssert.Contains(await File.ReadAllTextAsync(path), "\n");
    }

    [TestMethod]
    public async Task SaveAsync_WritesVersion2JsonForNonNormalizedSettings()
    {
        var path = CreateSettingsPath();
        var store = new JsonSettingsStore(path);
        var list = new StudyList(Guid.NewGuid(), "Practice", 10, 0, [new StudySentence(Guid.NewGuid(), "Sentence.")]);
        var settings = new AppSettings
        {
            Version = 8,
            Sentence = "Sentence.",
            ActiveListId = list.Id,
            StudyLists = [list]
        };

        await store.SaveAsync(settings);

        using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.AreEqual(2, document.RootElement.GetProperty("Version").GetInt32());
    }

    private string CreateSettingsPath()
    {
        var directory = Path.Combine(TestContext.TestRunDirectory!, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "settings.json");
    }
}
