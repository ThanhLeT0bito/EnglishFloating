namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class SettingsUpdateMergerTests
{
    [TestMethod]
    public void DefaultSettings_HasExpectedTtsDefaults()
    {
        var defaultSettings = AppSettings.Default;

        Assert.AreEqual("en-US-JennyNeural", defaultSettings.TtsVoice);
        Assert.AreEqual(1.0, defaultSettings.TtsSpeed);
        Assert.AreEqual(20 * 1024 * 1024, defaultSettings.TtsMaxCacheSizeBytes);
    }

    [TestMethod]
    public void MergeEditableFields_PreservesTtsVoiceAndSpeed()
    {
        var original = AppSettings.Default with { TtsVoice = "en-US-JennyNeural", TtsSpeed = 1.0, TtsMaxCacheSizeBytes = 20 * 1024 * 1024 };
        var submitted = original with { TtsVoice = "en-AU-NatashaNeural", TtsSpeed = 1.1, TtsMaxCacheSizeBytes = 50 * 1024 * 1024 };

        var merged = SettingsUpdateMerger.MergeEditableFields(original, submitted);

        Assert.AreEqual("en-AU-NatashaNeural", merged.TtsVoice);
        Assert.AreEqual(1.1, merged.TtsSpeed);
        Assert.AreEqual(50 * 1024 * 1024, merged.TtsMaxCacheSizeBytes);
    }
}
