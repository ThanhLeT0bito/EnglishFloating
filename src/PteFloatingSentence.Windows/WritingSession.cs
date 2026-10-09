using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows;

public enum WritingSessionState
{
    Idle,
    Generating,
    Answering,
    Checking,
    Reviewed
}

/// <summary>What Enter in the answer box does in the current state.</summary>
public enum WritingEnterAction
{
    None,
    Check,
    Retype,
    Next
}

/// <summary>
/// UI-independent flow of one writing practice: generate a prompt, reveal tips, check an answer (by AI when
/// translating, locally in dictation), retype the corrected sentence, retry on error. Checked mistakes go to the
/// <see cref="History"/> notebook. A newer request cancels the older one and a request counter ignores stale results.
/// Not thread-safe: call it from the UI thread.
/// </summary>
public sealed class WritingSession : IDisposable
{
    public const int RecentCapacity = 20;
    public const string UnexpectedErrorMessage = "Something went wrong. Please try again.";

    private readonly IWritingCoach _coach;
    private readonly bool _prefetchNext;
    private readonly List<string> _recent = [];
    private Prefetch? _prefetch;
    private CancellationTokenSource? _request;
    private long _generation;
    private Func<Task>? _retry;
    private bool _disposed;

    /// <param name="prefetchNext">
    /// When true, the next sentence is requested in the background as soon as one is shown, so Next/Skip with the
    /// same topics, level and language is instant.
    /// </param>
    public WritingSession(IWritingCoach coach, bool prefetchNext = false)
    {
        _coach = coach ?? throw new ArgumentNullException(nameof(coach));
        _prefetchNext = prefetchNext;
    }

    /// <summary>True when the current prompt came from the background prefetch.</summary>
    public bool PromptWasPrefetched { get; private set; }

    /// <summary>True while a prefetched next sentence is being prepared or waiting to be used.</summary>
    public bool HasPrefetch => _prefetch is not null;

    public event Action? StateChanged;

    /// <summary>How the next answer is checked. Dictation compares with the English reference without AI.</summary>
    public WritingPracticeMode Mode { get; set; } = WritingPracticeMode.Translate;

    /// <summary>When true, a check with mistakes asks the learner to type the corrected sentence once.</summary>
    public bool RetypeEnabled { get; set; } = true;

    /// <summary>Notebook that receives the mistakes of each check; null keeps nothing.</summary>
    public WritingHistoryBook? History { get; set; }

    /// <summary>Mode used for the current feedback.</summary>
    public WritingPracticeMode FeedbackMode { get; private set; }

    /// <summary>Word-by-word result of the latest dictation check; null after a translation check.</summary>
    public DictationResult? Dictation { get; private set; }

    /// <summary>The corrected sentence to retype, or null when no retype is needed.</summary>
    public string? RetypeTarget { get; private set; }

    public bool RetypeDone { get; private set; }

    public bool RetypeSkipped { get; private set; }

    /// <summary>Result of the latest retype attempt.</summary>
    public RetypeResult? LastRetype { get; private set; }

    /// <summary>The learner's matching retype, kept so Enter on it moves to the next sentence.</summary>
    public string? RetypedText { get; private set; }

    /// <summary>True while the learner must type the corrected sentence before moving on.</summary>
    public bool IsRetyping => State == WritingSessionState.Reviewed && RetypeTarget is not null && !RetypeDone;

    public WritingSessionState State { get; private set; } = WritingSessionState.Idle;
    public WritingPrompt? Prompt { get; private set; }
    public WritingFeedback? Feedback { get; private set; }

    /// <summary>The trimmed answer sent with the latest check. Kept when the check fails.</summary>
    public string? LastAnswer { get; private set; }

    public string? Error { get; private set; }
    public bool CanRetry => _retry is not null && !IsBusy;
    public bool IsBusy => State is WritingSessionState.Generating or WritingSessionState.Checking;
    public IReadOnlyList<string> RecentVietnamese => _recent;

    public int TipsRevealed { get; private set; }

    /// <summary>Number of tips that had been revealed when the current feedback was requested.</summary>
    public int TipsUsedForFeedback { get; private set; }

    public IReadOnlyList<WritingTipTier> AvailableTipTiers =>
        Prompt is null ? [] : WritingTipRules.AvailableTiers(Prompt.Tips);

    public IReadOnlyList<WritingTipTier> RevealedTipTiers => AvailableTipTiers.Take(TipsRevealed).ToList();

    public bool CanRevealTip => Prompt is not null && TipsRevealed < AvailableTipTiers.Count;

