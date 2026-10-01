namespace PteFloatingSentence.Core;

public static class StudyListRules
{
    private const int DefaultTargetSentenceCount = 10;
    private const string DefaultListName = "My first list";
    private const string DefaultSentence = "Right-click this sentence to open Settings.";

    public static AppSettings CreateDefault(string legacySentence)
    {
        var sentence = NormalizeSentence(legacySentence);
        var list = new StudyList(
            Guid.NewGuid(),
            DefaultListName,
            DefaultTargetSentenceCount,
            0,
            [new StudySentence(Guid.NewGuid(), sentence)]);

        return new AppSettings
        {
            Version = 2,
            Sentence = sentence,
            ActiveListId = list.Id,
            StudyLists = [list]
        };
    }

    public static AppSettings Normalize(AppSettings settings)
    {
        var normalizedLists = NormalizeLists(settings.StudyLists);
        if (normalizedLists.Count == 0)
            normalizedLists = CreateDefault(settings.Sentence).StudyLists;

        var activeListId = normalizedLists.Any(list => list.Id == settings.ActiveListId)
            ? settings.ActiveListId
            : normalizedLists[0].Id;

        return settings with
        {
            Version = 2,
            Sentence = settings.Sentence?.Trim() ?? string.Empty,
            ActiveListId = activeListId,
            StudyLists = normalizedLists
        };
    }

    public static StudyList ActiveList(AppSettings settings)
    {
        var normalized = Normalize(settings);
        return normalized.StudyLists.Single(list => list.Id == normalized.ActiveListId);
    }

    public static AppSettings MoveCurrentSentence(AppSettings settings, int direction)
    {
        var activeList = ActiveList(settings);
        if (activeList.Sentences.Count == 0)
            return settings;

        var normalized = Normalize(settings);
        activeList = normalized.StudyLists.Single(list => list.Id == normalized.ActiveListId);
        var count = activeList.Sentences.Count;
        var index = (activeList.CurrentSentenceIndex + direction % count + count) % count;
        var movedList = activeList with { CurrentSentenceIndex = index };

        return normalized with
        {
            StudyLists = normalized.StudyLists.Select(list => list.Id == movedList.Id ? movedList : list).ToList()
        };
    }

    public static AppSettings MarkSentenceCompleted(AppSettings settings, Guid listId, Guid sentenceId, bool completed)
    {
        var normalized = Normalize(settings);
        var targetList = normalized.StudyLists.FirstOrDefault(list => list.Id == listId);
        if (targetList is null)
            return settings;

        var targetSentence = targetList.Sentences.FirstOrDefault(s => s.Id == sentenceId);
        if (targetSentence is null)
            return settings;

        var updatedSentences = targetList.Sentences
            .Select(s => s.Id == sentenceId ? s with { IsCompleted = completed } : s)
            .ToList();

        var updatedList = targetList with { Sentences = updatedSentences };
        var updatedLists = normalized.StudyLists
            .Select(list => list.Id == listId ? updatedList : list)
            .ToList();

        return normalized with { StudyLists = updatedLists };
    }


    public static ValidationResult ValidateList(StudyList list)
    {
        if (string.IsNullOrWhiteSpace(list.Name))
            return new(false, "Enter a list name.");

        if (list.TargetSentenceCount < 1)
            return new(false, "Target must be at least 1.");

        foreach (var sentence in list.Sentences)
        {
            var result = SentenceValidator.Validate(sentence.Text);
            if (!result.IsValid)
                return result;
        }

        return new(true, null);
    }

    private static IReadOnlyList<StudyList> NormalizeLists(IReadOnlyList<StudyList>? lists)
    {
        var listIds = new HashSet<Guid>();
        var sentenceIds = new HashSet<Guid>();

        return (lists ?? [])
            .Where(list => list is not null)
            .Select(list => NormalizeList(list, listIds, sentenceIds))
            .ToList();
    }

    private static StudyList NormalizeList(StudyList list, ISet<Guid> listIds, ISet<Guid> sentenceIds)
    {
        var listId = RepairId(list.Id, listIds);
        var sentences = (list.Sentences ?? [])
            .Where(sentence => sentence is not null)
            .Select(sentence => sentence with
            {
                Id = RepairId(sentence.Id, sentenceIds),
                Text = NormalizeSentence(sentence.Text),
                Vocabulary = NormalizeVocabulary(sentence.Vocabulary),
                PhraseBreakAfterWordIndices = SentencePhrasing.NormalizeBreaks(NormalizeSentence(sentence.Text), sentence.PhraseBreakAfterWordIndices)
            })
            .ToList();
        var currentSentenceIndex = sentences.Count == 0
            ? 0
            : Math.Clamp(list.CurrentSentenceIndex, 0, sentences.Count - 1);

        return list with
        {
            Id = listId,
            Name = string.IsNullOrWhiteSpace(list.Name) ? DefaultListName : list.Name.Trim(),
            TargetSentenceCount = list.TargetSentenceCount < 1 ? DefaultTargetSentenceCount : list.TargetSentenceCount,
            CurrentSentenceIndex = currentSentenceIndex,
            Sentences = sentences
        };
    }

    private static IReadOnlyList<VocabularyItem> NormalizeVocabulary(IReadOnlyList<VocabularyItem>? vocabulary)
    {
        if (vocabulary is null || vocabulary.Count == 0)
            return [];

        var vocabIds = new HashSet<Guid>();
        var seenCleanPhrases = new HashSet<string>();
        var result = new List<VocabularyItem>();

        foreach (var item in vocabulary)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Phrase))
                continue;

            var clean = VocabularyRules.CleanPhrase(item.Phrase);
            if (string.IsNullOrEmpty(clean) || !seenCleanPhrases.Add(clean))
                continue;

            var id = RepairId(item.Id, vocabIds);
            var phrase = VocabularyRules.NormalizePhrase(VocabularyRules.TrimPunctuation(item.Phrase));
            var normalized = clean;

            result.Add(item with
            {
                Id = id,
                Phrase = phrase,
                NormalizedPhrase = normalized
            });
        }

        return result;
    }

    private static Guid RepairId(Guid id, ISet<Guid> usedIds)
    {
        if (id == Guid.Empty || !usedIds.Add(id))
        {
            do
            {
                id = Guid.NewGuid();
            }
            while (!usedIds.Add(id));
        }

        return id;
    }

    public static string NormalizeSentence(string? sentence)
    {
        var text = sentence?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return DefaultSentence;

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= 20 ? text : DefaultSentence;
    }
}
