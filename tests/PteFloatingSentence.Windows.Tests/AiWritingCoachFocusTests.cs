using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public sealed class AiWritingCoachFocusTests
{
    private const string Exercise = """
        {
          "vietnamese": "Cô ấy đã sống ở đây từ năm 2019.",
          "english": "She has lived here since 2019.",
          "topic": "Daily life",
          "tips": { "vocabulary": [], "grammar": "Present perfect.", "starter": "She" }
        }
        """;

    private const string CheckResult = """
        {
          "score": 70,
          "correctedSentence": "She has lived here since 2019.",
          "naturalAlternative": "",
          "summary": "Close!",
          "issues": [
            { "type": "grammar", "tag": "tense", "original": "lives", "suggestion": "has lived", "explanation": "Present perfect with since." },
            { "type": "grammar", "tag": "unknown-thing", "original": "on", "suggestion": "in", "explanation": "x" },
            { "type": "spelling", "original": "heer", "suggestion": "here", "explanation": "y" }
          ]
        }
        """;

    private static (AiWritingCoach Coach, GeminiTestHandler Handler) CoachReturning(string text)
    {
        var handler = GeminiTestHandler.Returning(text);
        var client = new GeminiJsonClient(() => "test-key", new HttpClient(handler), ["model-a"]);
        return (new AiWritingCoach(client), handler);
    }

    [TestMethod]
    public async Task Generate_WithFocusTags_AsksForThoseStructuresAndKeepsThemOnThePrompt()
    {
        var (coach, handler) = CoachReturning(Exercise);

        var prompt = await coach.GenerateAsync(new WritingRequest(["Daily life"], WritingLevel.Medium, WritingExplanationLanguage.English, [])
        {
            FocusTags = [WritingSkillTag.Tense, WritingSkillTag.Spelling, WritingSkillTag.Article]
        });

        var system = handler.SystemInstruction(0);
        StringAssert.Contains(system, "practicing weak points");
        StringAssert.Contains(system, "present perfect vs past simple");
        StringAssert.Contains(system, "articles a/an/the");
        CollectionAssert.AreEqual(new[] { WritingSkillTag.Tense, WritingSkillTag.Article }, prompt.FocusTags.ToArray());
    }

    [TestMethod]
    public async Task Generate_WithoutFocus_HasNoWeakPointRule()
    {
        var (coach, handler) = CoachReturning(Exercise);

        var prompt = await coach.GenerateAsync(new WritingRequest(["Daily life"], WritingLevel.Medium, WritingExplanationLanguage.English, []));

        Assert.IsFalse(handler.SystemInstruction(0).Contains("weak points", StringComparison.Ordinal));
        Assert.AreEqual(0, prompt.FocusTags.Count);
    }

    [TestMethod]
    public async Task Check_ParsesIssueTagsWithFallbacks()
    {
        var (coach, handler) = CoachReturning(CheckResult);
        var prompt = new WritingPrompt("Cô ấy đã sống ở đây từ năm 2019.", "She has lived here since 2019.", "Daily life", WritingLevel.Medium, WritingTips.Empty);

        var feedback = await coach.CheckAsync(prompt, "She lives heer on 2019.", WritingExplanationLanguage.English);

        Assert.AreEqual(WritingSkillTag.Tense, feedback.Issues[0].Tag);
        Assert.AreEqual(WritingSkillTag.Other, feedback.Issues[1].Tag);
        Assert.AreEqual(WritingSkillTag.Spelling, feedback.Issues[2].Tag);
        StringAssert.Contains(handler.SystemInstruction(0), "subject_verb_agreement");
    }
}
