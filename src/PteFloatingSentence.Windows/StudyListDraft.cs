using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

/// <summary>
/// An isolated, WPF-free editing session for the Settings study-list workspace.
/// </summary>
public sealed class StudyListDraft
{
    private readonly Action<AppSettings> _save;
    private AppSettings _settings;
    private bool _isSaved;

    public StudyListDraft(AppSettings initial, Action<AppSettings> save)
    {
        _save = save;
        _settings = StudyListRules.Normalize(initial);
        SelectedListId = _settings.ActiveListId;
    }

    public AppSettings Settings => _settings;

    public Guid SelectedListId { get; private set; }

    public Guid? SelectedSentenceId { get; private set; }

    public StudyList SelectedList => _settings.StudyLists.Single(list => list.Id == SelectedListId);

    public void SelectList(Guid listId)
    {
        if (!_settings.StudyLists.Any(list => list.Id == listId))
            return;

        SelectedListId = listId;
        SelectedSentenceId = null;
    }

    public StudyList CreateList()
    {
        var list = new StudyList(Guid.NewGuid(), "New list", 10, 0, []);
        _settings = _settings with { StudyLists = _settings.StudyLists.Append(list).ToList() };
        SelectList(list.Id);
        return list;
    }

    public void UpdateSelectedList(string name, int targetSentenceCount)
    {
        ReplaceSelectedList(SelectedList with { Name = name, TargetSentenceCount = targetSentenceCount });
    }

    public void MakeSelectedListActive() => _settings = _settings with { ActiveListId = SelectedListId };

    public void SetApiKeyConfigured(bool configured) => _settings = _settings with { GeminiApiKeyConfigured = configured };

    public void SetDisplayPreferences(bool showSentence, bool showVocabulary) =>
        _settings = _settings with
        {
            ShowSentenceOverlay = showSentence,
            ShowVocabularyCards = showVocabulary
        };

    public void UpdateFlashcardSettings(AppSettings settings)
    {
        _settings = _settings with
        {
            CustomFlashcardDecks = settings.CustomFlashcardDecks,
            FlashcardProgress = settings.FlashcardProgress,
            ShowFloatingFlashcard = settings.ShowFloatingFlashcard,
            ActiveFlashcardDeckKey = settings.ActiveFlashcardDeckKey
        };
    }

    public ValidationResult DeleteSelectedList()
    {
        if (_settings.StudyLists.Count == 1)
            return new(false, "Cannot delete the last list.");

        var remaining = _settings.StudyLists.Where(list => list.Id != SelectedListId).ToList();
        var nextSelected = remaining[0];
        _settings = _settings with
        {
            StudyLists = remaining,
            ActiveListId = _settings.ActiveListId == SelectedListId ? nextSelected.Id : _settings.ActiveListId
        };
        SelectList(nextSelected.Id);
        return new(true, null);
    }

    public ValidationResult AddSentence(string text)
    {
        var validation = SentenceValidator.Validate(text);
        if (!validation.IsValid)
            return validation;

        var sentence = new StudySentence(Guid.NewGuid(), text.Trim());
        ReplaceSelectedList(SelectedList with { Sentences = SelectedList.Sentences.Append(sentence).ToList() });
        SelectSentence(sentence.Id);
        return new(true, null);
    }

    public ValidationResult UpdateSelectedSentence(string text)
    {
        if (SelectedSentenceId is not Guid sentenceId)
            return new(false, "Select a sentence first.");

        var validation = SentenceValidator.Validate(text);
        if (!validation.IsValid)
            return validation;

        var sentences = SelectedList.Sentences
            .Select(sentence => sentence.Id == sentenceId ? sentence with { Text = text.Trim() } : sentence)
            .ToList();
        ReplaceSelectedList(SelectedList with { Sentences = sentences });
        return new(true, null);
    }

    public void DeleteSelectedSentence()
    {
        if (SelectedSentenceId is not Guid sentenceId)
            return;

        var sentences = SelectedList.Sentences.Where(sentence => sentence.Id != sentenceId).ToList();
        ReplaceSelectedList(SelectedList with { Sentences = sentences });
        SelectedSentenceId = null;
    }

    public void SelectSentence(Guid sentenceId)
    {
        var index = SelectedList.Sentences.ToList().FindIndex(sentence => sentence.Id == sentenceId);
        if (index < 0)
            return;

        ReplaceSelectedList(SelectedList with { CurrentSentenceIndex = index });
        SelectedSentenceId = sentenceId;
    }

    public void MarkSentenceCompleted(Guid listId, Guid sentenceId, bool completed)
    {
        _settings = StudyListRules.MarkSentenceCompleted(_settings, listId, sentenceId, completed);
    }

    public ValidationResult Save()
    {
        if (_isSaved)
            return new(true, null);

        foreach (var list in _settings.StudyLists)
        {
            var validation = StudyListRules.ValidateList(list);
            if (!validation.IsValid)
                return validation;
        }

        _settings = StudyListRules.Normalize(_settings);
        _save(_settings);
        _isSaved = true;
        return new(true, null);
    }

    public void Cancel()
    {
    }

    private void ReplaceSelectedList(StudyList replacement) =>
        _settings = _settings with
        {
            StudyLists = _settings.StudyLists.Select(list => list.Id == replacement.Id ? replacement : list).ToList()
        };
}
