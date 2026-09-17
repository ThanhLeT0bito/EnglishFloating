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
    public async Task LoadAsync_RepairsInvalidVersion2ListFieldsWithSafeDefaults()
    {
        var path = CreateSettingsPath();
        var store = new JsonSettingsStore(path);
        await File.WriteAllTextAsync(path, """
            {
              "Version": 2,
              "Sentence": "Valid root sentence.",
              "StudyLists": [
                {
                  "Id": "00000000-0000-0000-0000-000000000000",
                  "Name": " ",
                  "TargetSentenceCount": 0,
                  "CurrentSentenceIndex": 5,
                  "Sentences": [
                    {
                      "Id": "00000000-0000-0000-0000-000000000000",
                      "Text": " "
                    }
                  ]
                }
              ]
            }
            """);

        var loaded = await store.LoadAsync();
        var list = loaded.StudyLists.Single();

        Assert.AreEqual("My first list", list.Name);
        Assert.AreEqual(10, list.TargetSentenceCount);
        Assert.AreEqual("Right-click this sentence to open Settings.", list.Sentences.Single().Text);
        Assert.AreNotEqual(Guid.Empty, list.Id);
        Assert.AreNotEqual(Guid.Empty, list.Sentences.Single().Id);
        Assert.AreEqual(0, list.CurrentSentenceIndex);
    }

    [TestMethod]
    public async Task LoadAsync_MissingVersion2ListsWithBlankRoot_CreatesValidDefaultList()
    {
        var path = CreateSettingsPath();
        var store = new JsonSettingsStore(path);
        await File.WriteAllTextAsync(path, """
            {
              "Version": 2,
              "Sentence": " ",
              "StudyLists": []
            }
            """);

        var loaded = await store.LoadAsync();
        var list = loaded.StudyLists.Single();

        Assert.AreEqual("Right-click this sentence to open Settings.", loaded.Sentence);
        Assert.AreEqual("My first list", list.Name);
        Assert.AreEqual("Right-click this sentence to open Settings.", list.Sentences.Single().Text);
        Assert.IsTrue(SentenceValidator.Validate(list.Sentences.Single().Text).IsValid);
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

    [TestMethod]
    public async Task LoadAsync_OldSettingsWithoutVocabulary_LoadsWithEmptyVocabulary()
    {
        var path = CreateSettingsPath();
        var store = new JsonSettingsStore(path);
        await File.WriteAllTextAsync(path, """
            {
              "Version": 2,
              "Sentence": "Old sentence.",
              "StudyLists": [
                {
                  "Id": "11111111-1111-1111-1111-111111111111",
                  "Name": "Legacy List",
                  "TargetSentenceCount": 5,
                  "CurrentSentenceIndex": 0,
                  "Sentences": [
                    {
                      "Id": "22222222-2222-2222-2222-222222222222",
                      "Text": "Sentence without vocabulary."
                    }
                  ]
                }
              ]
            }
            """);

        var loaded = await store.LoadAsync();
        var sentence = loaded.StudyLists.Single().Sentences.Single();

        Assert.IsNotNull(sentence.Vocabulary);
        Assert.AreEqual(0, sentence.Vocabulary.Count);
    }

    [TestMethod]
    public async Task SaveAsync_RoundTripsVocabularyItems_WithStatusAndIsHidden()
    {
        var path = CreateSettingsPath();
        var store = new JsonSettingsStore(path);
        var vocabItem = new VocabularyItem(
            Id: Guid.NewGuid(),
            Phrase: "take into account",
            NormalizedPhrase: "take into account",
            Meaning: "To consider something.",
            Example: "We must take into account all costs.",
            PronunciationIpa: "/teɪk ˈɪntuː əˈkaʊnt/",
            Status: VocabularyStatus.Ready,
            IsHidden: true,
            LastError: null);

        var sentence = new StudySentence(
            Guid.NewGuid(),
            "We must take into account all factors.",
            IsCompleted: false,
            Vocabulary: [vocabItem]);

        var list = new StudyList(Guid.NewGuid(), "List with Vocab", 10, 0, [sentence]);
        var settings = new AppSettings
        {
            Version = 2,
            Sentence = sentence.Text,
            ActiveListId = list.Id,
            StudyLists = [list],
            GeminiApiKeyConfigured = true
        };

        await store.SaveAsync(settings);

        var loaded = await store.LoadAsync();
        var loadedSentence = loaded.StudyLists.Single().Sentences.Single();

        Assert.IsTrue(loaded.GeminiApiKeyConfigured);
        Assert.AreEqual(1, loadedSentence.Vocabulary.Count);
        var loadedVocab = loadedSentence.Vocabulary[0];
        Assert.AreEqual(vocabItem.Id, loadedVocab.Id);
        Assert.AreEqual(vocabItem.Phrase, loadedVocab.Phrase);
        Assert.AreEqual(vocabItem.NormalizedPhrase, loadedVocab.NormalizedPhrase);
        Assert.AreEqual(vocabItem.Meaning, loadedVocab.Meaning);
        Assert.AreEqual(vocabItem.Example, loadedVocab.Example);
        Assert.AreEqual(vocabItem.PronunciationIpa, loadedVocab.PronunciationIpa);
        Assert.AreEqual(VocabularyStatus.Ready, loadedVocab.Status);
        Assert.IsTrue(loadedVocab.IsHidden);
    }

    [TestMethod]
    public async Task SaveAsync_SerializedJsonContainsNoPlaintextApiKey()
    {
        var path = CreateSettingsPath();
        var store = new JsonSettingsStore(path);
        var settings = AppSettings.Default with { GeminiApiKeyConfigured = true };

        await store.SaveAsync(settings);

        var rawJson = await File.ReadAllTextAsync(path);
        StringAssert.DoesNotMatch(rawJson, new System.Text.RegularExpressions.Regex(@"""(Gemini)?ApiKey""\s*:\s*""[^""]+"""));
        // Ensure GeminiApiKeyConfigured is stored without any secret key property
        using var doc = System.Text.Json.JsonDocument.Parse(rawJson);
        Assert.IsTrue(doc.RootElement.GetProperty("GeminiApiKeyConfigured").GetBoolean());
        Assert.IsFalse(doc.RootElement.TryGetProperty("ApiKey", out _));
        Assert.IsFalse(doc.RootElement.TryGetProperty("GeminiApiKey", out _));
    }

    private string CreateSettingsPath()
    {
        var directory = Path.Combine(TestContext.TestRunDirectory!, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "settings.json");
    }
}

