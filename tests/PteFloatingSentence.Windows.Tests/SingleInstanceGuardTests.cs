using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public sealed class SingleInstanceGuardTests
{
    [TestMethod]
    public void TryAcquire_SecondGuardWithSameName_ReturnsFalse()
    {
        var name = $"EnglishFloating.Tests.{Guid.NewGuid():N}";

        Assert.IsTrue(SingleInstanceGuard.TryAcquire(name, out var first));
        using (first!)
        {
            Assert.IsFalse(SingleInstanceGuard.TryAcquire(name, out _));
        }
    }
}