    public async Task GenerateAsync(
        IReadOnlyList<string> topicNames,
        WritingLevel level,
        WritingExplanationLanguage language,
        IReadOnlyList<WritingSkillTag>? focusTags = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var topics = (topicNames ?? []).ToList();
        var focus = (focusTags ?? []).Distinct().ToList();
        var (request, generation) = BeginRequest();
        Prompt = null;
        Feedback = null;
        LastAnswer = null;
        Error = null;
        TipsRevealed = 0;
        TipsUsedForFeedback = 0;
        PromptWasPrefetched = false;
        ResetCheckResult();
        _retry = null;
        SetState(WritingSessionState.Generating);

        var key = PrefetchKey(topics, level, language, focus);
        var prefetched = TakePrefetch(key);
        try
        {
            WritingPrompt? prompt = null;
            if (prefetched is not null)
            {
                try
                {
                    prompt = await prefetched;
                    PromptWasPrefetched = true;
                }
                catch (Exception) when (IsCurrent(generation) && !request.IsCancellationRequested)
                {
                    // The background request failed; ask again in the foreground.
                }
            }

            prompt ??= await _coach.GenerateAsync(NewRequest(topics, level, language, focus), request.Token);
            if (!IsCurrent(generation))
                return;

            Prompt = prompt;
            Remember(prompt.Vietnamese);
            SetState(WritingSessionState.Answering);
            StartPrefetch(key, topics, level, language, focus);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            // Superseded or cancelled; whoever cancelled has already updated the state.
        }
        catch (Exception error)
        {
            if (!IsCurrent(generation))
                return;

            Error = MessageFor(error);
            _retry = () => GenerateAsync(topics, level, language, focus);
            SetState(WritingSessionState.Idle);
        }
        finally
        {
            EndRequest(request);
        }
    }

    public async Task CheckAsync(string? answer, WritingExplanationLanguage language)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var prompt = Prompt;
        if (prompt is null || State is not (WritingSessionState.Answering or WritingSessionState.Reviewed))
            return;

        var trimmed = (answer ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            Error = AiWritingCoach.EmptyAnswerMessage;
            _retry = null;
            StateChanged?.Invoke();
            return;
        }

        var mode = Mode;
        if (mode == WritingPracticeMode.Dictation)
        {
            // Dictation is scored locally and instantly against the English reference.
            ++_generation;
            _request?.Cancel();
            LastAnswer = trimmed;
            Error = null;
            _retry = null;
            TipsUsedForFeedback = TipsRevealed;
            var result = WritingTextMatch.ScoreDictation(prompt.ReferenceEnglish, trimmed);
            Complete(prompt, trimmed, WritingTextMatch.DictationFeedback(prompt.ReferenceEnglish, result, language), mode, result);
            return;
        }

        var (request, generation) = BeginRequest();
        LastAnswer = trimmed;
        Feedback = null;
        ResetCheckResult();
        Error = null;
        _retry = null;
        TipsUsedForFeedback = TipsRevealed;
        SetState(WritingSessionState.Checking);

        try
        {
            var feedback = await _coach.CheckAsync(prompt, trimmed, language, request.Token);
            if (!IsCurrent(generation))
                return;

            Complete(prompt, trimmed, feedback, mode, null);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!IsCurrent(generation))
                return;

