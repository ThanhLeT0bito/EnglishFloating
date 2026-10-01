using PteFloatingSentence.Core;

namespace PteFloatingSentence.Core.Tests;

[TestClass]
public class ReviewPracticeTests
{
    [DataTestMethod]
    [DataRow("Don't stop at the station.", "don't   stop at the station", true)]
    [DataRow("Wait for me!", "  WAIT for me...  ", true)]
    [DataRow("“Don't stop,” she said.", "don't stop she said", true)]
    [DataRow("Don't stop.", "Dont stop", false)]
    [DataRow("You must wear a hard hat.", "You must wear hard hat", false)]
    [DataRow("You must wear a hard hat.", "You must wear a hard hat today", false)]
    [DataRow("You must wear a hard hat.", "You wear must a hard hat", false)]
    [DataRow("You must wear a hard hat.", "You must wear a hard cat", false)]
    [DataRow("You must wear a hard hat.", "", false)]
    [DataRow("You must wear a hard hat.", "   ", false)]
    [DataRow("", "", false)]
    public void IsCorrectDictation_GradesCompleteNormalizedSentence(string expected, string answer, bool isCorrect)
    {
        Assert.AreEqual(isCorrect, ReviewPracticeRules.IsCorrectDictation(expected, answer));
    }

    [TestMethod]
    public void IsCorrectDictation_NullAnswer_IsIncorrect()
    {
        Assert.IsFalse(ReviewPracticeRules.IsCorrectDictation("Listen carefully.", null));
    }

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

    [TestMethod]
    public void CheckAnswer_ExactAndCaseInsensitiveAndWhitespace_Succeeds()
    {
        var sentence = new StudySentence(Guid.NewGuid(), "Alpha beta gamma.");
        var review = ReviewPracticeRules.CreateProjection(sentence, seed: 10);
        // Ensure there is at least one hidden token
        Assert.IsTrue(review.HiddenTokenIndexes.Count > 0);

        var firstHiddenToken = review.Tokens[review.HiddenTokenIndexes[0]];
        var rawWord = firstHiddenToken.SourceText.Trim('.', ',');

        // Case insensitive with whitespace
        var result = ReviewPracticeRules.CheckAnswer(review, 0, "  " + rawWord.ToUpperInvariant() + "  ");
        Assert.IsTrue(result.IsCorrect);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void CheckAnswer_WrongAnswer_ReturnsTryAgainAndKeepsPosition()
    {
        var sentence = new StudySentence(Guid.NewGuid(), "Alpha beta gamma.");
        var review = ReviewPracticeRules.CreateProjection(sentence, seed: 10);
        Assert.IsTrue(review.HiddenTokenIndexes.Count > 0);

        var result = ReviewPracticeRules.CheckAnswer(review, 0, "definitely_wrong_word");
        Assert.IsFalse(result.IsCorrect);
        Assert.IsFalse(result.IsComplete);
        Assert.AreEqual(0, result.NextHiddenTokenPosition);
        Assert.AreEqual("Try again.", result.Error);
    }

    [TestMethod]
    public void CheckAnswer_ProgressionAndFinalCompletion()
    {
        var sentence = new StudySentence(Guid.NewGuid(), "One two three four five six.");
        var review = ReviewPracticeRules.CreateProjection(sentence, seed: 42);
        Assert.AreEqual(3, review.HiddenTokenIndexes.Count);

        // First hidden token
        var word0 = review.Tokens[review.HiddenTokenIndexes[0]].SourceText.Trim('.', ',');
        var res0 = ReviewPracticeRules.CheckAnswer(review, 0, word0);
        Assert.IsTrue(res0.IsCorrect);
        Assert.IsFalse(res0.IsComplete);
        Assert.AreEqual(1, res0.NextHiddenTokenPosition);

        // Second hidden token
        var word1 = review.Tokens[review.HiddenTokenIndexes[1]].SourceText.Trim('.', ',');
        var res1 = ReviewPracticeRules.CheckAnswer(review, 1, word1);
        Assert.IsTrue(res1.IsCorrect);
        Assert.IsFalse(res1.IsComplete);
        Assert.AreEqual(2, res1.NextHiddenTokenPosition);

        // Third and final hidden token
        var word2 = review.Tokens[review.HiddenTokenIndexes[2]].SourceText.Trim('.', ',');
        var res2 = ReviewPracticeRules.CheckAnswer(review, 2, word2);
        Assert.IsTrue(res2.IsCorrect);
        Assert.IsTrue(res2.IsComplete);
    }

    [TestMethod]
    public void CreateProjection_HandlesMultipleSpacesRepeatedWordsApostrophesAndHyphens()
    {
        var sentence = new StudySentence(Guid.NewGuid(), "Don't   forget   state-of-the-art   tools,   don't   they?");
        var review = ReviewPracticeRules.CreateProjection(sentence, seed: 100);

        Assert.AreEqual(6, review.Tokens.Count);
        Assert.AreEqual("Don't", review.Tokens[0].SourceText);
        Assert.AreEqual("forget", review.Tokens[1].SourceText);
        Assert.AreEqual("state-of-the-art", review.Tokens[2].SourceText);
        Assert.AreEqual("tools,", review.Tokens[3].SourceText);
        Assert.AreEqual("don't", review.Tokens[4].SourceText);
        Assert.AreEqual("they?", review.Tokens[5].SourceText);

        // Check answer for don't with punctuation
        var sentenceApos = new StudySentence(Guid.NewGuid(), "Don't forget!");
        var reviewApos = ReviewPracticeRules.CreateProjection(sentenceApos, seed: 1);
        Assert.AreEqual(1, reviewApos.HiddenTokenIndexes.Count);
        Assert.AreEqual(0, reviewApos.HiddenTokenIndexes[0]);

        var checkApos = ReviewPracticeRules.CheckAnswer(reviewApos, 0, "don't");
        Assert.IsTrue(checkApos.IsCorrect);
    }
}
