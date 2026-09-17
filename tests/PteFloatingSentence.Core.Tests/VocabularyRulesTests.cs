using PteFloatingSentence.Core;

namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class VocabularyRulesTests
{
    [TestMethod]
    public void NormalizePhrase_TrimsAndCollapsesWhitespace()
    {
        Assert.AreEqual("apple", VocabularyRules.NormalizePhrase("  apple  "));
        Assert.AreEqual("take into account", VocabularyRules.NormalizePhrase(" take \t into   \r\n account "));
    }

    [TestMethod]
    public void ValidatePhrase_EmptyOrWhitespace_ReturnsInvalid()
    {
        var empty = VocabularyRules.ValidatePhrase("");
        var whitespace = VocabularyRules.ValidatePhrase("   \t  ");

        Assert.IsFalse(empty.IsValid);
        Assert.IsFalse(whitespace.IsValid);
    }

    [TestMethod]
    public void ValidatePhrase_OverTwentyWords_ReturnsInvalid()
    {
        var twentyWords = string.Join(" ", Enumerable.Range(1, 20).Select(i => $"word{i}"));
        var twentyOneWords = string.Join(" ", Enumerable.Range(1, 21).Select(i => $"word{i}"));

        Assert.IsTrue(VocabularyRules.ValidatePhrase(twentyWords).IsValid);
        var overLimit = VocabularyRules.ValidatePhrase(twentyOneWords);
        Assert.IsFalse(overLimit.IsValid);
    }

    [TestMethod]
    public void ContainsEquivalent_IgnoresCaseAndExtraWhitespace()
    {
        var existingItem = VocabularyRules.CreatePending("Take Into Account");
        var items = new[] { existingItem };

        Assert.IsTrue(VocabularyRules.ContainsEquivalent(items, "take into account"));
        Assert.IsTrue(VocabularyRules.ContainsEquivalent(items, "  TAKE   INTO   ACCOUNT  "));
        Assert.IsFalse(VocabularyRules.ContainsEquivalent(items, "take into consideration"));
    }

    [TestMethod]
    public void FindEquivalent_MatchesExactCaseInsensitiveAndPunctuation()
    {
        var existingItem = VocabularyRules.CreatePending("questions");
        var items = new[] { existingItem };

        var exact = VocabularyRules.FindEquivalent(items, "questions");
        var upper = VocabularyRules.FindEquivalent(items, "QUESTIONS");
        var punctuated = VocabularyRules.FindEquivalent(items, "questions.");
        var quoted = VocabularyRules.FindEquivalent(items, "\"questions\"");
        var none = VocabularyRules.FindEquivalent(items, "answers");

        Assert.IsNotNull(exact);
        Assert.AreEqual(existingItem.Id, exact.Id);
        Assert.IsNotNull(upper);
        Assert.AreEqual(existingItem.Id, upper.Id);
        Assert.IsNotNull(punctuated);
        Assert.AreEqual(existingItem.Id, punctuated.Id);
        Assert.IsNotNull(quoted);
        Assert.AreEqual(existingItem.Id, quoted.Id);
        Assert.IsNull(none);
    }

    [TestMethod]
    public void FindEquivalent_ExistingItemHasPunctuation_MatchesCleanQuery()
    {
        var existingItem = new VocabularyItem(Guid.NewGuid(), "people.", "people.", Status: VocabularyStatus.Ready);
        var items = new[] { existingItem };

        var match = VocabularyRules.FindEquivalent(items, "people");
        Assert.IsNotNull(match);
        Assert.AreEqual(existingItem.Id, match.Id);
    }

    [TestMethod]
    public void TrimPunctuation_RemovesLeadingAndTrailingPunctuation()
    {
        Assert.AreEqual("hello", VocabularyRules.TrimPunctuation("...hello!?"));
        Assert.AreEqual("by attending meeting", VocabularyRules.TrimPunctuation("“by attending meeting”"));
        Assert.AreEqual(string.Empty, VocabularyRules.TrimPunctuation("   "));
    }

    [TestMethod]
    public void CreatePending_SetsInitialPropertiesCorrectly()
    {
        var item = VocabularyRules.CreatePending("  Fluent English  ");

        Assert.AreNotEqual(Guid.Empty, item.Id);
        Assert.AreEqual("Fluent English", item.Phrase);
        Assert.AreEqual("fluent english", item.NormalizedPhrase);
        Assert.AreEqual(VocabularyStatus.Pending, item.Status);
        Assert.IsNull(item.Meaning);
        Assert.IsNull(item.Example);
        Assert.IsNull(item.PronunciationIpa);
        Assert.IsFalse(item.IsHidden);
        Assert.IsNull(item.LastError);
    }

    [TestMethod]
    public void StudySentence_DefaultsToEmptyVocabulary_PreservingCompletion()
    {
        var id = Guid.NewGuid();
        var sentence = new StudySentence(id, "Practice sentence.", IsCompleted: true);

        Assert.AreEqual(id, sentence.Id);
        Assert.AreEqual("Practice sentence.", sentence.Text);
        Assert.IsTrue(sentence.IsCompleted);
        Assert.IsNotNull(sentence.Vocabulary);
        Assert.AreEqual(0, sentence.Vocabulary.Count);
    }
}
