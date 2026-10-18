namespace PteFloatingSentence.Core;

public static class WritingTopics
{
    public const string CustomKeyPrefix = "custom:";

    public static IReadOnlyList<WritingTopic> BuiltIn { get; } =
    [
        new("it-work", "Working in IT"),
        new("traffic", "Traffic & transport"),
        new("daily-life", "Daily life"),
        new("work-meetings", "Work & meetings"),
        new("travel", "Travel"),
        new("health", "Health"),
        new("education", "Education"),
        new("shopping", "Shopping"),
        new("food", "Food & cooking"),
        new("family-friends", "Family & friends"),
        new("environment", "Environment"),
        new("technology", "Technology"),
        new("weather", "Weather"),
        new("housing", "Housing")
    ];

    public static bool IsCustomKey(string? key) =>
        key is not null && key.StartsWith(CustomKeyPrefix, StringComparison.Ordinal) && key.Length > CustomKeyPrefix.Length;
}
