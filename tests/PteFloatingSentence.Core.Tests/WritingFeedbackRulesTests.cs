namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class WritingFeedbackRulesTests
{
    private static WritingIssue Issue(string original, WritingIssueType type = WritingIssueType.Grammar) =>
        new(type, original, "fix", "why");

    [TestMethod]
    public void MapHighlights_FindsTextIgnoringCase()
    {
        var highlights = WritingFeedbackRules.MapHighlights("This morning the road WAS traffic jam.", [Issue("was traffic jam")]);

        Assert.AreEqual(1, highlights.Count);
        Assert.AreEqual(new WritingHighlight(22, 15, 0), highlights[0]);
    }

    [TestMethod]
    public void MapHighlights_RepeatedWordGoesToNextUnusedOccurrence()
    {
        var answer = "I go to go home.";
        var highlights = WritingFeedbackRules.MapHighlights(answer, [Issue("go"), Issue("go")]);

        Assert.AreEqual(2, highlights.Count);
        Assert.AreEqual(2, highlights[0].Start);
        Assert.AreEqual(0, highlights[0].IssueIndex);
        Assert.AreEqual(8, highlights[1].Start);
        Assert.AreEqual(1, highlights[1].IssueIndex);
    }

    [TestMethod]
    public void MapHighlights_DoesNotMatchInsideLongerWord()
    {
        var highlights = WritingFeedbackRules.MapHighlights("I have an apple.", [Issue("a")]);

        Assert.AreEqual(0, highlights.Count);
    }

    [TestMethod]
    public void MapHighlights_OverlappingIssueIsNotHighlighted()
    {
        var highlights = WritingFeedbackRules.MapHighlights("came to company late", [Issue("came to company"), Issue("to company late")]);

        Assert.AreEqual(1, highlights.Count);
        Assert.AreEqual(0, highlights[0].IssueIndex);
    }

    [TestMethod]
    public void MapHighlights_SkipsMissingMeaningAndUnknownText()
    {
        var highlights = WritingFeedbackRules.MapHighlights(
            "I came late.",
            [Issue("", WritingIssueType.MissingMeaning), Issue("not there"), Issue("late")]);

        Assert.AreEqual(1, highlights.Count);
        Assert.AreEqual(new WritingHighlight(7, 4, 2), highlights[0]);
    }

    [TestMethod]
    public void MapHighlights_MatchesPunctuationIssueAndReturnsSortedSpans()
    {
        var highlights = WritingFeedbackRules.MapHighlights(
            "i came late ,really.",
            [Issue("late"), Issue(" ,", WritingIssueType.Punctuation), Issue("i", WritingIssueType.Punctuation)]);

        CollectionAssert.AreEqual(new[] { 0, 7, 12 }, highlights.Select(h => h.Start).ToArray());
        CollectionAssert.AreEqual(new[] { 2, 0, 1 }, highlights.Select(h => h.IssueIndex).ToArray());
    }

    [TestMethod]
    public void MapHighlights_EmptyAnswerOrNullIssues_ReturnsNothing()
    {
        Assert.AreEqual(0, WritingFeedbackRules.MapHighlights("", [Issue("x")]).Count);
        Assert.AreEqual(0, WritingFeedbackRules.MapHighlights("text", null).Count);
    }

    [DataTestMethod]
    [DataRow(-5, 0)]
    [DataRow(55, 55)]
    [DataRow(140, 100)]
    public void ClampScore_KeepsZeroToHundred(int score, int expected)
    {
        Assert.AreEqual(expected, WritingFeedbackRules.ClampScore(score));
    }

    [DataTestMethod]
    [DataRow(100, WritingVerdict.Correct)]
    [DataRow(90, WritingVerdict.Correct)]
    [DataRow(89, WritingVerdict.Almost)]
    [DataRow(60, WritingVerdict.Almost)]
    [DataRow(59, WritingVerdict.NeedsWork)]
    public void VerdictFor_UsesThresholds(int score, WritingVerdict expected)
    {
        Assert.AreEqual(expected, WritingFeedbackRules.VerdictFor(score));
    }

    [DataTestMethod]
    [DataRow("grammar", WritingIssueType.Grammar)]
    [DataRow("word_order", WritingIssueType.WordOrder)]
    [DataRow("Word order", WritingIssueType.WordOrder)]
    [DataRow("missing_meaning", WritingIssueType.MissingMeaning)]
    [DataRow("extra_meaning", WritingIssueType.ExtraMeaning)]
    [DataRow("SPELLING", WritingIssueType.Spelling)]
    [DataRow("vocabulary", WritingIssueType.Vocabulary)]
    [DataRow("capitalization", WritingIssueType.Punctuation)]
    [DataRow("something new", WritingIssueType.Grammar)]
    [DataRow(null, WritingIssueType.Grammar)]
    public void ParseIssueType_MapsKnownNamesAndFallsBackToGrammar(string? value, WritingIssueType expected)
    {
        Assert.AreEqual(expected, WritingFeedbackRules.ParseIssueType(value));
    }

    [TestMethod]
    public void Normalize_PunctuationOnlyIssuesDoNotLowerScore()
    {
        var feedback = new WritingFeedback(70, WritingVerdict.NeedsWork, "I came late.", null, "", [Issue("i", WritingIssueType.Punctuation)]);

        var normalized = WritingFeedbackRules.Normalize(feedback);

        Assert.AreEqual(100, normalized.Score);
        Assert.AreEqual(WritingVerdict.Correct, normalized.Verdict);
    }

    [TestMethod]
    public void Normalize_ClampsAndDerivesVerdictFromScore()
    {
        var feedback = new WritingFeedback(65, WritingVerdict.Correct, "x", null, "", [Issue("a"), Issue(",", WritingIssueType.Punctuation)]);

        var normalized = WritingFeedbackRules.Normalize(feedback);

        Assert.AreEqual(65, normalized.Score);
        Assert.AreEqual(WritingVerdict.Almost, normalized.Verdict);
        Assert.AreEqual(WritingVerdict.NeedsWork, WritingFeedbackRules.Normalize(feedback with { Score = -3 }).Verdict);
    }
}
