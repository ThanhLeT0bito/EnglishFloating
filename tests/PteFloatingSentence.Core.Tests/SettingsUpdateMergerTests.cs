namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class SettingsUpdateMergerTests
{
    [TestMethod]
    public void DefaultSettings_UsesTextHintsForExistingSettings()
    {
        Assert.AreEqual(PracticeMode.TextHints, AppSettings.Default.PracticeMode);
    }

    [TestMethod]
    public void MergeEditableFields_RetainsSubmittedAudioCountdownSeconds()
    {
        var latest = AppSettings.Default;
        Assert.AreEqual(3, latest.PracticeAudioDelaySeconds);

        var merged = SettingsUpdateMerger.MergeEditableFields(latest, latest with { PracticeAudioDelaySeconds = 5 });
        Assert.AreEqual(5, merged.PracticeAudioDelaySeconds);

        var outOfRange = SettingsUpdateMerger.MergeEditableFields(latest, latest with { PracticeAudioDelaySeconds = 9 });
        Assert.AreEqual(PracticeAudioDelay.DefaultSeconds, outOfRange.PracticeAudioDelaySeconds);
    }

    [TestMethod]
    public void PracticeAudioDelay_NormalizeKeepsOneToFiveSeconds()
    {
        Assert.AreEqual(1, PracticeAudioDelay.Normalize(1));
        Assert.AreEqual(5, PracticeAudioDelay.Normalize(5));
        Assert.AreEqual(3, PracticeAudioDelay.Normalize(0));
        Assert.AreEqual(3, PracticeAudioDelay.Normalize(6));
        Assert.AreEqual(3, PracticeAudioDelay.Normalize(-2));
    }

    [TestMethod]
    public void MergeEditableFields_RetainsSubmittedPracticeMode()
    {
        var latest = AppSettings.Default;
        var submitted = latest with { PracticeMode = PracticeMode.ListenAndWrite };

        var merged = SettingsUpdateMerger.MergeEditableFields(latest, submitted);

        Assert.AreEqual(PracticeMode.ListenAndWrite, merged.PracticeMode);
    }

    [TestMethod]
    public void DefaultSettings_HasExpectedTtsDefaults()
    {
        var defaultSettings = AppSettings.Default;

        Assert.AreEqual("en-US-JennyNeural", defaultSettings.TtsVoice);
        Assert.AreEqual(1.0, defaultSettings.TtsSpeed);
    }

    [TestMethod]
    public void AppSettings_DoesNotExposeUnusedTtsCacheLimit()
    {
        Assert.IsNull(typeof(AppSettings).GetProperty("TtsMaxCacheSizeBytes"));
    }

    [TestMethod]
    public void MergeEditableFields_PreservesTtsVoiceAndSpeed()
    {
        var original = AppSettings.Default with { TtsVoice = "en-US-JennyNeural", TtsSpeed = 1.0 };
        var submitted = original with { TtsVoice = "en-AU-NatashaNeural", TtsSpeed = 1.1 };

        var merged = SettingsUpdateMerger.MergeEditableFields(original, submitted);

        Assert.AreEqual("en-AU-NatashaNeural", merged.TtsVoice);
        Assert.AreEqual(1.1, merged.TtsSpeed);
    }
}
