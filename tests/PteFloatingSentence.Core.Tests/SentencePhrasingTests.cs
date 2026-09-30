using PteFloatingSentence.Core;

namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class SentencePhrasingTests
{
    [TestMethod]
    public void ParseGroups_ProducesCanonicalTextAndWordBoundaries()
    {
        var result = SentencePhrasing.ParseGroups("I usually go to the gym\nafter work\nwith my friends.");

        Assert.IsTrue(result.Validation.IsValid);
        Assert.AreEqual("I usually go to the gym after work with my friends.", result.Text);
        CollectionAssert.AreEqual(new[] { 6, 8 }, result.BreakAfterWordIndices.ToArray());
    }

    [TestMethod]
    public void ParseGroups_RejectsBlankMiddleGroup()
    {
        var result = SentencePhrasing.ParseGroups("First group\n\nsecond group");

        Assert.IsFalse(result.Validation.IsValid);
    }

    [TestMethod]
    public void NormalizeBreaks_RemovesInvalidAndDuplicateBoundaries()
    {
        var result = SentencePhrasing.NormalizeBreaks("One two three four", [3, 1, 3, 0, 4, -1]);

        CollectionAssert.AreEqual(new[] { 1, 3 }, result.ToArray());
    }

    [TestMethod]
    public void LegacySentence_HasNoPhraseBreaks()
    {
        var sentence = new StudySentence(Guid.NewGuid(), "One two three.");

        Assert.AreEqual(0, sentence.PhraseBreakAfterWordIndices.Count);
    }
}
