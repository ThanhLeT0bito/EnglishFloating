using PteFloatingSentence.Core;

namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class SentenceValidatorTests
{
    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Validate_EmptyText_ReturnsInvalid(string? text)
    {
        var result = SentenceValidator.Validate(text);
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Enter a sentence.", result.Error);
    }

    [TestMethod]
    public void Validate_TwentyWords_ReturnsValid()
    {
        var text = string.Join(' ', Enumerable.Repeat("word", 20));
        Assert.IsTrue(SentenceValidator.Validate(text).IsValid);
    }

    [TestMethod]
    public void Validate_TwentyOneWords_ReturnsInvalid()
    {
        var text = string.Join(' ', Enumerable.Repeat("word", 21));
        var result = SentenceValidator.Validate(text);
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Use 20 words or fewer.", result.Error);
    }

    [TestMethod]
    public void Validate_MixedWhitespace_CountsWordsCorrectly()
    {
        Assert.IsTrue(SentenceValidator.Validate("one   two\tthree\nfour").IsValid);
    }
}
