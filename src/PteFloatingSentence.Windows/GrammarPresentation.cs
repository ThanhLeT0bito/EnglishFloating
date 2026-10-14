using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

/// <summary>WPF-free text and colors for the Grammar page, the drill and the Learn hints.</summary>
public static class GrammarPresentation
{
    public static string StatusLabel(GrammarTopicStatus status) => status switch
    {
        GrammarTopicStatus.NeedsReview => "Needs review",
        GrammarTopicStatus.Improving => "Improving",
        GrammarTopicStatus.Good => "Good",
        _ => "Not practiced"
    };

    public static string StatusColor(GrammarTopicStatus status) => status switch
    {
        GrammarTopicStatus.NeedsReview => "#FFF87171",
        GrammarTopicStatus.Improving => "#FFFBBF24",
        GrammarTopicStatus.Good => "#FF34D399",
        _ => "#FF94A3B8"
    };

    /// <summary>"Present simple · Thì hiện tại đơn", or just the name for a custom topic.</summary>
    public static string TopicTitle(GrammarTopic topic) =>
        topic.IsCustom || string.Equals(topic.Name, topic.VietnameseName, StringComparison.OrdinalIgnoreCase)
            ? topic.Name
            : $"{topic.Name} · {topic.VietnameseName}";

    /// <summary>One line for a weak topic, e.g. "6 mistakes in 30 days · last drill 2/5".</summary>
    public static string StatLine(GrammarTopicStat stat)
    {
        var mistakes = stat.RecentMistakes == 1 ? "1 mistake in 30 days" : $"{stat.RecentMistakes} mistakes in 30 days";
        return stat.LastDrill is null ? mistakes : $"{mistakes} · last drill {stat.LastDrill.Passed}/{stat.LastDrill.Total}";
    }

    public static string LearnButtonText(GrammarTopic topic) => $"Learn: {topic.Name}";

    /// <summary>Hint under a checked answer when the learner keeps making the same kind of mistake.</summary>
    public static string RepeatHint(GrammarTopicStat stat) =>
        $"You often get {stat.Topic.Name} wrong ({stat.MistakesSinceDrill} times). Learn it →";

    public static string DrillProgress(GrammarDrill drill) =>
        $"Drill · {drill.Topic.Name} · sentence {Math.Min(drill.Done + 1, drill.Length)} of {drill.Length} · {drill.Passed} without {drill.Topic.Name} mistakes";

    public static string DrillSummary(GrammarDrill drill) =>
        drill.Passed * 5 >= drill.Length * 4
            ? $"Drill done: {drill.Passed}/{drill.Length} sentences without {drill.Topic.Name} mistakes. Well done!"
            : $"Drill done: {drill.Passed}/{drill.Length} sentences without {drill.Topic.Name} mistakes. Review the lesson and try again.";

    public static string LastDrillText(GrammarTopicStat stat, DateTimeOffset now) =>
        stat.LastDrill is null
            ? "Not practiced yet."
            : $"Last drill: {stat.LastDrill.Passed}/{stat.LastDrill.Total} · {WritingPresentation.RelativeDay(stat.LastDrill.At, now)}";

    public static string SearchHint(string query, int matches) =>
        matches == 0
            ? $"No built-in topic matches \"{query}\". Press Enter or Learn to get a lesson about it anyway."
            : $"{matches} matching topic{(matches == 1 ? string.Empty : "s")}. Press Enter to open the first one.";

    public const string LoadingText = "Preparing the lesson… (saved for next time)";

    // Section headings follow the explanation language.
    public static string EssenceHeading(WritingExplanationLanguage language) => Pick(language, "The core idea", "Bản chất");
    public static string FormHeading(WritingExplanationLanguage language) => Pick(language, "Form", "Công thức");
    public static string UsesHeading(WritingExplanationLanguage language) => Pick(language, "When to use it", "Khi nào dùng");
    public static string SignalHeading(WritingExplanationLanguage language) => Pick(language, "Signal words", "Dấu hiệu nhận biết");
    public static string ContrastHeading(WritingExplanationLanguage language) => Pick(language, "Don't confuse it with", "Đừng nhầm với");
    public static string CommonMistakesHeading(WritingExplanationLanguage language) => Pick(language, "Common mistakes", "Lỗi thường gặp");
    public static string YourMistakesHeading(WritingExplanationLanguage language) => Pick(language, "Your mistakes", "Lỗi của bạn");
    public static string ExamplesHeading(WritingExplanationLanguage language) => Pick(language, "Examples", "Ví dụ");
    public static string QuizHeading(WritingExplanationLanguage language) => Pick(language, "Quick check", "Kiểm tra nhanh");
    public static string PracticeButtonText(WritingExplanationLanguage language) =>
        Pick(language, $"Practice {GrammarProgressRules.DrillLength} sentences", $"Luyện {GrammarProgressRules.DrillLength} câu");
    public static string NoOwnMistakesText(WritingExplanationLanguage language) =>
        Pick(language, "No mistakes on this topic in your notebook yet.", "Sổ lỗi chưa có lỗi nào về chủ điểm này.");

    public static string QuizResult(bool correct, string explanation) =>
        (correct ? "✓ Correct. " : "✗ Not quite. ") + explanation;

    private static string Pick(WritingExplanationLanguage language, string english, string vietnamese) =>
        language == WritingExplanationLanguage.Vietnamese ? vietnamese : english;
}
