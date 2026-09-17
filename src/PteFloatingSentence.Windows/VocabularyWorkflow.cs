using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows;

public sealed class VocabularyWorkflow : IDisposable
{
    private readonly IVocabularyExplainer _explainer;
    private readonly Func<AppSettings> _getSettings;
    private readonly Action<AppSettings> _updateSettings;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _syncRoot = new();
    private bool _isDisposed;

    public VocabularyWorkflow(
        IVocabularyExplainer explainer,
        Func<AppSettings> getSettings,
        Action<AppSettings> updateSettings)
    {
        _explainer = explainer ?? throw new ArgumentNullException(nameof(explainer));
        _getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
        _updateSettings = updateSettings ?? throw new ArgumentNullException(nameof(updateSettings));
    }

    public async Task<VocabularyItem?> AddAsync(
        StudySentence sentence,
        string selection,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var validation = VocabularyRules.ValidatePhrase(selection);
        if (!validation.IsValid)
            return null;

        var currentSentence = FindSentence(sentence.Id) ?? sentence;
        if (VocabularyRules.ContainsEquivalent(currentSentence.Vocabulary, selection))
            return null;

        var pendingItem = VocabularyRules.CreatePending(selection);

        lock (_syncRoot)
        {
            var settings = _getSettings();
            var updatedLists = settings.StudyLists.Select(list =>
            {
                var updatedSentences = list.Sentences.Select(s =>
                {
                    if (s.Id != currentSentence.Id)
                        return s;

                    return s with { Vocabulary = [..s.Vocabulary, pendingItem] };
                }).ToList();

                return list with { Sentences = updatedSentences };
            }).ToList();

            _updateSettings(settings with { StudyLists = updatedLists });
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        await ExplainAndUpdateAsync(currentSentence.Id, pendingItem.Id, pendingItem.Phrase, currentSentence.Text, linkedCts.Token);

        return FindItem(currentSentence.Id, pendingItem.Id);
    }

    public async Task RetryAsync(Guid sentenceId, Guid itemId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var sentence = FindSentence(sentenceId);
        var item = FindItem(sentenceId, itemId);
        if (sentence is null || item is null)
            return;

        UpdateItemInSettings(sentenceId, itemId, v => v with
        {
            Status = VocabularyStatus.Pending,
            LastError = null
        });

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        await ExplainAndUpdateAsync(sentenceId, itemId, item.Phrase, sentence.Text, linkedCts.Token);
    }

    public void SetHidden(Guid sentenceId, Guid itemId, bool isHidden)
    {
        ThrowIfDisposed();
        UpdateItemInSettings(sentenceId, itemId, v => v with { IsHidden = isHidden });
    }

    private async Task ExplainAndUpdateAsync(
        Guid sentenceId,
        Guid itemId,
        string phrase,
        string sourceSentence,
        CancellationToken token)
    {
        try
        {
            var explanation = await _explainer.ExplainAsync(phrase, sourceSentence, token);
            UpdateItemInSettings(sentenceId, itemId, v => v with
            {
                Status = VocabularyStatus.Ready,
                Meaning = explanation.Meaning,
                Example = explanation.Example,
                PronunciationIpa = explanation.PronunciationIpa,
                LastError = null
            });
        }
        catch (OperationCanceledException)
        {
            // Shutdown or cancellation
        }
        catch (Exception ex)
        {
            UpdateItemInSettings(sentenceId, itemId, v => v with
            {
                Status = VocabularyStatus.Failed,
                LastError = ex.Message
            });
        }
    }

    private void UpdateItemInSettings(Guid sentenceId, Guid itemId, Func<VocabularyItem, VocabularyItem> update)
    {
        lock (_syncRoot)
        {
            var settings = _getSettings();
            var updatedLists = settings.StudyLists.Select(list =>
            {
                var updatedSentences = list.Sentences.Select(s =>
                {
                    if (s.Id != sentenceId)
                        return s;

                    var updatedVocab = s.Vocabulary.Select(v => v.Id == itemId ? update(v) : v).ToList();
                    return s with { Vocabulary = updatedVocab };
                }).ToList();

                return list with { Sentences = updatedSentences };
            }).ToList();

            _updateSettings(settings with { StudyLists = updatedLists });
        }
    }

    private StudySentence? FindSentence(Guid sentenceId)
    {
        lock (_syncRoot)
        {
            return _getSettings().StudyLists
                .SelectMany(l => l.Sentences)
                .FirstOrDefault(s => s.Id == sentenceId);
        }
    }

    private VocabularyItem? FindItem(Guid sentenceId, Guid itemId)
    {
        var sentence = FindSentence(sentenceId);
        return sentence?.Vocabulary.FirstOrDefault(v => v.Id == itemId);
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(VocabularyWorkflow));
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }
}
