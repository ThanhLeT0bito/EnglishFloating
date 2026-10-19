namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class SentenceCorrectionsTests
{
    [TestMethod]
    public void WordChanges_ShowsWhatTheAiFixed()
    {
        CollectionAssert.AreEqual(new[] { "Card → car" },
            SentenceCorrections.WordChanges("Your Card can park in the building", "Your car can park in the building.").ToArray());
        CollectionAssert.AreEqual(new[] { "recieve → receive", "form → from" },
            SentenceCorrections.WordChanges("I recieve letters form home.", "I receive letters from home.").ToArray());
        CollectionAssert.AreEqual(new[] { "the → (removed)", "+ a" },
            SentenceCorrections.WordChanges("I saw the dog today", "I saw dog today a").ToArray());
        Assert.AreEqual(0, SentenceCorrections.WordChanges("Hello, world", "Hello world!").Count, "punctuation only");
        Assert.AreEqual(0, SentenceCorrections.WordChanges(null, " ").Count);
    }
}
