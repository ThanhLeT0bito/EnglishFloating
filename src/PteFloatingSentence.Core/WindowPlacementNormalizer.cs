namespace PteFloatingSentence.Core;

public static class WindowPlacementNormalizer
{
    public static (double Left, double Top) Normalize(AppSettings settings, IReadOnlyList<DisplayBounds> displays)
    {
        if (displays.Any(display => display.Contains(settings.Left, settings.Top)))
            return (settings.Left, settings.Top);

        return displays.Count == 0
            ? (100, 100)
            : (displays[0].Left + 100, displays[0].Top + 100);
    }
}
