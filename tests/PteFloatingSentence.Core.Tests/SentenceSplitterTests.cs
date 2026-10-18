namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class SentenceSplitterTests
{
    [TestMethod]
    public void NumberedList_OneSentencePerLineWithoutNumbers()
    {
        var sentences = SentenceSplitter.Split("1. Stock predictions can be very capricious even for professionals\r\n2. We are looking for new ways to engage learners.\n\n3) You are trained to be a specialized journalist.");

        CollectionAssert.AreEqual(new[]
        {
            "Stock predictions can be very capricious even for professionals",
            "We are looking for new ways to engage learners.",
            "You are trained to be a specialized journalist."
        }, sentences.ToArray());
    }

    [TestMethod]
    public void Paragraph_SplitsAtSentenceEnds()
    {
        var sentences = SentenceSplitter.Split("There was a lot of traffic this morning. You need to pass the written exam!  Do you have any questions? Please raise your hand.");

        CollectionAssert.AreEqual(new[]
        {
            "There was a lot of traffic this morning.",
            "You need to pass the written exam!",
            "Do you have any questions?",
            "Please raise your hand."
        }, sentences.ToArray());
    }

    [TestMethod]
    public void AbbreviationsInitialsAndNumbers_DoNotSplit()
    {
        CollectionAssert.AreEqual(new[] { "Dr. Smith arrived at 9 a.m. today.", "The rate rose 3.5 percent." },
            SentenceSplitter.Split("Dr. Smith arrived at 9 a.m. today. The rate rose 3.5 percent.").ToArray());
        CollectionAssert.AreEqual(new[] { "J. K. Rowling moved to the U.S.A. last year.", "She wrote, e.g. novels." },
            SentenceSplitter.Split("J. K. Rowling moved to the U.S.A. last year. She wrote, e.g. novels.").ToArray());
    }

    [TestMethod]
    public void QuotesBulletsAndEmptyLines()
    {
        CollectionAssert.AreEqual(new[] { "He said \"stop.\"", "Then he left." }, SentenceSplitter.Split("He said \"stop.\" Then he left.").ToArray());
        CollectionAssert.AreEqual(new[] { "First one", "Second one" }, SentenceSplitter.Split("- First one\n• Second one\n\n   \n").ToArray());
        Assert.AreEqual(0, SentenceSplitter.Split("  ").Count);
        Assert.AreEqual(0, SentenceSplitter.Split("1.\n2.").Count);
        Assert.AreEqual(1, SentenceSplitter.Split("Just one sentence without a full stop").Count);
        Assert.AreEqual("Many spaces here.", SentenceSplitter.Split("Many    spaces\there.").Single());
    }

    [TestMethod]
    public void Split_KeepsAtMostTheLimit()
    {
        var text = string.Join(" ", Enumerable.Range(1, 150).Select(i => $"Sentence number {i}."));

        Assert.AreEqual(SentenceSplitter.MaxSentences, SentenceSplitter.Split(text).Count);
    }
}
