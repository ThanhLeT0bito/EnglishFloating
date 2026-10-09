using System.IO;
using System.Text.Json;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

/// <summary>Retype, dictation, mistake notebook and weak-point practice in <see cref="WritingSession"/>.</summary>
[TestClass]
public sealed class WritingPracticeFlowTests
{
    private static readonly string[] Topics = ["Daily life"];
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.FromHours(7));
    private const string Reference = "She has lived here since 2019.";

    private static WritingFeedback FeedbackWith(params WritingIssue[] issues) =>
        new(issues.Length == 0 ? 100 : 70, issues.Length == 0 ? WritingVerdict.Correct : WritingVerdict.Almost, Reference, null, "Ok.", issues);

    private static WritingIssue TenseIssue => new(WritingIssueType.Grammar, "lives", "has lived", "Use present perfect.") { Tag = WritingSkillTag.Tense };

    private static (WritingSession Session, StubCoach Coach, WritingHistoryBook Book, InMemoryWritingHistoryStore Store) Create(WritingFeedback? feedback = null)
    {
        var coach = new StubCoach { Feedback = feedback ?? FeedbackWith(TenseIssue) };
        var store = new InMemoryWritingHistoryStore();
        var book = new WritingHistoryBook(store, () => Now);
        var session = new WritingSession(coach) { History = book };
        return (session, coach, book, store);
    }

    [TestMethod]
    public async Task CheckWithMistakes_StartsRetypeOfCorrectedSentence()
    {
        var (session, _, _, _) = Create();
        using var _session = session;
        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);

        await session.CheckAsync("She lives here since 2019.", WritingExplanationLanguage.English);

        Assert.IsTrue(session.IsRetyping);
        Assert.AreEqual(Reference, session.RetypeTarget);
        Assert.AreEqual(WritingEnterAction.Retype, session.EnterActionFor("anything"));
    }

    [TestMethod]
    public async Task Retype_WrongWordIsReportedThenMatchFinishes()
    {
        var (session, _, _, _) = Create();
        using var _session = session;
        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);
        await session.CheckAsync("She lives here since 2019.", WritingExplanationLanguage.English);
        var changes = 0;
        session.StateChanged += () => changes++;

        var wrong = session.SubmitRetype("She have lived here since 2019");
        Assert.IsFalse(wrong!.IsMatch);
        Assert.AreEqual("has", wrong.Expected);
        Assert.IsTrue(session.IsRetyping);

        var right = session.SubmitRetype("she has lived here since 2019");
        Assert.IsTrue(right!.IsMatch);
        Assert.IsFalse(session.IsRetyping);
        Assert.IsTrue(session.RetypeDone);
        Assert.AreEqual(2, changes);
        Assert.AreEqual(WritingEnterAction.Next, session.EnterActionFor("she has lived here since 2019"));
        Assert.AreEqual(WritingEnterAction.Check, session.EnterActionFor("She has been living here since 2019."));
        Assert.IsNull(session.SubmitRetype("again"));
    }

    [TestMethod]
    public async Task SkipRetype_EndsRetypeStep()
    {
        var (session, _, _, _) = Create();
        using var _session = session;
        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);
        await session.CheckAsync("She lives here since 2019.", WritingExplanationLanguage.English);

        session.SkipRetype();

        Assert.IsFalse(session.IsRetyping);
        Assert.IsTrue(session.RetypeSkipped);
        Assert.AreEqual(WritingEnterAction.Next, session.EnterActionFor("She lives here since 2019."));
    }

    [TestMethod]
    public async Task NoRetype_WhenDisabledOrAnswerAlreadyCorrect()
    {
        var (session, _, _, _) = Create();
        using var _session = session;
        session.RetypeEnabled = false;
        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);
        await session.CheckAsync("She lives here since 2019.", WritingExplanationLanguage.English);
        Assert.IsFalse(session.IsRetyping);

        var (other, _, _, _) = Create(FeedbackWith(new WritingIssue(WritingIssueType.Punctuation, "2019", "2019.", "Add a period.")));
        using var _other = other;
        await other.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);
        await other.CheckAsync("she has lived here since 2019", WritingExplanationLanguage.English);
        Assert.IsFalse(other.IsRetyping);
        Assert.AreEqual(WritingEnterAction.Next, other.EnterActionFor("she has lived here since 2019"));
    }

    [TestMethod]
    public async Task NewSentence_ClearsRetypeState()
    {
        var (session, _, _, _) = Create();
        using var _session = session;
        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);
        await session.CheckAsync("She lives here since 2019.", WritingExplanationLanguage.English);

        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);

        Assert.IsNull(session.RetypeTarget);
        Assert.IsFalse(session.IsRetyping);
        Assert.AreEqual(WritingEnterAction.Check, session.EnterActionFor("x"));
    }

    [TestMethod]
    public async Task Dictation_ScoresLocallyWithoutCallingTheAi()
    {
        var (session, coach, book, _) = Create();
        using var _session = session;
        session.Mode = WritingPracticeMode.Dictation;
        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);
        var states = new List<WritingSessionState>();
        session.StateChanged += () => states.Add(session.State);

        await session.CheckAsync("She has livd here 2019", WritingExplanationLanguage.English);

        Assert.AreEqual(0, coach.CheckCalls);
        CollectionAssert.AreEqual(new[] { WritingSessionState.Reviewed }, states.ToArray());
        Assert.AreEqual(WritingPracticeMode.Dictation, session.FeedbackMode);
        Assert.AreEqual(4, session.Dictation!.Correct);
        Assert.AreEqual(6, session.Dictation.Total);
        Assert.AreEqual(Reference, session.Feedback!.CorrectedSentence);
        Assert.IsTrue(session.IsRetyping);
        Assert.AreEqual(2, book.Current.Mistakes.Count);
        Assert.IsTrue(book.Current.Mistakes.All(mistake => mistake.Mode == WritingPracticeMode.Dictation && mistake.Source == Reference));
    }

    [TestMethod]
    public async Task Dictation_PerfectAnswerNeedsNoRetype()
    {
        var (session, _, book, _) = Create();
        using var _session = session;
        session.Mode = WritingPracticeMode.Dictation;
        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);

        await session.CheckAsync("she has lived here since 2019", WritingExplanationLanguage.English);

        Assert.AreEqual(100, session.Feedback!.Score);
        Assert.AreEqual(WritingVerdict.Correct, session.Feedback.Verdict);
        Assert.IsFalse(session.IsRetyping);
        Assert.AreEqual(0, book.Current.Mistakes.Count);
    }

    [TestMethod]
    public async Task Check_RecordsMistakesInTheNotebook()
    {
        var (session, _, book, store) = Create();
        using var _session = session;
        var changed = 0;
        book.Changed += () => changed++;
        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);

        await session.CheckAsync("She lives here since 2019.", WritingExplanationLanguage.English);

        Assert.AreEqual(1, book.Current.Mistakes.Count);
        var mistake = book.Current.Mistakes[0];
        Assert.AreEqual(WritingSkillTag.Tense, mistake.Tag);
        Assert.AreEqual("Câu 1", mistake.Source);
        Assert.AreEqual("She lives here since 2019.", mistake.Answer);
        Assert.AreEqual(Now, mistake.At);
        Assert.AreEqual(1, changed);
        Assert.AreEqual(1, store.SaveCount);
    }

    [TestMethod]
    public async Task FocusTags_ArePassedToTheCoachAndReviewedAfterTheCheck()
    {
        var (session, coach, book, _) = Create(FeedbackWith());
        using var _session = session;

        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English, [WritingSkillTag.Tense, WritingSkillTag.Article]);
        await session.CheckAsync(Reference, WritingExplanationLanguage.English);

        CollectionAssert.AreEqual(new[] { WritingSkillTag.Tense, WritingSkillTag.Article }, coach.Requests[0].FocusTags.ToArray());
        Assert.AreEqual(2, book.Current.Reviews.Count);
        Assert.IsTrue(book.Current.Reviews.All(review => review.Passed));
    }

    [TestMethod]
    public async Task Prefetch_IsReusedOnlyForTheSameFocus()
    {
        var coach = new StubCoach { Feedback = FeedbackWith() };
        using var session = new WritingSession(coach, prefetchNext: true);

        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English, [WritingSkillTag.Tense]);
        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English, [WritingSkillTag.Tense]);
        Assert.IsTrue(session.PromptWasPrefetched);

        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);
        Assert.IsFalse(session.PromptWasPrefetched);
        Assert.AreEqual(0, coach.Requests[^1].FocusTags.Count);
    }

    [TestMethod]
    public void HistoryBook_ExposesWeakPointsAndClears()
    {
        var store = new InMemoryWritingHistoryStore();
        var book = new WritingHistoryBook(store, () => Now);
        for (var i = 0; i < 5; i++)
        {
            var prompt = new WritingPrompt($"Câu {i}", Reference, "Daily life", WritingLevel.Medium, WritingTips.Empty);
            book.RecordCheck(WritingPracticeMode.Translate, prompt, "She lives here.", FeedbackWith(TenseIssue));
        }

        Assert.IsTrue(book.CanPracticeWeakPoints);
        CollectionAssert.AreEqual(new[] { WritingSkillTag.Tense }, book.WeakTags().ToArray());
        Assert.AreEqual(5, book.RecentMistakeCount);

        book.Clear();
        Assert.AreEqual(0, book.Current.Mistakes.Count);
        Assert.IsFalse(book.CanPracticeWeakPoints);
    }

    [TestMethod]
    public void JsonStore_RoundTripsAndToleratesBrokenFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "writing-history-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "writing-history.json");
        try
        {
            var store = new JsonWritingHistoryStore(path);
            Assert.AreEqual(0, store.Load().Mistakes.Count);

            var book = new WritingHistoryBook(store, () => Now);
            var prompt = new WritingPrompt("Câu", Reference, "Daily life", WritingLevel.Medium, WritingTips.Empty) { FocusTags = [WritingSkillTag.Tense] };
            book.RecordCheck(WritingPracticeMode.Translate, prompt, "She lives here.", FeedbackWith(TenseIssue));

            var loaded = new JsonWritingHistoryStore(path).Load();
            Assert.AreEqual(1, loaded.Mistakes.Count);
            Assert.AreEqual(WritingSkillTag.Tense, loaded.Mistakes[0].Tag);
            Assert.AreEqual(Now, loaded.Mistakes[0].At);
            Assert.AreEqual(1, loaded.Reviews.Count);
            Assert.IsFalse(loaded.Reviews[0].Passed);

            File.WriteAllText(path, "{ not json");
            Assert.AreEqual(0, new JsonWritingHistoryStore(path).Load().Mistakes.Count);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Presentation_RetypeMessagesAndSpeeds()
    {
        Assert.AreEqual("Word 2: expected \"has\", you typed \"have\".", WritingPresentation.RetypeMessage(new RetypeResult(false, 1, "has", "have")));
        Assert.AreEqual("Word 4: \"tea\" is missing.", WritingPresentation.RetypeMessage(new RetypeResult(false, 3, "tea", null)));
        StringAssert.Contains(WritingPresentation.RetypeMessage(new RetypeResult(false, 3, null, "a")), "extra");
        Assert.AreEqual("", WritingPresentation.RetypeMessage(null, skipped: true));
        Assert.AreEqual(0.8, WritingPresentation.SlowSpeed(1.0));
        Assert.AreEqual(0.64, WritingPresentation.SlowSpeed(0.8));
        Assert.AreEqual(0.8, WritingPresentation.SlowSpeed(double.NaN));
        Assert.AreEqual("Submit", WritingPresentation.CheckButtonText(WritingSessionState.Reviewed, isRetyping: true));
        Assert.AreEqual("Check again", WritingPresentation.CheckButtonText(WritingSessionState.Reviewed, isRetyping: false));
        StringAssert.Contains(WritingPresentation.AnswerPlaceholder(WritingPracticeMode.Dictation, false), "hear");
    }

    [TestMethod]
    public void Presentation_WeakPointsStatsAndDates()
    {
        Assert.AreEqual("\"go\" → \"went\" ×3", WritingPresentation.MistakeGroupText(new WritingMistakeGroup(
            new WritingMistake(Now, WritingPracticeMode.Translate, WritingIssueType.Grammar, WritingSkillTag.Tense, "go", "went", "", "", "", ""), 3)));
        Assert.AreEqual("Practice my weak points: Tense, Articles",
            WritingPresentation.WeakPointsText(true, [WritingSkillTag.Tense, WritingSkillTag.Article], 9));
        Assert.AreEqual("Practice my weak points (after 5 mistakes · 2 so far)", WritingPresentation.WeakPointsText(false, [], 2));
        Assert.AreEqual("Tense · 6 · ✓2", WritingPresentation.TagStatText(new WritingTagStat(WritingSkillTag.Tense, 6, 2, 4, Now)));
        Assert.AreEqual("today", WritingPresentation.RelativeDay(Now.AddHours(-2), Now));
        Assert.AreEqual("yesterday", WritingPresentation.RelativeDay(Now.AddDays(-1), Now));
        Assert.AreEqual("3 days ago", WritingPresentation.RelativeDay(Now.AddDays(-3), Now));
        Assert.AreEqual("Dictation · Mixed topics · Easy · Explain in English · Weak points",
            WritingPresentation.SetupSummary([], WritingLevel.Easy, WritingExplanationLanguage.English, WritingPracticeMode.Dictation, true));
        var prompt = new WritingPrompt("Câu", Reference, "Daily life", WritingLevel.Hard, WritingTips.Empty) { FocusTags = [WritingSkillTag.Tense] };
        Assert.AreEqual("Daily life · Hard · Focus: Tense", WritingPresentation.PromptMeta(prompt));
    }

    private sealed class StubCoach : IWritingCoach
    {
        private int _counter;

        public WritingFeedback Feedback { get; set; } = FeedbackWith();
        public List<WritingRequest> Requests { get; } = [];
        public int CheckCalls { get; private set; }

        public Task<WritingPrompt> GenerateAsync(WritingRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            _counter++;
            return Task.FromResult(new WritingPrompt($"Câu {_counter}", Reference, "Daily life", request.Level, WritingTips.Empty)
            {
                FocusTags = request.FocusTags
            });
        }

        public Task<WritingFeedback> CheckAsync(WritingPrompt prompt, string translation, WritingExplanationLanguage language, CancellationToken cancellationToken = default)
        {
            CheckCalls++;
            return Task.FromResult(Feedback);
        }
    }
}
