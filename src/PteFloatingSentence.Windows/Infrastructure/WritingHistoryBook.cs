using System.IO;
using System.Text.Json;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows.Infrastructure;

public interface IWritingHistoryStore
{
    WritingHistory Load();
    void Save(WritingHistory history);
}

/// <summary>Keeps the history in memory only (tests and fallback).</summary>
public sealed class InMemoryWritingHistoryStore : IWritingHistoryStore
{
    private WritingHistory _history;

    public InMemoryWritingHistoryStore(WritingHistory? history = null)
    {
        _history = history ?? WritingHistory.Empty;
    }

    public int SaveCount { get; private set; }

    public WritingHistory Load() => _history;

    public void Save(WritingHistory history)
    {
        _history = history;
        SaveCount++;
    }
}

/// <summary>Stores the history as writing-history.json next to settings.json. Read and write errors are ignored.</summary>
public sealed class JsonWritingHistoryStore : IWritingHistoryStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public JsonWritingHistoryStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PteFloatingSentence",
            "writing-history.json");
    }

    public string FilePath { get; }

    public WritingHistory Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return WritingHistory.Empty;
            return WritingHistoryRules.Normalize(JsonSerializer.Deserialize<WritingHistory>(File.ReadAllText(FilePath), Options));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return WritingHistory.Empty;
        }
    }

    public void Save(WritingHistory history)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(history, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The notebook is a convenience; losing one write must not break practice.
        }
    }
}

/// <summary>
/// The learner's mistake notebook, shared by the Settings page and the floating window.
/// Every change is saved at once and announced through <see cref="Changed"/>. Use it from the UI thread.
/// </summary>
public sealed class WritingHistoryBook
{
    private readonly IWritingHistoryStore _store;
    private readonly Func<DateTimeOffset> _clock;

    public WritingHistoryBook(IWritingHistoryStore store, Func<DateTimeOffset>? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? (() => DateTimeOffset.Now);
        Current = WritingHistoryRules.Normalize(_store.Load());
    }

    public static WritingHistoryBook InMemory() => new(new InMemoryWritingHistoryStore());

    public WritingHistory Current { get; private set; }

    public DateTimeOffset Now => _clock();

    public event Action? Changed;

    public IReadOnlyList<WritingTagStat> TagStats() => WritingHistoryRules.TagStats(Current, Now);

    public IReadOnlyList<WritingSkillTag> WeakTags() => WritingHistoryRules.WeakTags(Current, Now);

    public bool CanPracticeWeakPoints => WritingHistoryRules.CanPracticeWeakPoints(Current, Now);

    public int RecentMistakeCount => WritingHistoryRules.RecentMistakeCount(Current, Now);

    /// <summary>Records the mistakes of a check and, for a weak-point sentence, whether each focus tag was passed.</summary>
    public void RecordCheck(WritingPracticeMode mode, WritingPrompt prompt, string answer, WritingFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(feedback);
        var now = Now;
        var source = WritingModes.HistorySource(prompt, mode);
        var updated = WritingHistoryRules.RecordFeedback(Current, mode, source, answer, feedback, now);
        updated = WritingHistoryRules.RecordFocusResult(updated, prompt.FocusTags, feedback, now);
        updated = WritingHistoryRules.RecordCheckScore(updated, new WritingCheckRecord(now, mode, prompt.Level, feedback.Score)
        {
            FocusTopicKey = prompt.FocusTopicKey
        });
        Replace(updated);
    }

    /// <summary>Saves a finished quick check of a lesson.</summary>
    public void RecordQuiz(string topicKey, int correct, int total) =>
        Replace(WritingHistoryRules.RecordQuiz(Current, new GrammarQuizResult(Now, topicKey, correct, total)));

    /// <summary>Makes <paramref name="stageKey"/> the current roadmap stage; null follows progress again.</summary>
    public void ChooseRoadmapStage(string? stageKey) => Replace(RoadmapRules.ChooseStage(Current, stageKey));

    public IReadOnlyList<(RoadmapStageProgress Progress, RoadmapStageState State)> RoadmapOverview() => RoadmapRules.Overview(Current, Now);

    public RoadmapStage CurrentRoadmapStage() => RoadmapRules.CurrentStage(Current, Now);

    public RoadmapStageProgress RoadmapProgress(RoadmapStage stage) => RoadmapRules.Progress(Current, stage, Now);

    /// <summary>Today's session plan for the current stage.</summary>
    public IReadOnlyList<RoadmapStep> TodayPlan() => RoadmapRules.TodayPlan(Current, RoadmapProgress(CurrentRoadmapStage()), Now);

    public int Streak => RoadmapRules.Streak(Current, Now);

    public int ChecksToday => RoadmapRules.ChecksToday(Current, Now);

    /// <summary>Saves the result of a grammar drill.</summary>
    public void RecordDrill(GrammarDrillResult result) => Replace(GrammarProgressRules.RecordDrill(Current, result));

    public GrammarTopicStat StatFor(GrammarTopic topic) => GrammarProgressRules.StatFor(Current, topic, Now);

    /// <summary>Grammar topics to study next (see <see cref="GrammarProgressRules.Recommendations"/>).</summary>
    public IReadOnlyList<GrammarTopicStat> GrammarRecommendations() => GrammarProgressRules.Recommendations(Current, Now);

    public IReadOnlyList<GrammarTopicStat> GrammarStats() => GrammarProgressRules.Stats(Current, Now);

    /// <summary>Topics of <paramref name="issues"/> the learner keeps getting wrong.</summary>
    public IReadOnlyList<GrammarTopicStat> RepeatedTopics(IEnumerable<WritingIssue>? issues) =>
        GrammarProgressRules.RepeatedTopics(Current, issues, Now);

    public IReadOnlyList<WritingMistakeGroup> MistakesFor(string topicKey, int count) =>
        GrammarProgressRules.MistakesFor(Current, topicKey, count);

    public void Clear() => Replace(WritingHistory.Empty);

    private void Replace(WritingHistory updated)
    {
        if (ReferenceEquals(updated, Current))
            return;

        Current = updated;
        _store.Save(updated);
        Changed?.Invoke();
    }
}