            Error = MessageFor(error);
            _retry = () => CheckAsync(trimmed, language);
            SetState(WritingSessionState.Answering);
        }
        finally
        {
            EndRequest(request);
        }
    }

    private void Complete(WritingPrompt prompt, string answer, WritingFeedback feedback, WritingPracticeMode mode, DictationResult? dictation)
    {
        ResetCheckResult();
        Feedback = feedback;
        FeedbackMode = mode;
        Dictation = dictation;
        if (RetypeEnabled && NeedsRetype(answer, feedback))
            RetypeTarget = feedback.CorrectedSentence;

        History?.RecordCheck(mode, prompt, answer, feedback);
        SetState(WritingSessionState.Reviewed);
    }

    /// <summary>A retype is useful when the answer had real mistakes and differs from the corrected sentence.</summary>
    public static bool NeedsRetype(string? answer, WritingFeedback feedback) =>
        !string.IsNullOrWhiteSpace(feedback.CorrectedSentence)
        && (feedback.Issues ?? []).Any(issue => issue.Type != WritingIssueType.Punctuation)
        && !WritingTextMatch.SameWords(answer, feedback.CorrectedSentence);

    /// <summary>Compares the learner's retype with the corrected sentence; a match finishes the retype.</summary>
    public RetypeResult? SubmitRetype(string? text)
    {
        if (_disposed || !IsRetyping)
            return null;

        var result = WritingTextMatch.CompareRetype(RetypeTarget, text);
        LastRetype = result;
        if (result.IsMatch)
        {
            RetypeDone = true;
            RetypedText = (text ?? string.Empty).Trim();
        }

        StateChanged?.Invoke();
        return result;
    }

    /// <summary>Ends the retype step without typing the sentence.</summary>
    public void SkipRetype()
    {
        if (_disposed || !IsRetyping)
            return;

        RetypeDone = true;
        RetypeSkipped = true;
        LastRetype = null;
        StateChanged?.Invoke();
    }

    /// <summary>What Enter does with <paramref name="currentText"/> in the answer box.</summary>
    public WritingEnterAction EnterActionFor(string? currentText)
    {
        if (IsBusy || Prompt is null)
            return WritingEnterAction.None;
        if (State == WritingSessionState.Answering)
            return WritingEnterAction.Check;
        if (State != WritingSessionState.Reviewed)
            return WritingEnterAction.None;
        if (IsRetyping)
            return WritingEnterAction.Retype;

        var text = (currentText ?? string.Empty).Trim();
        var unchanged = text.Length == 0
            || string.Equals(text, (LastAnswer ?? string.Empty).Trim(), StringComparison.Ordinal)
            || (RetypedText is not null && string.Equals(text, RetypedText, StringComparison.Ordinal));
        return unchanged ? WritingEnterAction.Next : WritingEnterAction.Check;
    }

    private void ResetCheckResult()
    {
        Dictation = null;
        RetypeTarget = null;
        RetypeDone = false;
        RetypeSkipped = false;
        LastRetype = null;
        RetypedText = null;
    }

    private WritingRequest NewRequest(
        IReadOnlyList<string> topics,
        WritingLevel level,
        WritingExplanationLanguage language,
        IReadOnlyList<WritingSkillTag> focus) =>
        new(topics, level, language, _recent.ToList()) { FocusTags = focus };

    /// <summary>Repeats the last failed generate or check.</summary>
    public Task RetryAsync()
    {
        if (_disposed || IsBusy || _retry is null)
            return Task.CompletedTask;

        var retry = _retry;
        _retry = null;
        return retry();
    }

    /// <summary>Stops an in-flight request. A pending check returns to answering; a pending generate returns to idle.</summary>
    public void Cancel()
    {
        if (!IsBusy)
            return;

        ++_generation;
        _request?.Cancel();
        SetState(State == WritingSessionState.Checking ? WritingSessionState.Answering : WritingSessionState.Idle);
    }

    /// <summary>Reveals the next tip tier (key words, then grammar, then starter).</summary>
    public bool RevealNextTip()
    {
        if (_disposed || !CanRevealTip)
            return false;

        TipsRevealed++;
        StateChanged?.Invoke();
        return true;
    }

    private sealed record Prefetch(string Key, Task<WritingPrompt> Task, CancellationTokenSource Cancellation);

    private static string PrefetchKey(
        IReadOnlyList<string> topics,
        WritingLevel level,
        WritingExplanationLanguage language,
        IReadOnlyList<WritingSkillTag> focus) =>
        $"{level}|{language}|{string.Join(",", focus)}|{string.Join("\u001f", topics)}";

    /// <summary>Hands over a prefetched request made with the same options, cancelling one made with other options.</summary>
    private Task<WritingPrompt>? TakePrefetch(string key)
    {
        var prefetch = _prefetch;
        _prefetch = null;
        if (prefetch is null)
            return null;
        if (prefetch.Key == key && !prefetch.Task.IsCanceled)
            return prefetch.Task;

        Cancel(prefetch);
        return null;
    }

    private void StartPrefetch(
        string key,
        IReadOnlyList<string> topics,
        WritingLevel level,
        WritingExplanationLanguage language,
        IReadOnlyList<WritingSkillTag> focus)
    {
        CancelPrefetch();
        if (!_prefetchNext || _disposed)
            return;

        var cancellation = new CancellationTokenSource();
        Task<WritingPrompt> task;
        try
        {
            task = _coach.GenerateAsync(NewRequest(topics, level, language, focus), cancellation.Token);
        }
        catch (Exception error)
        {
            task = Task.FromException<WritingPrompt>(error);
        }

        // The source is only disposed once the request is over, so cancelling it is always safe.
        // Reading Exception marks a failure as observed; GenerateAsync retries in the foreground.
        _ = task.ContinueWith(completed =>
        {
            _ = completed.Exception;
            cancellation.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _prefetch = new Prefetch(key, task, cancellation);
    }

    private void CancelPrefetch()
    {
        var prefetch = _prefetch;
        _prefetch = null;
        if (prefetch is not null)
            Cancel(prefetch);
    }

    private static void Cancel(Prefetch prefetch)
    {
        if (prefetch.Task.IsCompleted)
            return;
        try
        {
            prefetch.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The request finished (and released its source) at the same moment.
        }
    }

    private (CancellationTokenSource Request, long Generation) BeginRequest()
    {
        _request?.Cancel();
        var request = new CancellationTokenSource();
        _request = request;
        return (request, ++_generation);
    }

    private void EndRequest(CancellationTokenSource request)
    {
        if (ReferenceEquals(_request, request))
            _request = null;
        request.Dispose();
    }

    private bool IsCurrent(long generation) => !_disposed && generation == _generation;

    private void Remember(string vietnamese)
    {
        _recent.Remove(vietnamese);
        _recent.Add(vietnamese);
        if (_recent.Count > RecentCapacity)
            _recent.RemoveRange(0, _recent.Count - RecentCapacity);
    }

    private void SetState(WritingSessionState state)
    {
        State = state;
        StateChanged?.Invoke();
    }

    private static string MessageFor(Exception error) =>
        error is InvalidOperationException && !string.IsNullOrWhiteSpace(error.Message) ? error.Message : UnexpectedErrorMessage;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ++_generation;
        _request?.Cancel();
        CancelPrefetch();
        _retry = null;
        StateChanged = null;
    }
}
