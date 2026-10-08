namespace PteFloatingSentence.Core;

public static class WritingTopicRules
{
    public const int MaxNameLength = 40;
    public const int MaxCustomTopics = 30;

    /// <summary>Built-in topics followed by the learner's custom topics.</summary>
    public static IReadOnlyList<WritingTopic> AllTopics(AppSettings settings) =>
        [.. WritingTopics.BuiltIn, .. Normalize(settings).CustomWritingTopics];

    /// <summary>Topics currently selected, in catalog order.</summary>
    public static IReadOnlyList<WritingTopic> SelectedTopics(AppSettings settings)
    {
        var normalized = Normalize(settings);
        var selected = normalized.SelectedWritingTopicKeys.ToHashSet(StringComparer.Ordinal);
        return AllTopics(normalized).Where(topic => selected.Contains(topic.Key)).ToList();
    }

    public static (AppSettings Settings, ValidationResult Result) AddCustom(AppSettings settings, string? name)
    {
        var normalized = Normalize(settings);
        var cleanName = CleanName(name);
        if (cleanName.Length == 0)
            return (normalized, new(false, "Enter a topic name."));
        if (cleanName.Length > MaxNameLength)
            return (normalized, new(false, $"Use {MaxNameLength} characters or fewer."));
        if (AllTopics(normalized).Any(topic => string.Equals(topic.Name, cleanName, StringComparison.OrdinalIgnoreCase)))
            return (normalized, new(false, "This topic already exists."));
        if (normalized.CustomWritingTopics.Count >= MaxCustomTopics)
            return (normalized, new(false, $"You can add up to {MaxCustomTopics} topics."));

        var topic = new WritingTopic(WritingTopics.CustomKeyPrefix + Guid.NewGuid().ToString("N"), cleanName);
        return (normalized with { CustomWritingTopics = [.. normalized.CustomWritingTopics, topic] }, new(true, null));
    }

    public static AppSettings RemoveCustom(AppSettings settings, string key)
    {
        var normalized = Normalize(settings);
        if (!WritingTopics.IsCustomKey(key))
            return normalized;

        return normalized with
        {
            CustomWritingTopics = normalized.CustomWritingTopics.Where(topic => topic.Key != key).ToList(),
            SelectedWritingTopicKeys = normalized.SelectedWritingTopicKeys.Where(selected => selected != key).ToList()
        };
    }

    public static AppSettings ToggleSelection(AppSettings settings, string key)
    {
        var normalized = Normalize(settings);
        if (!AllTopics(normalized).Any(topic => topic.Key == key))
            return normalized;

        var selected = normalized.SelectedWritingTopicKeys.Contains(key, StringComparer.Ordinal)
            ? normalized.SelectedWritingTopicKeys.Where(existing => existing != key).ToList()
            : [.. normalized.SelectedWritingTopicKeys, key];
        return normalized with { SelectedWritingTopicKeys = selected };
    }

    public static AppSettings Normalize(AppSettings settings)
    {
        var builtInNames = WritingTopics.BuiltIn.Select(topic => topic.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(builtInNames, StringComparer.OrdinalIgnoreCase);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var custom = new List<WritingTopic>();
        foreach (var topic in settings.CustomWritingTopics ?? [])
        {
            if (topic is null || !WritingTopics.IsCustomKey(topic.Key))
                continue;
            var cleanName = CleanName(topic.Name);
            if (cleanName.Length == 0 || cleanName.Length > MaxNameLength)
                continue;
            if (!seenKeys.Add(topic.Key) || !seenNames.Add(cleanName))
                continue;
            custom.Add(topic with { Name = cleanName });
            if (custom.Count == MaxCustomTopics)
                break;
        }

        var knownKeys = WritingTopics.BuiltIn.Select(topic => topic.Key).Concat(custom.Select(topic => topic.Key))
            .ToHashSet(StringComparer.Ordinal);
        var selected = (settings.SelectedWritingTopicKeys ?? [])
            .Where(key => key is not null && knownKeys.Contains(key))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return settings with
        {
            CustomWritingTopics = custom,
            SelectedWritingTopicKeys = selected,
            WritingLevel = Enum.IsDefined(settings.WritingLevel) ? settings.WritingLevel : WritingLevel.Medium,
            WritingExplanationLanguage = Enum.IsDefined(settings.WritingExplanationLanguage)
                ? settings.WritingExplanationLanguage
                : WritingExplanationLanguage.English
        };
    }

    private static string CleanName(string? name) =>
        string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
