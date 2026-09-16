namespace PteFloatingSentence.Core;

public static class DisplayProjection
{
    public static IReadOnlyList<DisplayBounds> PrimaryFirst(IEnumerable<(DisplayBounds Bounds, bool IsPrimary)> displays) => displays
        .OrderByDescending(display => display.IsPrimary)
        .Select(display => display.Bounds)
        .ToArray();
}
