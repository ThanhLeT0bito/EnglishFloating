namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class WritingModesTests
{
    private static readonly WritingPrompt Translate = new("Tôi đi làm bằng xe buýt.", "I go to work by bus.", "Daily life", WritingLevel.Easy, WritingTips.Empty);

    private static readonly WritingPrompt Paraphrase = new("Cuộc họp bị hoãn vì mưa.", "Because of the rain, the meeting was put off.", "Work", WritingLevel.Medium, WritingTips.Empty)
    {
        Mode = WritingPracticeMode.Paraphrase,
        SourceEnglish = "The meeting was postponed because it rained."
    };

    private static readonly WritingPrompt Combine = new("Mặc dù trời mưa, chúng tôi vẫn đi bộ.", "Although it was raining, we went for a walk.", "Weather", WritingLevel.Medium, WritingTips.Empty)
    {
        Mode = WritingPracticeMode.Combine,
        SourceEnglish = "It was raining.\nWe went for a walk.",
        Instruction = "although"
    };

    private static readonly WritingPrompt Reverse = new("Cô ấy đã sống ở đây từ năm 2019.", "She has lived here since 2019.", "Housing", WritingLevel.Medium, WritingTips.Empty)
    {
        Mode = WritingPracticeMode.Reverse
    };

    private static readonly WritingFeedback Feedback = new(80, WritingVerdict.Almost, "Corrected answer.", null, "Ok.", []);

    [TestMethod]
    public void GenerationMode_DictationUsesTranslatePrompts()
    {
        Assert.AreEqual(WritingPracticeMode.Translate, WritingModes.GenerationMode(WritingPracticeMode.Dictation));
        Assert.AreEqual(WritingPracticeMode.Paraphrase, WritingModes.GenerationMode(WritingPracticeMode.Paraphrase));
        Assert.AreEqual(5, WritingModes.All.Count);
    }

    [TestMethod]
    public void Fits_OnlySameKindOfPrompt()
    {
        Assert.IsTrue(WritingModes.Fits(WritingPracticeMode.Translate, WritingPracticeMode.Dictation));
        Assert.IsTrue(WritingModes.Fits(WritingPracticeMode.Combine, WritingPracticeMode.Combine));
        Assert.IsFalse(WritingModes.Fits(WritingPracticeMode.Translate, WritingPracticeMode.Paraphrase));
        Assert.IsFalse(WritingModes.Fits(WritingPracticeMode.Translate, WritingPracticeMode.Reverse));
    }

    [TestMethod]
    public void Effective_KeepsThePromptModeWhenTheChosenOneDoesNotFit()
    {
        Assert.AreEqual(WritingPracticeMode.Dictation, WritingModes.Effective(Translate, WritingPracticeMode.Dictation));
        Assert.AreEqual(WritingPracticeMode.Translate, WritingModes.Effective(Translate, WritingPracticeMode.Combine));
        Assert.AreEqual(WritingPracticeMode.Paraphrase, WritingModes.Effective(Paraphrase, WritingPracticeMode.Translate));
    }

    [TestMethod]
    public void PromptText_ShowsWhatEachModeWorksFrom()
    {
        Assert.AreEqual(Translate.Vietnamese, WritingModes.PromptText(Translate, WritingPracticeMode.Translate));
        Assert.AreEqual(Paraphrase.SourceEnglish, WritingModes.PromptText(Paraphrase, WritingPracticeMode.Paraphrase));
        Assert.AreEqual("It was raining.\nWe went for a walk.", WritingModes.PromptText(Combine, WritingPracticeMode.Combine));
        Assert.AreEqual(Reverse.ReferenceEnglish, WritingModes.PromptText(Reverse, WritingPracticeMode.Reverse));
    }

    [TestMethod]
    public void ListenText_NeverReadsTheTranslateAnswer()
    {
        Assert.AreEqual("", WritingModes.ListenText(Translate, WritingPracticeMode.Translate));
        Assert.AreEqual(Translate.ReferenceEnglish, WritingModes.ListenText(Translate, WritingPracticeMode.Dictation));
        Assert.AreEqual("It was raining. We went for a walk.", WritingModes.ListenText(Combine, WritingPracticeMode.Combine));
        Assert.AreEqual(Reverse.ReferenceEnglish, WritingModes.ListenText(Reverse, WritingPracticeMode.Reverse));
    }

    [TestMethod]
    public void ExpectedAnswerMeaningAndHistorySource()
    {
        Assert.AreEqual(Reverse.Vietnamese, WritingModes.ExpectedAnswer(Reverse, WritingPracticeMode.Reverse));
        Assert.AreEqual(Combine.ReferenceEnglish, WritingModes.ExpectedAnswer(Combine, WritingPracticeMode.Combine));
        Assert.AreEqual(Paraphrase.Vietnamese, WritingModes.MeaningText(Paraphrase, WritingPracticeMode.Paraphrase));
        Assert.AreEqual("", WritingModes.MeaningText(Translate, WritingPracticeMode.Translate));
        Assert.AreEqual(Paraphrase.SourceEnglish, WritingModes.HistorySource(Paraphrase, WritingPracticeMode.Paraphrase));
        Assert.AreEqual(Reverse.ReferenceEnglish, WritingModes.HistorySource(Reverse, WritingPracticeMode.Reverse));
        Assert.AreEqual(Translate.Vietnamese, WritingModes.HistorySource(Translate, WritingPracticeMode.Translate));
    }

    [TestMethod]
    public void EnglishResult_IsTheEnglishSourceInReverseMode()
    {
        Assert.AreEqual(Reverse.ReferenceEnglish, WritingModes.EnglishResult(Reverse, Feedback, WritingPracticeMode.Reverse));
        Assert.AreEqual("Corrected answer.", WritingModes.EnglishResult(Paraphrase, Feedback, WritingPracticeMode.Paraphrase));
        Assert.IsTrue(WritingModes.AnswersInVietnamese(WritingPracticeMode.Reverse));
        Assert.IsFalse(WritingModes.AnswersInVietnamese(WritingPracticeMode.Combine));
    }

    [TestMethod]
    public void IsCopiedParaphrase_IgnoresCaseAndPunctuation()
    {
        Assert.IsTrue(WritingModes.IsCopiedParaphrase(Paraphrase, "the meeting was postponed because it rained"));
        Assert.IsFalse(WritingModes.IsCopiedParaphrase(Paraphrase, "The meeting was delayed due to the rain."));
        Assert.IsFalse(WritingModes.IsCopiedParaphrase(Translate, Translate.Vietnamese));
    }

    [TestMethod]
    public void SentenceStructureTag_IsParsedAndTargetable()
    {
        Assert.AreEqual(WritingSkillTag.SentenceStructure, WritingSkillTags.Parse("sentence_structure", WritingIssueType.Grammar));
        Assert.AreEqual(WritingSkillTag.SentenceStructure, WritingSkillTags.Parse("connector", WritingIssueType.Grammar));
        Assert.IsTrue(WritingSkillTags.IsTargetable(WritingSkillTag.SentenceStructure));
        Assert.AreEqual("Sentence structure", WritingSkillTags.Label(WritingSkillTag.SentenceStructure));
        StringAssert.Contains(WritingSkillTags.PromptValues, "sentence_structure");
    }

    [TestMethod]
    public void Normalize_KeepsNewModes()
    {
        var settings = WritingTopicRules.Normalize(AppSettings.Default with { WritingPracticeMode = WritingPracticeMode.Combine });
        Assert.AreEqual(WritingPracticeMode.Combine, settings.WritingPracticeMode);
    }
}
