namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class AiProviderRulesTests
{
    [TestMethod]
    public void Defaults_UseGeminiWithNoOtherKeys()
    {
        Assert.AreEqual(AiProvider.Gemini, AppSettings.Default.AiProvider);
        Assert.IsFalse(AppSettings.Default.GroqApiKeyConfigured);
        Assert.IsFalse(AppSettings.Default.ClaudeApiKeyConfigured);
    }

    [TestMethod]
    public void Normalize_MapsUnknownValuesToGemini()
    {
        Assert.AreEqual(AiProvider.Groq, AiProviderRules.Normalize(AiProvider.Groq));
        Assert.AreEqual(AiProvider.Gemini, AiProviderRules.Normalize((AiProvider)42));
    }

    [TestMethod]
    public void KeyConfiguredFlags_ArePerProvider()
    {
        var settings = AiProviderRules.WithKeyConfigured(AppSettings.Default, AiProvider.Groq, true);
        settings = AiProviderRules.WithKeyConfigured(settings, AiProvider.Claude, true);
        settings = AiProviderRules.WithKeyConfigured(settings, AiProvider.Claude, false);

        Assert.IsTrue(AiProviderRules.IsKeyConfigured(settings, AiProvider.Groq));
        Assert.IsFalse(AiProviderRules.IsKeyConfigured(settings, AiProvider.Claude));
        Assert.IsFalse(AiProviderRules.IsKeyConfigured(settings, AiProvider.Gemini));
    }

    [TestMethod]
    public void Merger_KeepsSubmittedProviderAndFlags()
    {
        var submitted = AppSettings.Default with { AiProvider = AiProvider.Claude, ClaudeApiKeyConfigured = true, GroqApiKeyConfigured = true };

        var merged = SettingsUpdateMerger.MergeEditableFields(AppSettings.Default, submitted);

        Assert.AreEqual(AiProvider.Claude, merged.AiProvider);
        Assert.IsTrue(merged.ClaudeApiKeyConfigured);
        Assert.IsTrue(merged.GroqApiKeyConfigured);
        Assert.AreEqual(AiProvider.Gemini, SettingsUpdateMerger.MergeEditableFields(AppSettings.Default, AppSettings.Default with { AiProvider = (AiProvider)9 }).AiProvider);
    }
}
