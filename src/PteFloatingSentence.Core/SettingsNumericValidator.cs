namespace PteFloatingSentence.Core;

public static class SettingsNumericValidator
{
    public static bool IsValidFontSize(double value) => double.IsFinite(value) && value is >= 12 and <= 96;

    public static bool IsValidOpacity(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
}
