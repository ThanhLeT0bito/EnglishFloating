using PteFloatingSentence.Core;

namespace PteFloatingSentence.Core.Tests;

[TestClass]
public class DisplayProjectionTests
{
    [TestMethod]
    public void PrimaryFirst_PlacesPrimaryDisplayBeforeAnEarlierSecondaryDisplay()
    {
        var projectionType = typeof(DisplayBounds).Assembly.GetType("PteFloatingSentence.Core.DisplayProjection");

        Assert.IsNotNull(projectionType);
        var primaryFirstMethod = projectionType.GetMethod("PrimaryFirst");
        Assert.IsNotNull(primaryFirstMethod);

        var secondary = new DisplayBounds(1920, 0, 3840, 1040);
        var primary = new DisplayBounds(0, 0, 1920, 1040);
        var ordered = (IReadOnlyList<DisplayBounds>)primaryFirstMethod.Invoke(null, [new[] { (secondary, false), (primary, true) }])!;
        var placement = WindowPlacementNormalizer.Normalize(AppSettings.Default with { Left = -500, Top = -500 }, ordered);

        CollectionAssert.AreEqual(new[] { primary, secondary }, ordered.ToArray());
        Assert.AreEqual(100d, placement.Left);
        Assert.AreEqual(100d, placement.Top);
    }
}
