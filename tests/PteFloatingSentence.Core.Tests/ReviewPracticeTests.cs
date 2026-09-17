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
}
