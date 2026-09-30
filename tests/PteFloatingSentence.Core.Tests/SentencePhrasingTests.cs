using PteFloatingSentence.Core;

namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class SentencePhrasingTests
{
    [TestMethod]
    public void DisplayText_UsesWideSpacesWithoutChangingCanonicalSentence()
    {
        var text = "I go after work.";
        Assert.AreEqual("I go\u2003\u2003after work.", SentencePhrasing.GetDisplayText(text, [2]));
        Assert.AreEqual("go\u2003\u2003after", SentencePhrasing.GetDisplayRange(text, 2, 8, [2]));
    }

    [TestMethod]
    public void SentenceMetadata_JsonRoundTripAndLegacyAreCompatible()
    {
        var sentence = new StudySentence(Guid.NewGuid(), "I go after work.", PhraseBreakAfterWordIndices: [2]);
        var json = System.Text.Json.JsonSerializer.Serialize(sentence);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<StudySentence>(json)!;
        CollectionAssert.AreEqual(new[] { 2 }, loaded.PhraseBreakAfterWordIndices.ToArray());
        var legacy = System.Text.Json.JsonSerializer.Deserialize<StudySentence>("{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Text\":\"I go after work.\"}")!;
        Assert.AreEqual(0, legacy.PhraseBreakAfterWordIndices.Count);
    }

    [TestMethod]
    public void ParseGroups_RejectsMoreThanTwentyWords()
    {
        Assert.IsFalse(SentencePhrasing.ParseGroups(string.Join(" ", Enumerable.Repeat("word", 21))).Validation.IsValid);
    }

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
