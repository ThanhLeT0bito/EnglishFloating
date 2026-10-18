using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

/// <summary>What adding several sentences did: how many were added, skipped as duplicates, or rejected (with the first reason).</summary>
public sealed record AddSentencesResult(int Added, int Duplicates, IReadOnlyList<string> Rejected, string? FirstError)
{
    /// <summary>"Added 5 sentences · 1 was already in the list · 1 not added: Use 20 words or fewer. Fix it in the box and add again."</summary>
    public string Message
    {
        get
        {
            var parts = new List<string> { Added == 1 ? "Added 1 sentence" : $"Added {Added} sentences" };
            if (Duplicates > 0)
                parts.Add(Duplicates == 1 ? "1 was already in the list" : $"{Duplicates} were already in the list");
            if (Rejected.Count > 0)
                parts.Add($"{(Rejected.Count == 1 ? "1 not added" : $"{Rejected.Count} not added")}: {FirstError} Fix {(Rejected.Count == 1 ? "it" : "them")} in the box and add again");
            return string.Join(" · ", parts) + ".";
        }
    }
}

/// <summary>
/// An isolated, WPF-free editing session for the Settings study-list workspace.
/// </summary>
public sealed class StudyListDraft
{
    private readonly Action<AppSettings> _save;
    private AppSettings _settings;
    private bool _isSaved;

    /// <summary>The app's settings as this draft last saw them, to tell what the app changed from what was edited here.</summary>
    private AppSettings _appBaseline;

