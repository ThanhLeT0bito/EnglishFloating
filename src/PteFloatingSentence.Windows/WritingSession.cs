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

/// <summary>
/// UI-independent flow of one writing practice: generate a prompt, reveal tips, check an answer, retry on error.
/// A newer request cancels the older one and a request counter ignores stale results.
/// Not thread-safe: call it from the UI thread.
/// </summary>
public sealed class WritingSession : IDisposable
{
    public const int RecentCapacity = 20;
    public const string UnexpectedErrorMessage = "Something went wrong. Please try again.";

    private readonly IWritingCoach _coach;
    private readonly List<string> _recent = [];
    private CancellationTokenSource? _request;
    private long _generation;
    private Func<Task>? _retry;
    private bool _disposed;

    public WritingSession(IWritingCoach coach)
    {
        _coach = coach ?? throw new ArgumentNullException(nameof(coach));
    }

    public event Action? StateChanged;

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

    public async Task GenerateAsync(IReadOnlyList<string> topicNames, WritingLevel level, WritingExplanationLanguage language)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var topics = (topicNames ?? []).ToList();
        var (request, generation) = BeginRequest();
        Prompt = null;
        Feedback = null;
        LastAnswer = null;
        Error = null;
        TipsRevealed = 0;
        TipsUsedForFeedback = 0;
        _retry = null;
        SetState(WritingSessionState.Generating);

        try
        {
            var prompt = await _coach.GenerateAsync(new WritingRequest(topics, level, language, _recent.ToList()), request.Token);
            if (!IsCurrent(generation))
                return;

            Prompt = prompt;
            Remember(prompt.Vietnamese);
            SetState(WritingSessionState.Answering);
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
            _retry = () => GenerateAsync(topics, level, language);
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

        var (request, generation) = BeginRequest();
        LastAnswer = trimmed;
        Feedback = null;
        Error = null;
        _retry = null;
        TipsUsedForFeedback = TipsRevealed;
        SetState(WritingSessionState.Checking);

        try
        {
            var feedback = await _coach.CheckAsync(prompt, trimmed, language, request.Token);
            if (!IsCurrent(generation))
                return;

            Feedback = feedback;
            SetState(WritingSessionState.Reviewed);
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
        _retry = null;
        StateChanged = null;
    }
}
