using System.IO;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

/// <summary>The mistake notebook: what a check records, and that a saved notebook is never lost.</summary>
[TestClass]
public sealed class NotebookTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.FromHours(7));

    private static readonly WritingIssue TenseIssue =
        new(WritingIssueType.Grammar, "go", "goes", "Third person -s.") { Tag = WritingSkillTag.Tense, TopicKey = "present-simple" };

    private static WritingPrompt Prompt(string vietnamese = "Câu") =>
        new(vietnamese, "She goes to work.", "Daily life", WritingLevel.Easy, WritingTips.Empty) { FocusTags = [WritingSkillTag.Tense] };

    private static WritingFeedback Feedback(int score, params WritingIssue[] issues) =>
        new(score, issues.Length == 0 ? WritingVerdict.Correct : WritingVerdict.Almost, "She goes to work.", null, "Ok.", issues);

    private static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pte-notebook-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    [TestMethod]
    public void CheckingTheSamePromptAgain_CountsOneSentence()
    {
        var book = new WritingHistoryBook(new InMemoryWritingHistoryStore(), () => Now);
        var prompt = Prompt();

        book.RecordCheck(WritingPracticeMode.Translate, prompt, "She go to work.", Feedback(40, TenseIssue));
        book.RecordCheck(WritingPracticeMode.Translate, prompt, "She goes to work.", Feedback(100));
        book.RecordCheck(WritingPracticeMode.Translate, prompt, "She goes to work!", Feedback(100));

        Assert.AreEqual(40, book.Current.Checks.Single().Score, "only the first check is a sentence");
        Assert.IsFalse(book.Current.Reviews.Single().Passed, "the weak point is not marked passed by re-checking the correction");
        Assert.AreEqual(1, book.Current.Mistakes.Count);

        book.RecordCheck(WritingPracticeMode.Translate, Prompt("Câu khác"), "She goes to work.", Feedback(95));
        Assert.AreEqual(2, book.Current.Checks.Count, "a new prompt with the same text is a new sentence");
    }

    [TestMethod]
    public void RecheckingWithNewMistakes_StillRecordsThem()
    {
        var book = new WritingHistoryBook(new InMemoryWritingHistoryStore(), () => Now);
        var prompt = Prompt();
        book.RecordCheck(WritingPracticeMode.Translate, prompt, "She goes to the work.", Feedback(80,
            new WritingIssue(WritingIssueType.Grammar, "the work", "work", "No article.") { Tag = WritingSkillTag.Article }));

        book.RecordCheck(WritingPracticeMode.Translate, prompt, "She go to work.", Feedback(60, TenseIssue));

        Assert.AreEqual(2, book.Current.Mistakes.Count);
        Assert.AreEqual(1, book.Current.Checks.Count);
    }

    [TestMethod]
    public void Dictation_DoesNotMarkGrammarWeakPointsPassed()
    {
        var book = new WritingHistoryBook(new InMemoryWritingHistoryStore(), () => Now);

        book.RecordCheck(WritingPracticeMode.Dictation, Prompt(), "She goes to work", Feedback(50,
            new WritingIssue(WritingIssueType.Spelling, "wrk", "work", "Spelling.") { Tag = WritingSkillTag.Spelling }));

        Assert.AreEqual(0, book.Current.Reviews.Count);
        Assert.AreEqual(1, book.Current.Checks.Count, "the sentence still counts");
    }

    [TestMethod]
    public void ClearMistakes_KeepsRoadmapProgress()
    {
        var book = new WritingHistoryBook(new InMemoryWritingHistoryStore(), () => Now);
        book.RecordCheck(WritingPracticeMode.Translate, Prompt(), "She go to work.", Feedback(40, TenseIssue));
        book.RecordQuiz("present-simple", 1, 1);
        book.RecordDrill(new GrammarDrillResult(Now, "present-simple", 5, 5));
        book.ChooseRoadmapStage("tenses");

        book.Clear();

        Assert.AreEqual(0, book.Current.Mistakes.Count);
        Assert.AreEqual(0, book.Current.Reviews.Count);
        Assert.AreEqual(1, book.Current.Checks.Count);
        Assert.AreEqual(1, book.Current.Quizzes.Count);
        Assert.AreEqual(1, book.Current.Drills.Count);
        Assert.AreEqual("tenses", book.Current.RoadmapStageKey);
        Assert.AreEqual(1, book.Streak);
    }

    [TestMethod]
    public void ClearMistakes_DoesNotCountAsFinishingAStage()
    {
        var book = new WritingHistoryBook(new InMemoryWritingHistoryStore(), () => Now);
        var stage = WritingRoadmap.All[0];
        foreach (var key in stage.TopicKeys)
        {
            book.RecordQuiz(key, 3, 3);
            book.RecordDrill(new GrammarDrillResult(Now.AddHours(-1), key, 5, 5));
        }

        // Articles keep going wrong, so the stage is not done although everything else is.
        var articles = new WritingIssue(WritingIssueType.Grammar, "a", "the", "Use the.") { Tag = WritingSkillTag.Article, TopicKey = "articles" };
        for (var i = 0; i < 3; i++)
            book.RecordCheck(WritingPracticeMode.Translate, new WritingPrompt($"Bài {i}", "x", "y", WritingLevel.Easy, WritingTips.Empty), "x", Feedback(80, articles));
        for (var i = 0; i < RoadmapRules.RequiredSentences; i++)
            book.RecordCheck(stage.Practices[0].Mode, new WritingPrompt($"Câu {i}", "x", "y", stage.Practices[0].Level, WritingTips.Empty), "x", Feedback(90));
        Assert.AreEqual(0, book.Current.CompletedStages.Count);

        book.Clear();

        Assert.AreEqual(0, book.Current.CompletedStages.Count, "emptying the notebook is not finishing the stage");
    }

    [TestMethod]
    public void FinishingAStage_IsSavedAsSoonAsItHappens()
    {
        var book = new WritingHistoryBook(new InMemoryWritingHistoryStore(), () => Now);
        var stage = WritingRoadmap.All[0];
        foreach (var key in stage.TopicKeys)
        {
            book.RecordQuiz(key, 3, 3);
            book.RecordDrill(new GrammarDrillResult(Now, key, 5, 5));
        }

        Assert.AreEqual(0, book.Current.CompletedStages.Count);
        for (var i = 0; i < RoadmapRules.RequiredSentences; i++)
            book.RecordCheck(stage.Practices[0].Mode, new WritingPrompt($"Câu {i}", "She goes to work.", "Daily life", stage.Practices[0].Level, WritingTips.Empty), "She goes to work.", Feedback(90));

        Assert.AreEqual("simple-sentences", book.Current.CompletedStages.Single().StageKey);
        Assert.AreEqual("tenses", book.CurrentRoadmapStage().Key);
    }

    [TestMethod]
    public void JsonStore_KeepsABrokenFileAsideInsteadOfOverwritingIt()
    {
        var directory = TempDirectory();
        try
        {
            var path = Path.Combine(directory, "writing-history.json");
            const string broken = "{ \"Mistakes\": [ { \"At\": \"not a date\" } ] }";
            File.WriteAllText(path, broken);
            var store = new JsonWritingHistoryStore(path);

            var book = new WritingHistoryBook(store, () => Now);
            book.RecordCheck(WritingPracticeMode.Translate, Prompt(), "She go to work.", Feedback(40, TenseIssue));

            Assert.IsNotNull(store.BackupPath);
            Assert.AreEqual(broken, File.ReadAllText(store.BackupPath!), "the learner's old notebook is kept");
            StringAssert.Contains(Path.GetFileName(store.BackupPath!), "writing-history.bad-");
            Assert.AreEqual(1, new JsonWritingHistoryStore(path).Load().Mistakes.Count, "new work is saved in a fresh file");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void JsonStore_RoundTripsEveryField()
    {
        var directory = TempDirectory();
        try
        {
            var path = Path.Combine(directory, "writing-history.json");
            var book = new WritingHistoryBook(new JsonWritingHistoryStore(path), () => Now);
            book.RecordCheck(WritingPracticeMode.Combine, Prompt() with { FocusTopicKey = "present-simple" }, "She go to work.", Feedback(40, TenseIssue));
            book.RecordQuiz("present-simple", 2, 3);
            book.RecordDrill(new GrammarDrillResult(Now, "present-simple", 4, 5));
            book.ChooseRoadmapStage("tenses");

            var loaded = new JsonWritingHistoryStore(path).Load();

            Assert.AreEqual("present-simple", loaded.Mistakes.Single().TopicKey);
            Assert.AreEqual(new WritingCheckRecord(Now, WritingPracticeMode.Combine, WritingLevel.Easy, 40) { FocusTopicKey = "present-simple" }, loaded.Checks.Single());
            Assert.AreEqual(new GrammarQuizResult(Now, "present-simple", 2, 3), loaded.Quizzes.Single());
            Assert.AreEqual(new GrammarDrillResult(Now, "present-simple", 4, 5), loaded.Drills.Single());
            Assert.AreEqual("tenses", loaded.RoadmapStageKey);
            Assert.AreEqual(new DateOnly(2026, 10, 16), loaded.ActiveDays.Single());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void JsonStore_ReadsNumbersSavedAsText()
    {
        var directory = TempDirectory();
        try
        {
            var path = Path.Combine(directory, "writing-history.json");
            File.WriteAllText(path, "{ \"Version\": \"1\", \"Checks\": [ { \"At\": \"2026-10-16T09:00:00+07:00\", \"Mode\": 0, \"Level\": 0, \"Score\": \"77\" } ] }");

            var loaded = new JsonWritingHistoryStore(path).Load();

            Assert.AreEqual(77, loaded.Checks.Single().Score);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
