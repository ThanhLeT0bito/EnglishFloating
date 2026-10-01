using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows.Infrastructure;

public sealed class JsonSettingsStore
{
    private static readonly Regex ColorPattern = new("^#[0-9A-Fa-f]{8}$", RegexOptions.Compiled);

    public JsonSettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PteFloatingSentence",
            "settings.json");
    }

    public string FilePath { get; }

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            var json = await File.ReadAllTextAsync(FilePath);
            using var document = JsonDocument.Parse(json);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            if (settings is null)
                return AppSettings.Default;

            return IsVersion2(document.RootElement)
                ? NormalizeVersion2(settings)
                : MigrateVersion1(settings);
        }
        catch (IOException)
        {
            return AppSettings.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return AppSettings.Default;
        }
        catch (JsonException)
        {
            return AppSettings.Default;
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(NormalizeVersion2(settings), new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(FilePath, json);
    }

    private static bool IsVersion2(JsonElement root) =>
        root.TryGetProperty(nameof(AppSettings.Version), out var version)
        && version.ValueKind == JsonValueKind.Number
        && version.TryGetInt32(out var value)
        && value >= 2;

    private static AppSettings MigrateVersion1(AppSettings settings)
    {
        var legacy = NormalizeDisplay(settings);
        var migrated = StudyListRules.CreateDefault(legacy.Sentence);

        return migrated with
        {
            FontSize = legacy.FontSize,
            TextColor = legacy.TextColor,
            BackgroundOpacity = legacy.BackgroundOpacity,
            Left = legacy.Left,
            Top = legacy.Top,
            ShowSentenceOverlay = legacy.ShowSentenceOverlay,
            ShowVocabularyCards = legacy.ShowVocabularyCards
        };
    }

    private static AppSettings NormalizeVersion2(AppSettings settings) =>
        NormalizeDisplay(StudyListRules.Normalize(settings));

    private static AppSettings NormalizeDisplay(AppSettings settings)
    {
        var defaults = AppSettings.Default;
        return settings with
        {
            Sentence = StudyListRules.NormalizeSentence(settings.Sentence),
            FontSize = settings.FontSize is >= 12 and <= 96 ? settings.FontSize : defaults.FontSize,
            BackgroundOpacity = settings.BackgroundOpacity is >= 0 and <= 1 ? settings.BackgroundOpacity : defaults.BackgroundOpacity,
            TextColor = settings.TextColor is not null && ColorPattern.IsMatch(settings.TextColor)
                ? settings.TextColor
                : defaults.TextColor
        };
    }
}