    public StudyListDraft(AppSettings initial, Action<AppSettings> save)
    {
        _save = save;
        _settings = StudyListRules.Normalize(initial);
        _appBaseline = _settings;
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

    public void SetProviderKeyConfigured(AiProvider provider, bool configured) =>
        _settings = AiProviderRules.WithKeyConfigured(_settings, provider, configured);

    public void SetAiProvider(AiProvider provider) =>
        _settings = _settings with { AiProvider = AiProviderRules.Normalize(provider) };

    public void SetDisplayPreferences(bool showSentence, bool showVocabulary) =>
        _settings = _settings with
        {
            ShowSentenceOverlay = showSentence,
            ShowVocabularyCards = showVocabulary
        };

    public void SetDisplayPreferences(bool showSentence, bool showVocabulary, bool showFloatingFlashcard, string? activeDeckKey) =>
        SetDisplayPreferences(showSentence, showVocabulary, showFloatingFlashcard, activeDeckKey, _settings.LaunchAtWindowsSignIn);

    public void SetDisplayPreferences(bool showSentence, bool showVocabulary, bool showFloatingFlashcard, string? activeDeckKey, bool launchAtWindowsSignIn) =>
        _settings = _settings with
        {
            ShowSentenceOverlay = showSentence,
            ShowVocabularyCards = showVocabulary,
            ShowFloatingFlashcard = showFloatingFlashcard,
            ActiveFlashcardDeckKey = activeDeckKey,
            LaunchAtWindowsSignIn = launchAtWindowsSignIn
        };

    public void SetTtsPreferences(string voice, double speed) =>
        _settings = _settings with
        {
            TtsVoice = voice,
            TtsSpeed = speed
        };

    public void SetPracticeMode(PracticeMode mode) =>
        _settings = _settings with { PracticeMode = mode };

    public void SetPracticeAudioDelaySeconds(int seconds) =>
        _settings = _settings with { PracticeAudioDelaySeconds = PracticeAudioDelay.Normalize(seconds) };

    public void SetWritingLevel(WritingLevel level) =>
        _settings = WritingTopicRules.Normalize(_settings with { WritingLevel = level });

    public void SetWritingExplanationLanguage(WritingExplanationLanguage language) =>
        _settings = WritingTopicRules.Normalize(_settings with { WritingExplanationLanguage = language });

    public void SetWritingPracticeMode(WritingPracticeMode mode) =>
        _settings = WritingTopicRules.Normalize(_settings with { WritingPracticeMode = mode });

    /// <summary>Sets the retype, auto-play and weak-point switches of Writing practice.</summary>
    public void SetWritingOptions(bool retype, bool autoPlayCorrected, bool focusWeakPoints) =>
        _settings = _settings with
        {
            WritingRetypeEnabled = retype,
            WritingAutoPlayCorrected = autoPlayCorrected,
            WritingFocusWeakPoints = focusWeakPoints
        };

    public void ToggleWritingTopic(string key) =>
        _settings = WritingTopicRules.ToggleSelection(_settings, key);

    public ValidationResult AddWritingTopic(string? name)
    {
        var (settings, result) = WritingTopicRules.AddCustom(_settings, name);
        if (result.IsValid)
            _settings = settings;
        return result;
    }

    public void RemoveWritingTopic(string key) =>
        _settings = WritingTopicRules.RemoveCustom(_settings, key);

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

    /// <summary>
    /// Takes in what the app changed since this draft last saw its settings (a sentence saved from the floating
    /// window, a writing option changed there, flashcard progress…) and keeps what was edited here but not saved yet.
    /// When both sides changed the same study list, the edit here wins and sentences the app added are kept too.
    /// </summary>
    public void UpdateSettingsFromApp(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var app = StudyListRules.Normalize(settings);
        var baseline = _appBaseline;
        var draft = _settings;

        T Take<T>(Func<AppSettings, T> field) =>
            EqualityComparer<T>.Default.Equals(field(app), field(baseline)) ? field(draft) : field(app);
        IReadOnlyList<T> TakeList<T>(Func<AppSettings, IReadOnlyList<T>> field) =>
            field(app).SequenceEqual(field(baseline)) ? field(draft) : field(app);

        var merged = draft with
        {
            StudyLists = MergeLists(baseline.StudyLists, app.StudyLists, draft.StudyLists),
            ActiveListId = Take(s => s.ActiveListId),
            CustomFlashcardDecks = TakeList(s => s.CustomFlashcardDecks),
            FlashcardProgress = TakeList(s => s.FlashcardProgress),
            ShowSentenceOverlay = Take(s => s.ShowSentenceOverlay),
            ShowVocabularyCards = Take(s => s.ShowVocabularyCards),
            ShowFloatingFlashcard = Take(s => s.ShowFloatingFlashcard),
            ActiveFlashcardDeckKey = Take(s => s.ActiveFlashcardDeckKey),
            LaunchAtWindowsSignIn = Take(s => s.LaunchAtWindowsSignIn),
            TtsVoice = Take(s => s.TtsVoice),
            TtsSpeed = Take(s => s.TtsSpeed),
            PracticeMode = Take(s => s.PracticeMode),
            PracticeAudioDelaySeconds = Take(s => s.PracticeAudioDelaySeconds),
            CustomWritingTopics = TakeList(s => s.CustomWritingTopics),
            SelectedWritingTopicKeys = TakeList(s => s.SelectedWritingTopicKeys),
            WritingLevel = Take(s => s.WritingLevel),
            WritingExplanationLanguage = Take(s => s.WritingExplanationLanguage),
            WritingPracticeMode = Take(s => s.WritingPracticeMode),
            WritingRetypeEnabled = Take(s => s.WritingRetypeEnabled),
            WritingAutoPlayCorrected = Take(s => s.WritingAutoPlayCorrected),
            WritingFocusWeakPoints = Take(s => s.WritingFocusWeakPoints)
        };

        // Not StudyListRules.Normalize: it would quietly "repair" an unsaved edit (e.g. an empty name) that Save must reject.
        merged = WritingTopicRules.Normalize(merged);
        if (merged.StudyLists.Count > 0 && merged.StudyLists.All(list => list.Id != merged.ActiveListId))
            merged = merged with { ActiveListId = merged.StudyLists[0].Id };
        _settings = merged;
        _appBaseline = app;
        if (!_settings.StudyLists.Any(list => list.Id == SelectedListId))
            SelectList(_settings.ActiveListId);
    }

    /// <summary>
    /// Three-way merge of study lists, list by list and sentence by sentence: what only the app changed is taken,
    /// what was edited here is kept, and where both changed the same thing the edit here wins.
    /// </summary>
    private static IReadOnlyList<StudyList> MergeLists(IReadOnlyList<StudyList> baseline, IReadOnlyList<StudyList> app, IReadOnlyList<StudyList> draft)
    {
        var baseById = baseline.ToDictionary(list => list.Id);
        var appById = app.ToDictionary(list => list.Id);
        var merged = new List<StudyList>();
        foreach (var mine in draft)
        {
            baseById.TryGetValue(mine.Id, out var original);
            appById.TryGetValue(mine.Id, out var theirs);
            if (theirs is null)
            {
                // Removed by the app: keep it only if it was created or edited here.
                if (original is null || !SameList(mine, original))
                    merged.Add(mine);
                continue;
            }

            merged.Add(original is null ? mine : MergeList(original, theirs, mine));
        }

        // Lists the app created.
        merged.AddRange(app.Where(list => !baseById.ContainsKey(list.Id) && merged.All(existing => existing.Id != list.Id)));
        return merged;
    }

    private static StudyList MergeList(StudyList original, StudyList theirs, StudyList mine) => mine with
    {
        Name = Pick(original.Name, theirs.Name, mine.Name),
        TargetSentenceCount = Pick(original.TargetSentenceCount, theirs.TargetSentenceCount, mine.TargetSentenceCount),
        CurrentSentenceIndex = Pick(original.CurrentSentenceIndex, theirs.CurrentSentenceIndex, mine.CurrentSentenceIndex),
        Sentences = MergeSentences(original.Sentences, theirs.Sentences, mine.Sentences)
    };

    private static IReadOnlyList<StudySentence> MergeSentences(
        IReadOnlyList<StudySentence> original,
        IReadOnlyList<StudySentence> theirs,
        IReadOnlyList<StudySentence> mine)
    {
        var baseById = original.ToDictionary(sentence => sentence.Id);
        var theirsById = theirs.ToDictionary(sentence => sentence.Id);
        var merged = new List<StudySentence>();
        foreach (var sentence in mine)
        {
            if (!baseById.TryGetValue(sentence.Id, out var before))
            {
                merged.Add(sentence); // added here
                continue;
            }

            if (!theirsById.TryGetValue(sentence.Id, out var other))
            {
                // Deleted by the app: keep it only if it was edited here.
                if (!SameSentence(sentence, before))
                    merged.Add(sentence);
                continue;
            }

            merged.Add(MergeSentence(before, other, sentence));
        }

        // Sentences the app added (e.g. saved from the floating window). Sentences deleted here stay deleted.
        merged.AddRange(theirs.Where(sentence => !baseById.ContainsKey(sentence.Id) && merged.All(existing => existing.Id != sentence.Id)));
        return merged;
    }

    /// <summary>Field by field; vocabulary and phrase breaks follow the text when the text was edited here.</summary>
    private static StudySentence MergeSentence(StudySentence before, StudySentence theirs, StudySentence mine)
    {
        var textEditedHere = !string.Equals(mine.Text, before.Text, StringComparison.Ordinal);
        return mine with
        {
            Text = Pick(before.Text, theirs.Text, mine.Text),
            IsCompleted = Pick(before.IsCompleted, theirs.IsCompleted, mine.IsCompleted),
            Vocabulary = textEditedHere ? mine.Vocabulary
                : mine.Vocabulary.SequenceEqual(before.Vocabulary) ? theirs.Vocabulary : mine.Vocabulary,
            PhraseBreakAfterWordIndices = textEditedHere ? mine.PhraseBreakAfterWordIndices
                : mine.PhraseBreakAfterWordIndices.SequenceEqual(before.PhraseBreakAfterWordIndices) ? theirs.PhraseBreakAfterWordIndices : mine.PhraseBreakAfterWordIndices
        };
    }

    /// <summary>The app's value when this draft did not change it, otherwise this draft's.</summary>
    private static T Pick<T>(T original, T theirs, T mine) =>
        EqualityComparer<T>.Default.Equals(mine, original) ? theirs : mine;

    private static bool SameList(StudyList a, StudyList b) =>
        a.Id == b.Id
        && a.Name == b.Name
        && a.TargetSentenceCount == b.TargetSentenceCount
        && a.CurrentSentenceIndex == b.CurrentSentenceIndex
        && a.Sentences.Count == b.Sentences.Count
        && a.Sentences.Zip(b.Sentences).All(pair => SameSentence(pair.First, pair.Second));

    private static bool SameSentence(StudySentence a, StudySentence b) =>
        a.Id == b.Id
        && a.Text == b.Text
        && a.IsCompleted == b.IsCompleted
        && a.Vocabulary.SequenceEqual(b.Vocabulary)
        && a.PhraseBreakAfterWordIndices.SequenceEqual(b.PhraseBreakAfterWordIndices);

    /// <summary>Checks every study list without saving (see <see cref="Save"/>).</summary>
    public ValidationResult Validate()
    {
        foreach (var list in _settings.StudyLists)
        {
            var validation = StudyListRules.ValidateList(list);
            if (!validation.IsValid)
                return validation;
        }

        return new(true, null);
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

    /// <summary>
    /// Adds several sentences to the selected list in order. Sentences already in the list (ignoring case and spacing)
    /// are skipped; ones that fail validation (e.g. over 20 words) are returned so the learner can fix them. The
    /// selection and the widget's current sentence stay where they were.
    /// </summary>
    public AddSentencesResult AddSentences(IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var known = SelectedList.Sentences.Select(sentence => SentenceKey(sentence.Text)).ToHashSet(StringComparer.Ordinal);
        var added = new List<StudySentence>();
        var rejected = new List<string>();
        var duplicates = 0;
        string? firstError = null;
        foreach (var raw in texts)
        {
            var text = (raw ?? string.Empty).Trim();
            if (text.Length == 0)
                continue;
            var validation = SentenceValidator.Validate(text);
            if (!validation.IsValid)
            {
                rejected.Add(text);
                firstError ??= validation.Error;
                continue;
            }

            if (!known.Add(SentenceKey(text)))
            {
                duplicates++;
                continue;
            }

            added.Add(new StudySentence(Guid.NewGuid(), text));
        }

        if (added.Count > 0)
            ReplaceSelectedList(SelectedList with { Sentences = SelectedList.Sentences.Concat(added).ToList() });

        return new AddSentencesResult(added.Count, duplicates, rejected, firstError);
    }

    private static string SentenceKey(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>Appends a sentence to any list without changing the selection (used by Writing practice).</summary>
    public ValidationResult AddSentenceToList(Guid listId, string? text)
    {
        var list = _settings.StudyLists.FirstOrDefault(candidate => candidate.Id == listId);
        if (list is null)
            return new(false, "Choose a study list.");

        var trimmed = (text ?? string.Empty).Trim();
        var validation = SentenceValidator.Validate(trimmed);
        if (!validation.IsValid)
            return validation;

        var key = VocabularyRules.NormalizePhrase(trimmed);
        if (list.Sentences.Any(sentence => string.Equals(VocabularyRules.NormalizePhrase(sentence.Text), key, StringComparison.OrdinalIgnoreCase)))
            return new(false, "This sentence is already in the list.");

        var updated = list with { Sentences = list.Sentences.Append(new StudySentence(Guid.NewGuid(), trimmed)).ToList() };
        _settings = _settings with
        {
            StudyLists = _settings.StudyLists.Select(candidate => candidate.Id == listId ? updated : candidate).ToList()
        };
        return new(true, null);
    }

    public ValidationResult AddPhrasedSentence(string groups)
    {
        var proposal = SentencePhrasing.ParseGroups(groups);
        if (!proposal.Validation.IsValid)
            return proposal.Validation;

        var sentence = new StudySentence(Guid.NewGuid(), proposal.Text,
            PhraseBreakAfterWordIndices: proposal.BreakAfterWordIndices);
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
            .Select(sentence => sentence.Id == sentenceId ? sentence with { Text = text.Trim(), PhraseBreakAfterWordIndices = [] } : sentence)
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

    /// <summary>No sentence selected: the editor box is free for new sentences.</summary>
    public void ClearSentenceSelection() => SelectedSentenceId = null;

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

        var validation = Validate();
        if (!validation.IsValid)
            return validation;

        _settings = StudyListRules.Normalize(_settings);
        _appBaseline = _settings;
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
