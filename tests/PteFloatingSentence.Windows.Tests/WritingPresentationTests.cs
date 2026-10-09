using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public sealed class WritingPresentationTests
{
    [TestMethod]
    public void SplitAnswer_InterleavesPlainAndHighlightedSegments()
    {
        var answer = "I go to company late.";
        var highlights = WritingFeedbackRules.MapHighlights(answer,
        [
            new(WritingIssueType.Vocabulary, "company", "the office", ""),
            new(WritingIssueType.Grammar, "go", "went", "")
        ]);

        var segments = WritingPresentation.SplitAnswer(answer, highlights);

        CollectionAssert.AreEqual(
            new[] { new AnswerSegment("I ", null), new AnswerSegment("go", 1), new AnswerSegment(" to ", null), new AnswerSegment("company", 0), new AnswerSegment(" late.", null) },
            segments.ToArray());
        Assert.AreEqual(answer, string.Concat(segments.Select(s => s.Text)));
    }

    [TestMethod]
    public void SplitAnswer_IgnoresInvalidHighlights()
    {
        var segments = WritingPresentation.SplitAnswer("Hello", [new WritingHighlight(3, 10, 0), new WritingHighlight(-1, 2, 1)]);

        CollectionAssert.AreEqual(new[] { new AnswerSegment("Hello", null) }, segments.ToArray());
        Assert.AreEqual(0, WritingPresentation.SplitAnswer(null, null).Count);
    }

    [TestMethod]
    public void Labels_CoverEveryIssueTypeVerdictAndLevel()
    {
        foreach (var type in Enum.GetValues<WritingIssueType>())
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(WritingPresentation.IssueLabel(type)));
            StringAssert.Matches(WritingPresentation.IssueColor(type), new System.Text.RegularExpressions.Regex("^#[0-9A-F]{8}$"));
        }

        Assert.AreEqual("Word order", WritingPresentation.IssueLabel(WritingIssueType.WordOrder));
        Assert.AreEqual("Almost there", WritingPresentation.VerdictLabel(WritingVerdict.Almost));
        Assert.AreEqual("Hard", WritingPresentation.LevelLabel(WritingLevel.Hard));
    }

    [DataTestMethod]
    [DataRow(0, 3, "Tip 0/3")]
    [DataRow(2, 3, "Tip 2/3")]
    [DataRow(5, 3, "Tip 3/3")]
    [DataRow(0, 0, "No tips")]
    public void TipButtonText_ShowsProgress(int revealed, int available, string expected)
    {
        Assert.AreEqual(expected, WritingPresentation.TipButtonText(revealed, available));
    }

    [TestMethod]
    public void TipsUsedText_Pluralizes()
    {
        Assert.AreEqual(string.Empty, WritingPresentation.TipsUsedText(0));
        Assert.AreEqual("Used 1 tip", WritingPresentation.TipsUsedText(1));
        Assert.AreEqual("Used 3 tips", WritingPresentation.TipsUsedText(3));
    }

    [TestMethod]
    public void IssueText_DescribesReplacementAndMissingMeaning()
    {
        Assert.AreEqual("\"go\" → \"went\"", WritingPresentation.IssueText(new(WritingIssueType.Grammar, "go", "went", "")));
        Assert.AreEqual("(missing) → \"this morning\"", WritingPresentation.IssueText(new(WritingIssueType.MissingMeaning, "", "this morning", "")));
        Assert.AreEqual("\"really\"", WritingPresentation.IssueText(new(WritingIssueType.ExtraMeaning, "really", "", "")));
    }

    [TestMethod]
    public void VocabularyLine_IncludesPartOfSpeechWhenKnown()
    {
        Assert.AreEqual("kẹt xe → traffic jam (noun)", WritingPresentation.VocabularyLine(new("kẹt xe", "traffic jam", "noun")));
        Assert.AreEqual("kẹt xe → traffic jam", WritingPresentation.VocabularyLine(new("kẹt xe", "traffic jam", "")));
    }

    [TestMethod]
    public void EnterMeansNext_OnlyAfterReviewWithUnchangedAnswer()
    {
        Assert.IsTrue(WritingPresentation.EnterMeansNext(WritingSessionState.Reviewed, "I went.", " I went. "));
        Assert.IsFalse(WritingPresentation.EnterMeansNext(WritingSessionState.Reviewed, "I went.", "I go."));
        Assert.IsFalse(WritingPresentation.EnterMeansNext(WritingSessionState.Answering, "I went.", "I went."));
    }

    [TestMethod]
    public void BusyText_OnlyWhileWorking()
    {
        Assert.IsNotNull(WritingPresentation.BusyText(WritingSessionState.Generating));
        Assert.IsNotNull(WritingPresentation.BusyText(WritingSessionState.Checking));
        Assert.IsNull(WritingPresentation.BusyText(WritingSessionState.Answering));
    }
    [TestMethod]
    public void SetupSummary_ListsTopicsLevelAndLanguage()
    {
        Assert.AreEqual(
            "Working in IT, Traffic & transport · Medium · Explain in English",
            WritingPresentation.SetupSummary(["Working in IT", "Traffic & transport"], WritingLevel.Medium, WritingExplanationLanguage.English));
        Assert.AreEqual(
            "Mixed topics · Hard · Explain in Tiếng Việt",
            WritingPresentation.SetupSummary([], WritingLevel.Hard, WritingExplanationLanguage.Vietnamese));
        Assert.AreEqual(
            "A, B, C +2 more · Easy · Explain in English",
            WritingPresentation.SetupSummary(["A", "B", "C", "D", "E"], WritingLevel.Easy, WritingExplanationLanguage.English));
    }
    [TestMethod]
    public void CallText_ShowsProviderModelTimeAndRetries()
    {
        Assert.AreEqual("Groq · openai/gpt-oss-120b · 1.3 s",
            WritingPresentation.CallText(new AiCallInfo("Groq", "openai/gpt-oss-120b", TimeSpan.FromMilliseconds(1260), 1)));
        Assert.AreEqual("Gemini · gemini-3.8-flash · 4.0 s · 2 attempts",
            WritingPresentation.CallText(new AiCallInfo("Gemini", "gemini-3.8-flash", TimeSpan.FromSeconds(4), 2)));
        Assert.AreEqual("Claude · 0.5 s", WritingPresentation.CallText(new AiCallInfo("Claude", "", TimeSpan.FromSeconds(0.5), 1)));
        Assert.AreEqual("Groq · prepared in advance · ready instantly",
            WritingPresentation.CallText(new AiCallInfo("Groq", "m", TimeSpan.FromSeconds(1), 1), prefetched: true));
        Assert.AreEqual(string.Empty, WritingPresentation.CallText(null));
    }
}
