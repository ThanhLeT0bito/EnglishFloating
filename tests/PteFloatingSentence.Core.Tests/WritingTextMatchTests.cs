namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class WritingTextMatchTests
{
    [TestMethod]
    public void Words_IgnoresCaseAndPunctuationAndKeepsApostrophes()
    {
        CollectionAssert.AreEqual(
            new[] { "i've", "been", "to", "ho", "chi", "minh", "city", "twice" },
            WritingTextMatch.Words("  I’ve been to Ho Chi Minh City, twice!  ").ToArray());
        CollectionAssert.AreEqual(new[] { "well-known", "fact" }, WritingTextMatch.Words("a -- well-known fact.").Skip(1).ToArray());
    }

    [TestMethod]
    public void SameWords_TrueWhenOnlyCaseAndPunctuationDiffer()
    {
        Assert.IsTrue(WritingTextMatch.SameWords("there was a traffic jam", "There was a traffic jam."));
        Assert.IsFalse(WritingTextMatch.SameWords("There was traffic jam.", "There was a traffic jam."));
    }

    [TestMethod]
    public void CompareRetype_MatchIgnoresCaseAndPunctuation()
    {
        Assert.IsTrue(WritingTextMatch.CompareRetype("She has lived here since 2019.", "she has lived here since 2019").IsMatch);
    }

    [TestMethod]
    public void CompareRetype_ReportsFirstDifferentWord()
    {
        var result = WritingTextMatch.CompareRetype("She has lived here since 2019.", "She have lived here since 2019.");

        Assert.IsFalse(result.IsMatch);
        Assert.AreEqual(1, result.WordIndex);
        Assert.AreEqual("has", result.Expected);
        Assert.AreEqual("have", result.Typed);
    }

    [TestMethod]
    public void CompareRetype_ReportsMissingAndExtraWordsAtTheEnd()
    {
        var missing = WritingTextMatch.CompareRetype("I like green tea.", "I like green");
        Assert.AreEqual(3, missing.WordIndex);
        Assert.AreEqual("tea", missing.Expected);
        Assert.IsNull(missing.Typed);

        var extra = WritingTextMatch.CompareRetype("I like tea.", "I like tea a lot");
        Assert.AreEqual(3, extra.WordIndex);
        Assert.IsNull(extra.Expected);
        Assert.AreEqual("a", extra.Typed);
    }

    [TestMethod]
    public void ScoreDictation_PerfectAnswerScoresAllWords()
    {
        var result = WritingTextMatch.ScoreDictation("The library opens at nine.", "the library opens at nine");

        Assert.AreEqual(5, result.Correct);
        Assert.AreEqual(5, result.Total);
        Assert.AreEqual(100, result.Score);
        Assert.IsTrue(result.Words.All(word => word.Status == DictationWordStatus.Correct));
    }

    [TestMethod]
    public void ScoreDictation_FindsMisspelledMissingAndExtraWords()
    {
        var result = WritingTextMatch.ScoreDictation(
            "Students must submit their assignments before Friday.",
            "Students must sumbit the their assignments Friday");

        Assert.AreEqual(7, result.Total);
        Assert.AreEqual(5, result.Correct);
        CollectionAssert.AreEqual(
            new[]
            {
                DictationWordStatus.Correct, DictationWordStatus.Correct, DictationWordStatus.Misspelled, DictationWordStatus.Extra,
                DictationWordStatus.Correct, DictationWordStatus.Correct, DictationWordStatus.Missing, DictationWordStatus.Correct
            },
            result.Words.Select(word => word.Status).ToArray());
        Assert.AreEqual("submit", result.Words[2].Expected);
        Assert.AreEqual("sumbit", result.Words[2].Typed);
        Assert.AreEqual("before", result.Words[6].Expected);
        Assert.AreEqual(71, result.Score);
    }

    [TestMethod]
    public void ScoreDictation_DistantWordIsWrongNotMisspelled()
    {
        var result = WritingTextMatch.ScoreDictation("I bought a car.", "I sold a car.");

        Assert.AreEqual(DictationWordStatus.Wrong, result.Words[1].Status);
        Assert.AreEqual(3, result.Correct);
    }

    [TestMethod]
    public void ScoreDictation_EmptyAnswerMissesEverything()
    {
        var result = WritingTextMatch.ScoreDictation("Good morning everyone.", "");

        Assert.AreEqual(0, result.Correct);
        Assert.AreEqual(3, result.Words.Count(word => word.Status == DictationWordStatus.Missing));
        Assert.AreEqual(0, result.Score);
    }

    [TestMethod]
    public void DictationFeedback_TurnsWordsIntoTaggedIssues()
    {
        var reference = "Students must submit their assignments before Friday.";
        var result = WritingTextMatch.ScoreDictation(reference, "Students must sumbit the their assignments Friday");

        var feedback = WritingTextMatch.DictationFeedback(reference, result, WritingExplanationLanguage.English);

        Assert.AreEqual(reference, feedback.CorrectedSentence);
        Assert.AreEqual(71, feedback.Score);
        Assert.AreEqual(WritingVerdict.Almost, feedback.Verdict);
        Assert.AreEqual("5 of 7 words correct.", feedback.Summary);
        Assert.AreEqual(3, feedback.Issues.Count);
        Assert.AreEqual(WritingSkillTag.Spelling, feedback.Issues[0].Tag);
        Assert.AreEqual("sumbit", feedback.Issues[0].Original);
        Assert.AreEqual(WritingSkillTag.ExtraWord, feedback.Issues[1].Tag);
        Assert.AreEqual(WritingSkillTag.MissingWord, feedback.Issues[2].Tag);
        Assert.AreEqual("", feedback.Issues[2].Original);
        Assert.AreEqual("before", feedback.Issues[2].Suggestion);
    }

    [TestMethod]
    public void DictationFeedback_UsesVietnameseExplanations()
    {
        var result = WritingTextMatch.ScoreDictation("Good morning.", "Good");
        var feedback = WritingTextMatch.DictationFeedback("Good morning.", result, WritingExplanationLanguage.Vietnamese);

        Assert.AreEqual("Đúng 1/2 từ.", feedback.Summary);
        Assert.AreEqual("Thiếu từ này.", feedback.Issues[0].Explanation);
    }
}
