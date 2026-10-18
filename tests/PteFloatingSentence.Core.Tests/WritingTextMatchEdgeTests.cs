namespace PteFloatingSentence.Core.Tests;

/// <summary>Dictation and formatting checks: apostrophes, quotes and numbers with separators.</summary>
[TestClass]
public sealed class WritingTextMatchEdgeTests
{
    [TestMethod]
    public void Words_KeepsAPluralPossessiveButNotAClosingQuote()
    {
        CollectionAssert.AreEqual(new[] { "the", "students'", "books" }, WritingTextMatch.Words("The students’ books").ToArray());
        CollectionAssert.AreEqual(new[] { "she", "said", "hello" }, WritingTextMatch.Words("She said 'hello'.").ToArray());
        CollectionAssert.AreEqual(new[] { "a", "crisis" }, WritingTextMatch.Words("A ‘crisis’").ToArray());
        CollectionAssert.AreEqual(new[] { "he", "said", "yes" }, WritingTextMatch.Words("He said 'yes'").ToArray());
        CollectionAssert.AreEqual(new[] { "don't", "go" }, WritingTextMatch.Words("Don't go!").ToArray());
    }

    [TestMethod]
    public void PossessiveFix_IsAMistakeNotFormatting()
    {
        var issue = new WritingIssue(WritingIssueType.Grammar, "students", "students'", "Plural possessive.");

        Assert.IsFalse(WritingFeedbackRules.IsFormattingOnly(issue));
        Assert.IsTrue(WritingFeedbackRules.IsFormattingOnly(new WritingIssue(WritingIssueType.Grammar, "i", "I", "Capital.")));
    }

    [TestMethod]
    public void Dictation_DoesNotPenalizeAPossessiveApostropheThatCannotBeHeard()
    {
        Assert.AreEqual(100, WritingTextMatch.ScoreDictation("The students' books are new.", "the students books are new").Score);
        Assert.AreEqual(100, WritingTextMatch.ScoreDictation("He called it a ‘crisis’.", "He called it a crisis").Score);
    }

    [TestMethod]
    public void Numbers_SeparatorsKeepTheirMeaning()
    {
        CollectionAssert.AreEqual(new[] { "it", "costs", "1000", "dollars" }, WritingTextMatch.Words("It costs 1,000 dollars.").ToArray());
        CollectionAssert.AreEqual(new[] { "at", "10.30" }, WritingTextMatch.Words("At 10:30.").ToArray());
        CollectionAssert.AreEqual(new[] { "one", "two" }, WritingTextMatch.Words("one, two").ToArray(), "a comma between words still splits");
        CollectionAssert.AreEqual(new[] { "1", "5" }, WritingTextMatch.Words("1,5").ToArray(), "only a thousands comma joins");

        Assert.AreEqual(100, WritingTextMatch.ScoreDictation("The meeting costs 1,000 dollars.", "the meeting costs 1000 dollars").Score);
        Assert.AreEqual(100, WritingTextMatch.ScoreDictation("It starts at 10:30.", "It starts at 10.30").Score);
        Assert.IsTrue(WritingTextMatch.ScoreDictation("It rose 3.5 percent.", "It rose 35 percent").Score < 100, "3.5 is not 35");
        Assert.IsFalse(WritingFeedbackRules.IsFormattingOnly(new WritingIssue(WritingIssueType.Grammar, "25%", "2.5%", "Number.")));
    }
}
