using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public sealed class WritingSessionTests
{
    private static readonly string[] Topics = ["Traffic & transport"];

    private static WritingPrompt PromptFor(string vietnamese, WritingTips? tips = null) =>
        new(vietnamese, "There was a traffic jam this morning.", "Traffic & transport", WritingLevel.Medium,
            tips ?? new WritingTips([new("kẹt xe", "traffic jam", "noun")], "Use the past simple.", "There was"));

    private static WritingFeedback FeedbackWith(int score) =>
        new(score, WritingFeedbackRules.VerdictFor(score), "There was a traffic jam this morning.", null, "Nice.", []);

    [TestMethod]
    public async Task Generate_ThenCheck_MovesThroughStates()
    {
        var coach = new FakeCoach();
        using var session = new WritingSession(coach);
        var states = new List<WritingSessionState>();
        session.StateChanged += () => states.Add(session.State);

        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);
        await session.CheckAsync("  There was traffic jam.  ", WritingExplanationLanguage.Vietnamese);

        CollectionAssert.AreEqual(
            new[] { WritingSessionState.Generating, WritingSessionState.Answering, WritingSessionState.Checking, WritingSessionState.Reviewed },
            states.ToArray());
        Assert.AreEqual("There was traffic jam.", session.LastAnswer);
        Assert.AreEqual("There was traffic jam.", coach.LastAnswer);
        Assert.AreEqual(WritingExplanationLanguage.Vietnamese, coach.LastLanguage);
        Assert.AreEqual(85, session.Feedback!.Score);
        Assert.IsNull(session.Error);
    }

    [TestMethod]
    public async Task Generate_PassesTopicsLevelLanguageAndRecentSentences()
    {
        var coach = new FakeCoach();
        using var session = new WritingSession(coach);

        await session.GenerateAsync(Topics, WritingLevel.Hard, WritingExplanationLanguage.Vietnamese);
        await session.GenerateAsync(Topics, WritingLevel.Hard, WritingExplanationLanguage.Vietnamese);

        var request = coach.Requests[1];
        CollectionAssert.AreEqual(Topics, request.TopicNames.ToArray());
        Assert.AreEqual(WritingLevel.Hard, request.Level);
        Assert.AreEqual(WritingExplanationLanguage.Vietnamese, request.Language);
        CollectionAssert.AreEqual(new[] { "Câu 1" }, request.RecentVietnamese.ToArray());
    }

    [TestMethod]
    public async Task RecentVietnamese_KeepsLastTwentyNewestLast()
    {
        var coach = new FakeCoach();
        using var session = new WritingSession(coach);

        for (var i = 0; i < 25; i++)
            await session.GenerateAsync(Topics, WritingLevel.Easy, WritingExplanationLanguage.English);

        Assert.AreEqual(WritingSession.RecentCapacity, session.RecentVietnamese.Count);
        Assert.AreEqual("Câu 6", session.RecentVietnamese[0]);
        Assert.AreEqual("Câu 25", session.RecentVietnamese[^1]);
    }

    [TestMethod]
    public async Task NewGenerate_SupersedesPendingOneAndIgnoresItsLateResult()
    {
        var coach = new FakeCoach { HoldGenerate = true };
        using var session = new WritingSession(coach);

        var first = session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);
        var second = session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);
        Assert.IsTrue(coach.GenerateTokens[0].IsCancellationRequested);

        coach.CompleteGenerate(1, PromptFor("Câu mới"));
        coach.CompleteGenerate(0, PromptFor("Câu cũ"));
        await Task.WhenAll(first, second);

        Assert.AreEqual("Câu mới", session.Prompt!.Vietnamese);
        Assert.AreEqual(WritingSessionState.Answering, session.State);
        CollectionAssert.AreEqual(new[] { "Câu mới" }, session.RecentVietnamese.ToArray());
    }

    [TestMethod]
    public async Task Check_RefusesEmptyAnswerWithoutCallingCoach()
    {
        var coach = new FakeCoach();
        using var session = new WritingSession(coach);
        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);

        await session.CheckAsync("   ", WritingExplanationLanguage.English);

        Assert.AreEqual(0, coach.CheckCalls);
        Assert.AreEqual(GeminiWritingCoach.EmptyAnswerMessage, session.Error);
        Assert.AreEqual(WritingSessionState.Answering, session.State);
        Assert.IsFalse(session.CanRetry);
    }

    [TestMethod]
    public async Task Check_BeforeAnyPrompt_DoesNothing()
    {
        var coach = new FakeCoach();
        using var session = new WritingSession(coach);

        await session.CheckAsync("Hello", WritingExplanationLanguage.English);

        Assert.AreEqual(0, coach.CheckCalls);
        Assert.AreEqual(WritingSessionState.Idle, session.State);
    }

    [TestMethod]
    public async Task CheckError_KeepsAnswerAndRetryRepeatsCheck()
    {
        var coach = new FakeCoach { CheckFailure = new InvalidOperationException("Gemini took too long to respond. Please try again.") };
        using var session = new WritingSession(coach);
        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);

        await session.CheckAsync("There was traffic jam.", WritingExplanationLanguage.English);

        Assert.AreEqual(WritingSessionState.Answering, session.State);
        Assert.AreEqual("Gemini took too long to respond. Please try again.", session.Error);
        Assert.AreEqual("There was traffic jam.", session.LastAnswer);
        Assert.IsTrue(session.CanRetry);

        coach.CheckFailure = null;
        await session.RetryAsync();

        Assert.AreEqual(WritingSessionState.Reviewed, session.State);
        Assert.AreEqual(2, coach.CheckCalls);
        Assert.AreEqual("There was traffic jam.", coach.LastAnswer);
        Assert.IsFalse(session.CanRetry);
    }

    [TestMethod]
    public async Task GenerateError_ShowsMessageAndRetryRepeatsGenerate()
    {
        var coach = new FakeCoach { GenerateFailure = new HttpRequestException("boom") };
        using var session = new WritingSession(coach);

        await session.GenerateAsync(Topics, WritingLevel.Easy, WritingExplanationLanguage.English);

        Assert.AreEqual(WritingSessionState.Idle, session.State);
        Assert.AreEqual(WritingSession.UnexpectedErrorMessage, session.Error);
        Assert.IsNull(session.Prompt);

        coach.GenerateFailure = null;
        await session.RetryAsync();

        Assert.AreEqual(WritingSessionState.Answering, session.State);
        Assert.AreEqual(WritingLevel.Easy, coach.Requests[^1].Level);
    }

    [TestMethod]
    public async Task Cancel_PendingCheckReturnsToAnsweringAndIgnoresLateFeedback()
    {
        var coach = new FakeCoach { HoldCheck = true };
        using var session = new WritingSession(coach);
        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);
        var check = session.CheckAsync("There was traffic jam.", WritingExplanationLanguage.English);

        session.Cancel();
        coach.CompleteCheck(FeedbackWith(50));
        await check;

        Assert.AreEqual(WritingSessionState.Answering, session.State);
        Assert.IsNull(session.Feedback);
        Assert.IsNull(session.Error);
    }

    [TestMethod]
    public async Task Tips_RevealInOrderAndResetOnNewPrompt()
    {
        var coach = new FakeCoach();
        using var session = new WritingSession(coach);
        Assert.IsFalse(session.RevealNextTip());

        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);
        Assert.AreEqual(3, session.AvailableTipTiers.Count);
        Assert.IsTrue(session.RevealNextTip());
        Assert.IsTrue(session.RevealNextTip());
        CollectionAssert.AreEqual(new[] { WritingTipTier.Vocabulary, WritingTipTier.Grammar }, session.RevealedTipTiers.ToArray());

        await session.CheckAsync("There was traffic jam.", WritingExplanationLanguage.English);
        Assert.AreEqual(2, session.TipsUsedForFeedback);
        Assert.IsTrue(session.RevealNextTip());
        Assert.IsFalse(session.RevealNextTip());
        Assert.AreEqual(3, session.TipsRevealed);

        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);
        Assert.AreEqual(0, session.TipsRevealed);
        Assert.AreEqual(0, session.TipsUsedForFeedback);
    }

    [TestMethod]
    public async Task Tips_SkipEmptyTiers()
    {
        var coach = new FakeCoach { NextPrompt = PromptFor("Câu", new WritingTips([], "", "There was")) };
        using var session = new WritingSession(coach);

        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);

        CollectionAssert.AreEqual(new[] { WritingTipTier.Starter }, session.AvailableTipTiers.ToArray());
        Assert.IsTrue(session.RevealNextTip());
        Assert.IsFalse(session.CanRevealTip);
    }

    [TestMethod]
    public async Task Recheck_AfterReviewIsAllowed()
    {
        var coach = new FakeCoach();
        using var session = new WritingSession(coach);
        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);
        await session.CheckAsync("There was traffic jam.", WritingExplanationLanguage.English);

        await session.CheckAsync("There was a traffic jam.", WritingExplanationLanguage.English);

        Assert.AreEqual(2, coach.CheckCalls);
        Assert.AreEqual("There was a traffic jam.", session.LastAnswer);
    }

    [TestMethod]
    public async Task Dispose_CancelsPendingWorkAndStopsEvents()
    {
        var coach = new FakeCoach { HoldGenerate = true };
        var session = new WritingSession(coach);
        var events = 0;
        session.StateChanged += () => events++;
        var pending = session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);
        var before = events;

        session.Dispose();
        coach.CompleteGenerate(0, PromptFor("Muộn"));
        await pending;

        Assert.IsTrue(coach.GenerateTokens[0].IsCancellationRequested);
        Assert.AreEqual(before, events);
        Assert.IsNull(session.Prompt);
        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(() =>
            session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English));
    }

    private sealed class FakeCoach : IWritingCoach
    {
        private readonly List<TaskCompletionSource<WritingPrompt>> _pendingGenerates = [];
        private TaskCompletionSource<WritingFeedback>? _pendingCheck;
        private int _counter;

        public bool HoldGenerate { get; set; }
        public bool HoldCheck { get; set; }
        public Exception? GenerateFailure { get; set; }
        public Exception? CheckFailure { get; set; }
        public WritingPrompt? NextPrompt { get; set; }
        public List<WritingRequest> Requests { get; } = [];
        public List<CancellationToken> GenerateTokens { get; } = [];
        public int CheckCalls { get; private set; }
        public string? LastAnswer { get; private set; }
        public WritingExplanationLanguage LastLanguage { get; private set; }

        public Task<WritingPrompt> GenerateAsync(WritingRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            GenerateTokens.Add(cancellationToken);
            if (GenerateFailure is not null)
                return Task.FromException<WritingPrompt>(GenerateFailure);
            if (HoldGenerate)
            {
                var completion = new TaskCompletionSource<WritingPrompt>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingGenerates.Add(completion);
                return completion.Task;
            }

            _counter++;
            return Task.FromResult(NextPrompt ?? PromptFor($"Câu {_counter}"));
        }

        public void CompleteGenerate(int index, WritingPrompt prompt) => _pendingGenerates[index].TrySetResult(prompt);

        public Task<WritingFeedback> CheckAsync(WritingPrompt prompt, string translation, WritingExplanationLanguage language, CancellationToken cancellationToken = default)
        {
            CheckCalls++;
            LastAnswer = translation;
            LastLanguage = language;
            if (CheckFailure is not null)
                return Task.FromException<WritingFeedback>(CheckFailure);
            if (HoldCheck)
            {
                _pendingCheck = new TaskCompletionSource<WritingFeedback>(TaskCreationOptions.RunContinuationsAsynchronously);
                return _pendingCheck.Task;
            }

            return Task.FromResult(FeedbackWith(85));
        }

        public void CompleteCheck(WritingFeedback feedback) => _pendingCheck!.TrySetResult(feedback);
    }
}
