namespace PteFloatingSentence.Core.Tests;

[TestClass]
public class WindowPlacementNormalizerTests
{
    [TestMethod]
    public void Normalize_ReturnsSavedPlacement_WhenItIsWithinADisplay()
    {
        var displays = new[] { new DisplayBounds(0, 0, 1920, 1040) };

        var placement = WindowPlacementNormalizer.Normalize(
            AppSettings.Default with { Left = 500, Top = 300 },
            displays);

        Assert.AreEqual<(double Left, double Top)>((500d, 300d), placement);
    }

    [TestMethod]
    public void Normalize_ReturnsOffsetFromFirstDisplay_WhenSavedPlacementIsOutsideAllDisplays()
    {
        var displays = new[] { new DisplayBounds(0, 0, 1920, 1040) };

        var placement = WindowPlacementNormalizer.Normalize(
            AppSettings.Default with { Left = 9000, Top = 9000 },
            displays);

        Assert.AreEqual<(double Left, double Top)>((100d, 100d), placement);
    }

    [TestMethod]
    public void Normalize_ReturnsDefaultPlacement_WhenNoDisplaysAreAvailable()
    {
        var placement = WindowPlacementNormalizer.Normalize(AppSettings.Default, []);

        Assert.AreEqual<(double Left, double Top)>((100d, 100d), placement);
    }
}
