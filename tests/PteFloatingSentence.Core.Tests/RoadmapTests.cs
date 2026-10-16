namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class RoadmapTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.FromHours(7));
    private static readonly RoadmapStage Stage1 = WritingRoadmap.All[0];

    private static WritingHistory Checks(WritingHistory history, WritingPracticeMode mode, WritingLevel level, int score, int count, DateTimeOffset? at = null)
    {
        for (var i = 0; i < count; i++)
            history = WritingHistoryRules.RecordCheckScore(history, new WritingCheckRecord(at ?? Now, mode, level, score));
        return history;
    }

    private static WritingHistory MasterStage(WritingHistory history, RoadmapStage stage, bool sentences = true)
    {
        foreach (var key in stage.TopicKeys)
        {
            history = WritingHistoryRules.RecordQuiz(history, new GrammarQuizResult(Now.AddHours(-2), key, 3, 3));
            history = GrammarProgressRules.RecordDrill(history, new GrammarDrillResult(Now.AddHours(-1), key, 5, 5));
        }

        return sentences ? Checks(history, stage.Practices[0].Mode, stage.Practices[0].Level, 85, RoadmapRules.RequiredSentences) : history;
    }

    [TestMethod]
    public void Roadmap_CoversEveryGrammarTopicExactlyOnce()
    {
        var keys = WritingRoadmap.All.SelectMany(stage => stage.TopicKeys).ToList();

        Assert.AreEqual(GrammarTopics.All.Count, keys.Count);
        CollectionAssert.AreEquivalent(GrammarTopics.All.Select(topic => topic.Key).ToList(), keys);
        Assert.AreEqual(5, WritingRoadmap.All.Count(stage => stage.IsAvailable));
        Assert.IsFalse(WritingRoadmap.All[^1].IsAvailable);
        Assert.AreEqual("tenses", WritingRoadmap.StageOf("present-perfect")!.Key);
        CollectionAssert.AreEqual(Enumerable.Range(1, WritingRoadmap.All.Count).ToArray(), WritingRoadmap.All.Select(stage => stage.Number).ToArray());
    }

    [TestMethod]
    public void NewLearner_StartsAtStageOneWithZeroProgress()
    {
        var progress = RoadmapRules.Progress(WritingHistory.Empty, Stage1, Now);

        Assert.AreEqual(0, progress.Percent);
        Assert.AreEqual(0, progress.Sentences);
        Assert.IsNull(progress.RecentAverage);
        Assert.AreEqual(4, progress.DrillsNeeded);
        Assert.AreEqual("simple-sentences", RoadmapRules.CurrentStage(WritingHistory.Empty, Now).Key);

        var overview = RoadmapRules.Overview(WritingHistory.Empty, Now);
        Assert.AreEqual(RoadmapStageState.Current, overview[0].State);
        Assert.AreEqual(RoadmapStageState.Upcoming, overview[1].State);
        Assert.AreEqual(RoadmapStageState.ComingSoon, overview[^1].State);
    }

    [TestMethod]
    public void Sentences_CountOnlyTheStagesExercisesAndDrills()
    {
        var history = Checks(WritingHistory.Empty, WritingPracticeMode.Translate, WritingLevel.Easy, 80, 3);
        history = Checks(history, WritingPracticeMode.Combine, WritingLevel.Hard, 40, 5);
        history = WritingHistoryRules.RecordCheckScore(history, new WritingCheckRecord(Now, WritingPracticeMode.Translate, WritingLevel.Hard, 60) { FocusTopicKey = "articles" });

        var progress = RoadmapRules.Progress(history, Stage1, Now);

        Assert.AreEqual(4, progress.Sentences);
        Assert.AreEqual(75, progress.RecentAverage);
    }

    [TestMethod]
    public void RecentAverage_UsesTheLastFifteenSentences()
    {
        var history = Checks(WritingHistory.Empty, WritingPracticeMode.Translate, WritingLevel.Easy, 20, 10);
        history = Checks(history, WritingPracticeMode.Translate, WritingLevel.Easy, 90, 15);

        var progress = RoadmapRules.Progress(history, Stage1, Now);

        Assert.AreEqual(25, progress.Sentences);
        Assert.AreEqual(90, progress.RecentAverage);
        Assert.IsTrue(progress.SentencesComplete);
    }

    [TestMethod]
    public void Stage_CompletesWithLessonsDrillsAndSentencesThenTheNextIsCurrent()
    {
        var history = MasterStage(WritingHistory.Empty, Stage1);

        var progress = RoadmapRules.Progress(history, Stage1, Now);
        Assert.IsTrue(progress.IsComplete);
        Assert.AreEqual(100, progress.Percent);
        Assert.AreEqual("tenses", RoadmapRules.CurrentStage(history, Now).Key);
        Assert.AreEqual(RoadmapStageState.Done, RoadmapRules.Overview(history, Now)[0].State);
    }

    [TestMethod]
    public void Stage_IsNotCompleteWhileATopicNeedsReview()
    {
        var history = MasterStage(WritingHistory.Empty, Stage1);
        var issue = new WritingIssue(WritingIssueType.Grammar, "go", "goes", "") { TopicKey = "present-simple" };
        for (var i = 0; i < 3; i++)
            history = WritingHistoryRules.RecordFeedback(history, WritingPracticeMode.Translate, $"Câu {i}", "x", new WritingFeedback(60, WritingVerdict.Almost, "y", null, "", [issue]), Now);

        var progress = RoadmapRules.Progress(history, Stage1, Now);

        Assert.IsFalse(progress.IsComplete);
        Assert.AreEqual("present-simple", progress.NeedsReview.Single().Key);
        Assert.IsFalse(progress.Topics[0].Mastered);
        Assert.IsTrue(progress.Percent < 100);
    }

    [TestMethod]
    public void Percent_GrowsWithEachPart()
    {
        var history = WritingHistoryRules.RecordQuiz(WritingHistory.Empty, new GrammarQuizResult(Now, "present-simple", 2, 3));
        history = WritingHistoryRules.RecordQuiz(history, new GrammarQuizResult(Now, "past-simple", 1, 3));

        var progress = RoadmapRules.Progress(history, Stage1, Now);

        Assert.AreEqual(1, progress.LessonsDone, "a quick check needs two thirds right");
        Assert.AreEqual(4, progress.Percent);
    }

    [TestMethod]
    public void ChosenStage_IsCurrentUntilCleared()
    {
        var history = RoadmapRules.ChooseStage(WritingHistory.Empty, "academic-sentences");
        Assert.AreEqual("academic-sentences", RoadmapRules.CurrentStage(history, Now).Key);
        Assert.AreEqual(RoadmapStageState.Current, RoadmapRules.Overview(history, Now)[3].State);

        Assert.IsNull(RoadmapRules.ChooseStage(history, "paragraphs-pte").RoadmapStageKey, "a coming-soon stage cannot be chosen");
        Assert.IsNull(RoadmapRules.ChooseStage(history, null).RoadmapStageKey);
    }

    [TestMethod]
    public void TodayPlan_NewLearnerLearnsDrillsAndAppliesTheFirstTopic()
    {
        var progress = RoadmapRules.Progress(WritingHistory.Empty, Stage1, Now);

        var plan = RoadmapRules.TodayPlan(WritingHistory.Empty, progress, Now);

        CollectionAssert.AreEqual(new[] { RoadmapStepKind.Learn, RoadmapStepKind.Drill, RoadmapStepKind.Apply }, plan.Select(step => step.Kind).ToArray());
        Assert.AreEqual("present-simple", plan[0].Topic!.Key);
        Assert.AreEqual("present-simple", plan[1].Topic!.Key);
        Assert.AreEqual(GrammarProgressRules.DrillLength, plan[1].Sentences);
        Assert.AreEqual(RoadmapRules.ApplySentences, plan[2].Sentences);
        Assert.IsTrue(Stage1.Practices.Contains(plan[2].Practice));
    }

    [TestMethod]
    public void TodayPlan_ReviewsAWeakTopicFirstAndDrillsInAWritingMode()
    {
        var history = WritingHistoryRules.RecordQuiz(WritingHistory.Empty, new GrammarQuizResult(Now, "present-continuous", 3, 3));
        var issue = new WritingIssue(WritingIssueType.Grammar, "a", "the", "") { TopicKey = "articles" };
        for (var i = 0; i < 3; i++)
            history = WritingHistoryRules.RecordFeedback(history, WritingPracticeMode.Translate, $"Câu {i}", "x", new WritingFeedback(60, WritingVerdict.Almost, "y", null, "", [issue]), Now);
        var stage2 = WritingRoadmap.Find("tenses")!;

        var plan = RoadmapRules.TodayPlan(history, RoadmapRules.Progress(history, stage2, Now), Now);

        Assert.AreEqual(RoadmapStepKind.Review, plan[0].Kind);
        Assert.AreEqual("articles", plan[0].Topic!.Key);
        Assert.AreEqual(RoadmapRules.ReviewSentences, plan[0].Sentences);
        Assert.AreEqual(RoadmapStepKind.Learn, plan[1].Kind);
        Assert.AreEqual("present-perfect", plan[1].Topic!.Key, "present continuous is already learned");
        Assert.AreEqual(WritingPracticeMode.Translate, plan[2].Practice.Mode);
    }

    [TestMethod]
    public void TodayPlan_AfterAllLessonsDrillsTheFirstTopicNotMastered()
    {
        var history = WritingHistory.Empty;
        foreach (var key in Stage1.TopicKeys)
            history = WritingHistoryRules.RecordQuiz(history, new GrammarQuizResult(Now, key, 3, 3));
        history = GrammarProgressRules.RecordDrill(history, new GrammarDrillResult(Now, "present-simple", 5, 5));

        var plan = RoadmapRules.TodayPlan(history, RoadmapRules.Progress(history, Stage1, Now), Now);

        CollectionAssert.AreEqual(new[] { RoadmapStepKind.Drill, RoadmapStepKind.Apply }, plan.Select(step => step.Kind).ToArray());
        Assert.AreEqual("past-simple", plan[0].Topic!.Key);
    }

    [TestMethod]
    public void Streak_CountsConsecutiveDaysEndingTodayOrYesterday()
    {
        var history = Checks(WritingHistory.Empty, WritingPracticeMode.Translate, WritingLevel.Easy, 80, 1, Now.AddDays(-1));
        history = Checks(history, WritingPracticeMode.Translate, WritingLevel.Easy, 80, 1, Now.AddDays(-2));
        history = Checks(history, WritingPracticeMode.Translate, WritingLevel.Easy, 80, 1, Now.AddDays(-4));
        Assert.AreEqual(2, RoadmapRules.Streak(history, Now), "nothing today yet: the streak still counts until yesterday");

        history = Checks(history, WritingPracticeMode.Translate, WritingLevel.Easy, 80, 2, Now);
        Assert.AreEqual(3, RoadmapRules.Streak(history, Now));
        Assert.AreEqual(2, RoadmapRules.ChecksToday(history, Now));
        Assert.AreEqual(0, RoadmapRules.Streak(WritingHistory.Empty, Now));
    }

    [TestMethod]
    public void Normalize_KeepsLogsAndAValidStage()
    {
        var history = new WritingHistory
        {
            Checks = [new WritingCheckRecord(Now, WritingPracticeMode.Translate, WritingLevel.Easy, 80) { FocusTopicKey = "nope" }, new WritingCheckRecord(Now, WritingPracticeMode.Translate, WritingLevel.Easy, 140), null!],
            Quizzes = [new GrammarQuizResult(Now, "articles", 2, 3), new GrammarQuizResult(Now, "articles", 4, 3)],
            RoadmapStageKey = "tenses"
        };

        var normalized = WritingHistoryRules.Normalize(history);

        Assert.AreEqual(1, normalized.Checks.Count);
        Assert.IsNull(normalized.Checks[0].FocusTopicKey);
        Assert.AreEqual(1, normalized.Quizzes.Count);
        Assert.AreEqual("tenses", normalized.RoadmapStageKey);
        Assert.IsNull(WritingHistoryRules.Normalize(history with { RoadmapStageKey = "paragraphs-pte" }).RoadmapStageKey);
    }
}
