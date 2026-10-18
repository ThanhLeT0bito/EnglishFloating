namespace PteFloatingSentence.Core.Tests;

/// <summary>Review fixes: progress that survives the capped logs, honest drill and review status, tolerant topic keys.</summary>
[TestClass]
public sealed class ProgressRobustnessTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.FromHours(7));
    private static readonly RoadmapStage Stage1 = WritingRoadmap.All[0];

    private static WritingIssue TopicIssue(string topicKey) =>
        new(WritingIssueType.Grammar, "a", "the", "Use the.") { Tag = WritingSkillTag.Article, TopicKey = topicKey };

    private static WritingHistory Mistakes(WritingHistory history, string topicKey, int count, DateTimeOffset at)
    {
        for (var i = 0; i < count; i++)
            history = WritingHistoryRules.RecordFeedback(history, WritingPracticeMode.Translate, $"Câu {topicKey} {at:O} {i}", "x",
                new WritingFeedback(60, WritingVerdict.Almost, "y", null, "", [TopicIssue(topicKey)]), at);
        return history;
    }

    private static WritingHistory Checks(WritingHistory history, RoadmapPractice practice, int score, int count, DateTimeOffset at)
    {
        for (var i = 0; i < count; i++)
            history = WritingHistoryRules.RecordCheckScore(history, new WritingCheckRecord(at, practice.Mode, practice.Level, score));
        return history;
    }

    private static WritingHistory MasterStage1(WritingHistory history)
    {
        foreach (var key in Stage1.TopicKeys)
        {
            history = WritingHistoryRules.RecordQuiz(history, new GrammarQuizResult(Now.AddHours(-2), key, 3, 3));
            history = GrammarProgressRules.RecordDrill(history, new GrammarDrillResult(Now.AddHours(-1), key, 5, 5));
        }

        return Checks(history, Stage1.Practices[0], 85, RoadmapRules.RequiredSentences, Now);
    }

    [TestMethod]
    public void FinishedStage_StaysDoneWhenItsSentencesLeaveTheLog()
    {
        var history = RoadmapRules.RecordCompletions(MasterStage1(WritingHistory.Empty), Now);
        Assert.AreEqual("simple-sentences", history.CompletedStages.Single().StageKey);
        Assert.AreSame(history, RoadmapRules.RecordCompletions(history, Now), "a stage is saved once");

        // A thousand later sentences in stage 2 push stage 1's sentences out of the capped log.
        history = Checks(history, new RoadmapPractice(WritingPracticeMode.Translate, WritingLevel.Medium), 90, WritingHistoryRules.MaxChecks, Now.AddDays(1));
        var progress = RoadmapRules.Progress(history, Stage1, Now.AddDays(1));

        Assert.AreEqual(0, progress.Sentences);
        Assert.IsFalse(progress.MeetsTargets);
        Assert.IsTrue(progress.IsComplete);
        Assert.AreEqual(100, progress.Percent);
        Assert.AreEqual("tenses", RoadmapRules.CurrentStage(history, Now.AddDays(1)).Key);
        Assert.AreEqual(RoadmapStageState.Done, RoadmapRules.Overview(history, Now.AddDays(1))[0].State);
    }

    [TestMethod]
    public void Streak_UsesTheSavedDaysAndTheLocalDateOfEachCheck()
    {
        // Saturday 00:30 in summer time (+02:00); the clocks go back on Sunday, so "now" is in winter time (+01:00).
        var day1 = new DateTimeOffset(2026, 10, 24, 0, 30, 0, TimeSpan.FromHours(2));
        var day2 = new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.FromHours(1));
        var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.FromHours(1));
        var history = Checks(WritingHistory.Empty, Stage1.Practices[0], 80, 1, day1);
        history = Checks(history, Stage1.Practices[0], 80, 1, day2);

        Assert.AreEqual(2, RoadmapRules.Streak(history, now), "Saturday's check stays on Saturday");
        Assert.AreEqual(1, RoadmapRules.ChecksToday(history, now));

        // The days stay when the checks themselves leave the capped log.
        var trimmed = history with { Checks = [] };
        Assert.AreEqual(2, RoadmapRules.Streak(trimmed, now));
        CollectionAssert.AreEqual(new[] { new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 25) }, history.ActiveDays.ToArray());
    }

    [TestMethod]
    public void FailedDrill_DoesNotClearNeedsReviewAndIsNotMastery()
    {
        var articles = GrammarTopics.Find("articles")!;
        var history = GrammarProgressRules.RecordDrill(WritingHistory.Empty, new GrammarDrillResult(Now.AddDays(-5), "articles", 5, 5));
        history = Mistakes(history, "articles", 8, Now.AddDays(-2));
        Assert.AreEqual(GrammarTopicStatus.NeedsReview, GrammarProgressRules.StatFor(history, articles, Now).Status);

        history = GrammarProgressRules.RecordDrill(history, new GrammarDrillResult(Now.AddDays(-1), "articles", 0, 5));

        var stat = GrammarProgressRules.StatFor(history, articles, Now);
        Assert.AreEqual(GrammarTopicStatus.NeedsReview, stat.Status, "a failed drill does not reset the count");
        Assert.AreEqual(8, stat.MistakesSinceDrill);
        var topic = RoadmapRules.Progress(history, Stage1, Now).Topics.Single(item => item.Topic.Key == "articles");
        Assert.IsFalse(topic.DrillPassed, "the latest drill failed");
        Assert.IsFalse(topic.Mastered);

        history = GrammarProgressRules.RecordDrill(history, new GrammarDrillResult(Now, "articles", 5, 5));
        Assert.AreEqual(GrammarTopicStatus.Good, GrammarProgressRules.StatFor(history, articles, Now).Status);
    }

    [TestMethod]
    public void PassesTopic_NeedsADecentScoreAndReadsLooseTopicKeys()
    {
        var articles = GrammarTopics.Find("articles")!;
        WritingFeedback Feedback(int score, params WritingIssue[] issues) => new(score, WritingVerdict.Almost, "y", null, "", issues);

        Assert.IsTrue(GrammarProgressRules.PassesTopic(Feedback(70), articles));
        Assert.IsFalse(GrammarProgressRules.PassesTopic(Feedback(15), articles), "a poor answer whose mistake was not tagged fails");
        Assert.IsFalse(GrammarProgressRules.PassesTopic(Feedback(90, TopicIssue("articles (Articles: a / an / the)")), articles));
        Assert.IsTrue(GrammarProgressRules.PassesTopic(Feedback(70, TopicIssue("past-simple")), articles));
    }

    [TestMethod]
    public void Find_ToleratesWhatTheAiSendsBack()
    {
        Assert.AreEqual("articles", GrammarTopics.Find("articles (Articles: a / an / the)")!.Key);
        Assert.AreEqual("present-simple", GrammarTopics.Find(" PRESENT_SIMPLE ")!.Key);
        Assert.AreEqual("present-simple", GrammarTopics.Find("\"present-simple\"")!.Key);
        Assert.AreEqual("present-simple", GrammarTopics.Find("Present simple")!.Key);
        Assert.AreEqual("gerund-infinitive", GrammarTopics.Find("Gerund or infinitive (V-ing / to V)")!.Key);
        Assert.AreEqual("prepositions", GrammarTopics.Find("Prepositions of time and place")!.Key);
        Assert.AreEqual("word-form", GrammarTopics.Find("Word forms (noun, verb, adjective, adverb)")!.Key);
        Assert.IsNull(GrammarTopics.Find("custom:used to"));
        Assert.IsNull(GrammarTopics.Find("past"));
        Assert.IsNull(GrammarTopics.Find("tenses"));
        Assert.IsNull(GrammarTopics.Find("nope"));
        Assert.IsNull(GrammarTopics.Find("  "));
    }

    [TestMethod]
    public void ClearMistakes_KeepsLessonsDrillsAndRoadmapProgress()
    {
        var history = RoadmapRules.RecordCompletions(MasterStage1(Mistakes(WritingHistory.Empty, "articles", 2, Now)), Now);
        history = RoadmapRules.ChooseStage(history, "tenses");

        var cleared = WritingHistoryRules.ClearMistakes(history);

        Assert.AreEqual(0, cleared.Mistakes.Count);
        Assert.AreEqual(0, cleared.Reviews.Count);
        Assert.AreEqual(history.Drills.Count, cleared.Drills.Count);
        Assert.AreEqual(history.Quizzes.Count, cleared.Quizzes.Count);
        Assert.AreEqual(history.Checks.Count, cleared.Checks.Count);
        Assert.AreEqual("tenses", cleared.RoadmapStageKey);
        Assert.AreEqual(1, cleared.CompletedStages.Count);
        Assert.AreEqual(history.ActiveDays.Count, cleared.ActiveDays.Count);
    }

    [TestMethod]
    public void TodayPlan_ReviewsTheStagesOwnTopicEvenWithManyWeakTopics()
    {
        var history = WritingHistory.Empty;
        // Six topics from later stages with many mistakes, and stage 1's articles with fewer.
        foreach (var key in new[] { "passive-voice", "conditionals", "reported-speech", "word-form", "collocations", "inversion" })
            history = Mistakes(history, key, 6, Now);
        history = Mistakes(history, "articles", 3, Now);

        var plan = RoadmapRules.TodayPlan(history, RoadmapRules.Progress(history, Stage1, Now), Now);

        Assert.AreEqual(RoadmapStepKind.Review, plan[0].Kind);
        Assert.AreEqual("articles", plan[0].Topic!.Key);
    }

    [TestMethod]
    public void Normalize_KeepsValidCompletionsAndDays()
    {
        var history = new WritingHistory
        {
            CompletedStages = [new("simple-sentences", Now), new("simple-sentences", Now.AddDays(1)), new("paragraphs-pte", Now), new("nope", Now), null!],
            ActiveDays = [new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)]
        };

        var normalized = WritingHistoryRules.Normalize(history);

        Assert.AreEqual(Now, normalized.CompletedStages.Single().At);
        CollectionAssert.AreEqual(new[] { new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2) }, normalized.ActiveDays.ToArray());

        var many = new WritingHistory { ActiveDays = Enumerable.Range(0, 500).Select(i => new DateOnly(2025, 1, 1).AddDays(i)).ToList() };
        var capped = WritingHistoryRules.Normalize(many).ActiveDays;
        Assert.AreEqual(WritingHistoryRules.MaxActiveDays, capped.Count);
        Assert.AreEqual(new DateOnly(2025, 1, 1).AddDays(499), capped[^1], "the newest days are kept");
    }
}
