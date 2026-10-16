using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

/// <summary>Today's session, guided runs, the notebook's roadmap log and the roadmap texts. UI-free.</summary>
[TestClass]
public sealed class RoadmapSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.FromHours(7));
    private static readonly GrammarTopic PresentSimple = GrammarTopics.Find("present-simple")!;
    private static readonly GrammarTopic Articles = GrammarTopics.Find("articles")!;
    private static readonly RoadmapPractice TranslateEasy = new(WritingPracticeMode.Translate, WritingLevel.Easy);

    private static WritingPrompt Prompt(int number, string? topicKey = null) =>
        new($"Câu {number}", "She goes to work.", "Daily life", WritingLevel.Easy, WritingTips.Empty) { FocusTopicKey = topicKey };

    private static WritingFeedback Feedback(int score) => new(score, WritingVerdict.Almost, "She goes to work.", null, "Ok.", []);

    private static RoadmapSession Session(params RoadmapStep[] steps) => new(WritingRoadmap.All[0], steps, Now);

    private static RoadmapStep Step(RoadmapStepKind kind, GrammarTopic? topic = null, int sentences = 5) => new(kind, topic, TranslateEasy, sentences, []);

    [TestMethod]
    public void GuidedPractice_CountsEachPromptOnceAndAverages()
    {
        var run = GuidedPractice.ForStep(new RoadmapStep(RoadmapStepKind.Review, Articles, TranslateEasy, 3, [WritingSkillTag.Tense]));
        var first = Prompt(1, "articles");

        Assert.AreEqual($"Review: {Articles.Name}", run.Title);
        Assert.AreEqual(RoadmapStepKind.Review, run.Kind);
        Assert.AreEqual(0, run.FocusTags.Count, "a topic run does not also focus on weak tags");
        Assert.IsFalse(run.Record(first, Feedback(60)));
        Assert.IsFalse(run.Record(first, Feedback(100)), "checking the same sentence again does not count");
        Assert.IsFalse(run.Record(Prompt(2, "articles"), Feedback(81)));
        Assert.IsTrue(run.Record(Prompt(3, "articles"), Feedback(90)));
        Assert.IsTrue(run.IsComplete);
        Assert.AreEqual(77, run.Average);
        Assert.IsFalse(run.Record(Prompt(4, "articles"), Feedback(90)), "a finished run counts nothing more");
        Assert.AreEqual(3, run.Done);
    }

    [TestMethod]
    public void Session_CompletesStepsAndFinishesAfterTheLast()
    {
        var session = Session(Step(RoadmapStepKind.Learn, PresentSimple, 0), Step(RoadmapStepKind.Drill, PresentSimple), Step(RoadmapStepKind.Apply, sentences: 8));
        var changes = 0;
        session.Changed += () => changes++;

        Assert.IsFalse(session.Complete(RoadmapStepKind.Learn, "articles"), "another topic's lesson does not count");
        Assert.IsTrue(session.Complete(RoadmapStepKind.Learn, "present-simple"));
        Assert.IsTrue(session.IsCurrentDone);
        Assert.IsFalse(session.Complete(RoadmapStepKind.Learn, "present-simple"), "a step completes once");

        Assert.IsTrue(session.Advance());
        Assert.AreEqual(RoadmapStepKind.Drill, session.Current.Kind);
        Assert.IsFalse(session.IsCurrentDone);
        Assert.IsTrue(session.Advance(), "a step that is not done can be skipped");
        Assert.IsTrue(session.IsLastStep);
        Assert.IsFalse(session.Advance());
        Assert.IsTrue(session.IsFinished);
        Assert.AreEqual(1, session.StepsDone);
        Assert.IsFalse(session.Complete(RoadmapStepKind.Apply));
        Assert.AreEqual(4, changes);
    }

    [TestMethod]
    public void Session_CountsWorkDoneAheadOfTheCurrentStep()
    {
        var session = Session(Step(RoadmapStepKind.Learn, PresentSimple, 0), Step(RoadmapStepKind.Drill, PresentSimple), Step(RoadmapStepKind.Apply, sentences: 8));

        Assert.IsTrue(session.Complete(RoadmapStepKind.Drill, "present-simple"), "a drill started from the lesson counts for the drill step");
        Assert.IsFalse(session.IsCurrentDone);

        session.Advance();
        Assert.IsTrue(session.IsCurrentDone);
        Assert.ThrowsException<ArgumentException>(() => Session());
    }

    [TestMethod]
    public void Session_SummarizesChecksAndMistakesSinceItStarted()
    {
        var clock = Now;
        var book = new WritingHistoryBook(new InMemoryWritingHistoryStore(), () => clock);
        book.RecordCheck(WritingPracticeMode.Translate, Prompt(0), "She go to work.", Feedback(10));
        var session = new RoadmapSession(WritingRoadmap.All[0], [Step(RoadmapStepKind.Apply, sentences: 2)], Now.AddMinutes(1));
        clock = Now.AddMinutes(2);
        var issue = new WritingIssue(WritingIssueType.Grammar, "go", "goes", "Add -es.") { TopicKey = "present-simple" };
        book.RecordCheck(WritingPracticeMode.Translate, Prompt(1), "She go to work.", new WritingFeedback(70, WritingVerdict.Almost, "She goes to work.", null, "Ok.", [issue]));
        book.RecordCheck(WritingPracticeMode.Translate, Prompt(2), "She goes to work.", Feedback(91));
        session.Complete(RoadmapStepKind.Apply);

        var summary = session.Summarize(book.Current, clock);

        Assert.AreEqual(2, summary.Sentences);
        Assert.AreEqual(81, summary.Average);
        Assert.AreEqual(1, summary.NewMistakes);
        Assert.AreEqual(1, summary.StepsDone);
        Assert.AreEqual(1, summary.Streak);
        Assert.AreEqual("simple-sentences", summary.Progress.Stage.Key);
        StringAssert.StartsWith(RoadmapPresentation.SummaryText(summary), "Session done: 1/1 steps · 2 sentences · average 81 · 1 new mistake · Stage 1 is");
    }

    [TestMethod]
    public void HistoryBook_LogsChecksQuizzesAndTheChosenStage()
    {
        var book = new WritingHistoryBook(new InMemoryWritingHistoryStore(), () => Now);
        var changes = 0;
        book.Changed += () => changes++;

        book.RecordCheck(WritingPracticeMode.Combine, Prompt(1, "present-simple") with { Level = WritingLevel.Hard }, "x", Feedback(66));
        book.RecordQuiz("present-simple", 1, 1);
        book.ChooseRoadmapStage("tenses");

        var check = book.Current.Checks.Single();
        Assert.AreEqual(new WritingCheckRecord(Now, WritingPracticeMode.Combine, WritingLevel.Hard, 66) { FocusTopicKey = "present-simple" }, check);
        Assert.AreEqual(new GrammarQuizResult(Now, "present-simple", 1, 1), book.Current.Quizzes.Single());
        Assert.AreEqual("tenses", book.CurrentRoadmapStage().Key);
        Assert.AreEqual(1, book.Streak);
        Assert.AreEqual(1, book.ChecksToday);
        Assert.AreEqual(RoadmapStepKind.Learn, book.TodayPlan()[0].Kind);
        Assert.AreEqual(3, changes);

        book.ChooseRoadmapStage(null);
        Assert.AreEqual("simple-sentences", book.CurrentRoadmapStage().Key);
        Assert.IsTrue(book.RoadmapProgress(WritingRoadmap.All[0]).LessonsDone == 1);
    }

    [TestMethod]
    public void Presentation_PlanBannerAndChecklistTexts()
    {
        var learn = Step(RoadmapStepKind.Learn, PresentSimple, 0);
        var drill = Step(RoadmapStepKind.Drill, PresentSimple);
        var apply = new RoadmapStep(RoadmapStepKind.Apply, null, new RoadmapPractice(WritingPracticeMode.Dictation, WritingLevel.Easy), 8, []);
        var weak = new RoadmapStep(RoadmapStepKind.Review, null, TranslateEasy, 3, [WritingSkillTag.Tense]);

        Assert.AreEqual("Learn: Present simple · lesson + quick check", RoadmapPresentation.StepLine(learn));
        Assert.AreEqual("Drill: Present simple · 5 sentences", RoadmapPresentation.StepLine(drill));
        Assert.AreEqual("Apply: Dictation · Easy · 8 sentences", RoadmapPresentation.StepLine(apply));
        Assert.AreEqual("Review your weak points", RoadmapPresentation.StepTitle(weak));
        Assert.AreEqual("Start today's session (~25 min)", RoadmapPresentation.StartButtonText([learn, drill, apply]));

        var session = new RoadmapSession(WritingRoadmap.All[0], [learn, drill], Now);
        Assert.AreEqual("Today · Step 1 of 2 · Learn: Present simple · read the lesson, then answer the quick check", RoadmapPresentation.BannerText(session));
        Assert.AreEqual("Next step →", RoadmapPresentation.NextButtonText(session));
        session.Complete(RoadmapStepKind.Learn, "present-simple");
        StringAssert.EndsWith(RoadmapPresentation.BannerText(session), "· done ✓");
        session.Advance();
        Assert.AreEqual("Finish", RoadmapPresentation.NextButtonText(session));

        var progress = RoadmapRules.Progress(WritingHistory.Empty, WritingRoadmap.All[0], Now);
        Assert.AreEqual("Lessons + quick check: 0/6", RoadmapPresentation.ChecklistLessons(progress));
        Assert.AreEqual("Drills passed (4/5+): 0/4", RoadmapPresentation.ChecklistDrills(progress));
        Assert.AreEqual("Sentences in this stage: 0/15 · need an average of 75", RoadmapPresentation.ChecklistSentences(progress));
        Assert.AreEqual("No grammar that needs review", RoadmapPresentation.ChecklistReview(progress));
        Assert.AreEqual("0% done", RoadmapPresentation.PercentText(progress));
        Assert.AreEqual("Coming soon", RoadmapPresentation.PercentText(RoadmapRules.Progress(WritingHistory.Empty, WritingRoadmap.All[^1], Now)));
        Assert.AreEqual("#FF64748B", RoadmapPresentation.TopicColor(progress.Topics[0]));
        StringAssert.StartsWith(RoadmapPresentation.TopicTooltip(progress.Topics[0]), "Present simple · lesson – · drill –");
    }

    [TestMethod]
    public void Presentation_StreakGuidedRunAndQuizTexts()
    {
        Assert.AreEqual("Start your streak today", RoadmapPresentation.StreakText(0, 0));
        Assert.AreEqual("🔥 1 day in a row · 1 sentence today", RoadmapPresentation.StreakText(1, 1));
        Assert.AreEqual("🔥 4 days in a row · 6 sentences today", RoadmapPresentation.StreakText(4, 6));

        var apply = GuidedPractice.ForStep(new RoadmapStep(RoadmapStepKind.Apply, null, TranslateEasy, 2, []));
        Assert.AreEqual("Apply: Translate · Easy · sentence 1 of 2", RoadmapPresentation.GuidedProgress(apply), "the exercise is not repeated");
        var practice = new GuidedPractice("Stage 1 practice", TranslateEasy, 8);
        Assert.AreEqual("Stage 1 practice · Translate · Easy · sentence 1 of 8", RoadmapPresentation.GuidedProgress(practice));
        apply.Record(Prompt(1), Feedback(70));
        apply.Record(Prompt(2), Feedback(80));
        Assert.AreEqual("Apply: Translate · Easy: done · average 75.", RoadmapPresentation.GuidedSummary(apply));

        StringAssert.Contains(GrammarPresentation.QuizSummary(2, 3, WritingExplanationLanguage.English), "Lesson learned");
        StringAssert.Contains(GrammarPresentation.QuizSummary(1, 3, WritingExplanationLanguage.English), "need 2/3");
        StringAssert.Contains(GrammarPresentation.QuizSummary(1, 3, WritingExplanationLanguage.Vietnamese), "cần 2/3");
    }
}
