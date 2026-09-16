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
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return settings is null ? AppSettings.Default : Normalize(settings);
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

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(FilePath, json);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        var defaults = AppSettings.Default;
        return settings with
        {
            Sentence = SentenceValidator.Validate(settings.Sentence).IsValid ? settings.Sentence : defaults.Sentence,
            FontSize = settings.FontSize is >= 12 and <= 96 ? settings.FontSize : defaults.FontSize,
            BackgroundOpacity = settings.BackgroundOpacity is >= 0 and <= 1 ? settings.BackgroundOpacity : defaults.BackgroundOpacity,
            TextColor = settings.TextColor is not null && ColorPattern.IsMatch(settings.TextColor)
                ? settings.TextColor
                : defaults.TextColor
        };
    }
}
