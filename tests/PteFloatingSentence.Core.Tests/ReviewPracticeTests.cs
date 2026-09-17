using PteFloatingSentence.Core;

namespace PteFloatingSentence.Core.Tests;

[TestClass]
public class ReviewPracticeTests
{
    [TestMethod]
    public void CreateProjection_PreservesOriginalTextAndTokenCount()
    {
        var id = Guid.NewGuid();
        var sentence = new StudySentence(id, "You must wear a hard hat on the construction site");
        var review = ReviewPracticeRules.CreateProjection(sentence, seed: 7);

        Assert.AreEqual(id, review.SentenceId);
        Assert.AreEqual(sentence.Text, review.OriginalText);
        Assert.AreEqual(10, review.Tokens.Count);
        Assert.IsTrue(review.HiddenTokenIndexes.Count >= 2);
    }

    [TestMethod]
    public void CreateProjection_PreservesPunctuationOnTokens()
    {
        var id = Guid.NewGuid();
        var sentence = new StudySentence(id, "Really? Yes, of course!");
        var review = ReviewPracticeRules.CreateProjection(sentence, seed: 42);

        Assert.AreEqual("Really?", review.Tokens[0].SourceText);
        Assert.AreEqual("Yes,", review.Tokens[1].SourceText);
        Assert.AreEqual("of", review.Tokens[2].SourceText);
        Assert.AreEqual("course!", review.Tokens[3].SourceText);
    }

    [TestMethod]
    public void CreateProjection_HiddenTokensHaveSingleUnderscoreDisplayText()
    {
        var id = Guid.NewGuid();
        var sentence = new StudySentence(id, "Punctuation test, indeed.");
        var review = ReviewPracticeRules.CreateProjection(sentence, seed: 1);

        foreach (var token in review.Tokens)
        {
            if (token.IsHidden)
            {
                Assert.AreEqual("_", token.DisplayText);
            }
            else
            {
                Assert.AreEqual(token.SourceText, token.DisplayText);
            }
        }
    }

    [TestMethod]
    public void CreateProjection_DisplayTextJoinsTokensWithSpaces()
    {
        var id = Guid.NewGuid();
        var sentence = new StudySentence(id, "You must wear a hard hat on the construction site");
        var review = ReviewPracticeRules.CreateProjection(sentence, seed: 7);

        var expectedJoined = string.Join(" ", review.Tokens.Select(t => t.DisplayText));
        Assert.AreEqual(expectedJoined, review.DisplayText);
    }

    [TestMethod]
    public void CreateProjection_OneWordSentence_HidesZeroWords()
    {
        var sentence = new StudySentence(Guid.NewGuid(), "Hello.");
        var review = ReviewPracticeRules.CreateProjection(sentence);

        Assert.AreEqual(0, review.HiddenTokenIndexes.Count);
        Assert.AreEqual("Hello.", review.DisplayText);
    }

    [TestMethod]
    public void CreateProjection_ThreeWordSentence_HidesOneWordNotLast()
    {
        var sentence = new StudySentence(Guid.NewGuid(), "One two three.");
        var review = ReviewPracticeRules.CreateProjection(sentence, seed: 123);

        Assert.AreEqual(1, review.HiddenTokenIndexes.Count);
        Assert.AreNotEqual(2, review.HiddenTokenIndexes[0]); // Last word left visible
        Assert.IsFalse(review.Tokens[2].IsHidden);
    }

    [TestMethod]
    public void CreateProjection_SixWordSentence_HidesHalfAndLeavesLastWordVisible()
    {
        var sentence = new StudySentence(Guid.NewGuid(), "One two three four five six.");
        var review = ReviewPracticeRules.CreateProjection(sentence, seed: 456);

        Assert.AreEqual(3, review.HiddenTokenIndexes.Count);
        Assert.IsFalse(review.HiddenTokenIndexes.Contains(5));
        Assert.IsFalse(review.Tokens[5].IsHidden);
    }

    [TestMethod]
    public void CreateProjection_TenWordSentence_HidesFortyToFiftyPercentAndLeavesLastWordVisible()
    {
        var sentence = new StudySentence(Guid.NewGuid(), "One two three four five six seven eight nine ten.");
        var review = ReviewPracticeRules.CreateProjection(sentence, seed: 789);

        Assert.IsTrue(review.HiddenTokenIndexes.Count is >= 4 and <= 5);
        Assert.IsFalse(review.HiddenTokenIndexes.Contains(9));
        Assert.IsFalse(review.Tokens[9].IsHidden);
    }

    [TestMethod]
    public void CreateProjection_StableSentenceId_ProducesSameHiddenIndexes()
    {
        var id = Guid.NewGuid();
        var sentence = new StudySentence(id, "The quick brown fox jumps over the lazy dog today");

        var review1 = ReviewPracticeRules.CreateProjection(sentence);
        var review2 = ReviewPracticeRules.CreateProjection(sentence);

        CollectionAssert.AreEqual(review1.HiddenTokenIndexes.ToArray(), review2.HiddenTokenIndexes.ToArray());
    }

    [TestMethod]
    public void CreateProjection_DoesNotHidePunctuationOnlyTokens()
    {
        var sentence = new StudySentence(Guid.NewGuid(), "Alpha , beta -- gamma .");
        var review = ReviewPracticeRules.CreateProjection(sentence, seed: 99);

        foreach (var hiddenIdx in review.HiddenTokenIndexes)
        {
            var token = review.Tokens[hiddenIdx];
            Assert.IsTrue(token.SourceText.Any(char.IsLetterOrDigit), $"Token '{token.SourceText}' was punctuation-only but hidden.");
        }
    }
}
