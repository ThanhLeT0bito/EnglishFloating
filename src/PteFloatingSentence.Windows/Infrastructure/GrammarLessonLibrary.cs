using System.IO;
using System.Text.Json;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows.Infrastructure;

public interface IGrammarLessonStore
{
    IReadOnlyList<GrammarLesson> Load();
    void Save(IReadOnlyList<GrammarLesson> lessons);
}

public sealed class InMemoryGrammarLessonStore : IGrammarLessonStore
{
    private IReadOnlyList<GrammarLesson> _lessons = [];

    public int SaveCount { get; private set; }

    public IReadOnlyList<GrammarLesson> Load() => _lessons;

    public void Save(IReadOnlyList<GrammarLesson> lessons)
    {
        _lessons = lessons;
        SaveCount++;
    }
}

/// <summary>Keeps lessons in grammar-lessons.json next to settings.json. Read and write errors are ignored.</summary>
public sealed class JsonGrammarLessonStore : IGrammarLessonStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public JsonGrammarLessonStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PteFloatingSentence",
            "grammar-lessons.json");
    }

    public string FilePath { get; }

    public IReadOnlyList<GrammarLesson> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            var lessons = JsonSerializer.Deserialize<List<GrammarLesson>>(File.ReadAllText(FilePath), Options) ?? [];
            return lessons.Select(GrammarLessonRules.Sanitize).OfType<GrammarLesson>().ToList();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<GrammarLesson> lessons)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(lessons, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A lesson that is not saved is written again next time.
        }
    }
}

/// <summary>
/// Grammar lessons by topic and explanation language: saved ones open at once, new ones are written by the tutor
/// and kept (the newest <see cref="MaxLessons"/>). Use it from the UI thread.
/// </summary>
public sealed class GrammarLessonLibrary
{
    public const int MaxLessons = 100;

    private readonly IGrammarTutor? _tutor;
    private readonly IGrammarLessonStore _store;
    private readonly Dictionary<string, GrammarLesson> _lessons;

    public GrammarLessonLibrary(IGrammarTutor? tutor, IGrammarLessonStore store)
    {
        _tutor = tutor;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _lessons = _store.Load()
            .GroupBy(lesson => GrammarLessonRules.CacheKey(lesson.TopicKey, lesson.Language))
            .ToDictionary(group => group.Key, group => group.OrderByDescending(lesson => lesson.CreatedAt).First(), StringComparer.Ordinal);
    }

    public static GrammarLessonLibrary InMemory(IGrammarTutor? tutor = null) => new(tutor, new InMemoryGrammarLessonStore());

    public bool CanWriteLessons => _tutor is not null;

    public int Count => _lessons.Count;

    /// <summary>The saved lesson, or null.</summary>
    public GrammarLesson? Cached(GrammarTopic topic, WritingExplanationLanguage language) =>
        _lessons.GetValueOrDefault(GrammarLessonRules.CacheKey(topic.Key, language));

    /// <summary>Returns the saved lesson, or asks the tutor for one (always when <paramref name="refresh"/>) and saves it.</summary>
    public async Task<GrammarLesson> GetAsync(GrammarTopic topic, WritingExplanationLanguage language, bool refresh = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topic);
        if (!refresh && Cached(topic, language) is { } cached)
            return cached;
        if (_tutor is null)
            throw new InvalidOperationException(WritingPresentation.NoCoachMessage);

        var lesson = await _tutor.CreateLessonAsync(topic, language, cancellationToken);
        _lessons[GrammarLessonRules.CacheKey(topic.Key, language)] = lesson;
        if (_lessons.Count > MaxLessons)
        {
            foreach (var old in _lessons.OrderBy(pair => pair.Value.CreatedAt).Take(_lessons.Count - MaxLessons).Select(pair => pair.Key).ToList())
                _lessons.Remove(old);
        }

        _store.Save(_lessons.Values.OrderBy(saved => saved.CreatedAt).ToList());
        return lesson;
    }
}
