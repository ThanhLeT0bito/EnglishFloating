namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class WritingTipRulesTests
{
    private const string Reference = "There was a traffic jam this morning, so I got to the office late.";

    private static WritingVocabularyHint Hint(string vi, string en) => new(vi, en, "noun");

    [TestMethod]
    public void Sanitize_KeepsAtMostFiveVocabularyItemsAndTrims()
    {
        var tips = new WritingTips(
            Enumerable.Range(1, 7).Select(i => Hint($"  từ   {i} ", $" word  {i}")).ToList(),
            "  Use the   past simple. ",
            "There was");

        var clean = WritingTipRules.Sanitize(tips, Reference);

        Assert.AreEqual(WritingTipRules.MaxVocabularyItems, clean.Vocabulary.Count);
        Assert.AreEqual(new WritingVocabularyHint("từ 1", "word 1", "noun"), clean.Vocabulary[0]);
        Assert.AreEqual("Use the past simple.", clean.Grammar);
        Assert.AreEqual("There was", clean.Starter);
    }

    [TestMethod]
    public void Sanitize_DropsEmptyDuplicateAndAnswerRevealingVocabulary()
    {
        var tips = new WritingTips(
        [
            Hint("kẹt xe", "traffic jam"),
            Hint("", "empty vietnamese"),
            Hint("đến", ""),
            Hint("Kẹt xe", "jam again"),
            Hint("cả câu", "there was a traffic jam this morning so I got to the office late"),
            null!
        ], "", "");

        var clean = WritingTipRules.Sanitize(tips, Reference);

        Assert.AreEqual(1, clean.Vocabulary.Count);
        Assert.AreEqual("traffic jam", clean.Vocabulary[0].English);
    }

    [TestMethod]
    public void Sanitize_CutsStarterToThreeWords()
    {
        var clean = WritingTipRules.Sanitize(new WritingTips([], "", "There was a traffic jam"), Reference);

        Assert.AreEqual("There was a", clean.Starter);
    }

    [TestMethod]
    public void Sanitize_DropsStarterThatRevealsShortReference()
    {
        var clean = WritingTipRules.Sanitize(new WritingTips([], "", "I am late"), "I am late.");

        Assert.AreEqual(string.Empty, clean.Starter);
    }

    [TestMethod]
    public void Sanitize_NullTipsBecomeEmpty()
    {
        var clean = WritingTipRules.Sanitize(null, Reference);

        Assert.AreEqual(0, WritingTipRules.AvailableTiers(clean).Count);
        Assert.AreEqual(0, WritingTipRules.Sanitize(new WritingTips(null!, null!, null!), Reference).Vocabulary.Count);
    }

    [TestMethod]
    public void AvailableTiers_SkipsEmptyTiersInRevealOrder()
    {
        var all = new WritingTips([Hint("kẹt xe", "traffic jam")], "Past simple.", "There was");
        var noGrammar = all with { Grammar = " " };
        var starterOnly = new WritingTips([], "", "There was");

        CollectionAssert.AreEqual(
            new[] { WritingTipTier.Vocabulary, WritingTipTier.Grammar, WritingTipTier.Starter },
            WritingTipRules.AvailableTiers(all).ToArray());
        CollectionAssert.AreEqual(
            new[] { WritingTipTier.Vocabulary, WritingTipTier.Starter },
            WritingTipRules.AvailableTiers(noGrammar).ToArray());
        CollectionAssert.AreEqual(new[] { WritingTipTier.Starter }, WritingTipRules.AvailableTiers(starterOnly).ToArray());
    }
}
