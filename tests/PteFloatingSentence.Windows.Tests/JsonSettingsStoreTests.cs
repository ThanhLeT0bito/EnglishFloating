using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public class JsonSettingsStoreTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task LoadAsync_ReturnsSavedSettings_AfterSaveAsync()
    {
        var path = CreateSettingsPath();
        var store = new JsonSettingsStore(path);
        var saved = AppSettings.Default with { Sentence = "Practice makes progress.", FontSize = 42 };

        await store.SaveAsync(saved);

        Assert.AreEqual(saved, await store.LoadAsync());
    }

    [TestMethod]
    public async Task LoadAsync_ReturnsDefaults_WhenFileContainsInvalidJson()
    {
        var path = CreateSettingsPath();
        var store = new JsonSettingsStore(path);
        await File.WriteAllTextAsync(path, "not-json");

        Assert.AreEqual(AppSettings.Default, await store.LoadAsync());
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

    private string CreateSettingsPath()
    {
        var directory = Path.Combine(TestContext.TestRunDirectory!, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "settings.json");
    }
}
