namespace PteFloatingSentence.Core;

public static class FlashcardDeckProjection
{
    public static IReadOnlyList<FlashcardDeckSummary> GetDeckSummaries(AppSettings settings)
    {
        var summaries = new List<FlashcardDeckSummary>();
        var progressMap = FlashcardRules.NormalizeProgress(settings.FlashcardProgress)
            .ToDictionary(p => p.CardKey, p => p, StringComparer.Ordinal);

        // 1. Study-list decks
        foreach (var list in settings.StudyLists)
        {
            var deckKey = FlashcardRules.StudyListDeckKey(list.Id);
            var groups = GroupStudyListVocabulary(list);

            var readyCount = 0;
            var unavailableCount = 0;
            var rememberedCount = 0;
            var doneCount = 0;

            foreach (var g in groups)
            {
                var readyItem = g.Items.FirstOrDefault(IsReadyVocabulary);
                if (readyItem is not null)
                {
                    readyCount++;
                    var progress = FindStudyListProgress(progressMap, list.Id, g);
                    if (progress is not null)
                    {
                        if (progress.State == FlashcardLearningState.Remembered)
                            rememberedCount++;
                        if (progress.IsMarkedDone)
                            doneCount++;
                    }
                }
                else
                {
                    unavailableCount++;
                    var firstItem = g.Items.FirstOrDefault();
                    if (firstItem is not null)
                    {
                        var progress = FindStudyListProgress(progressMap, list.Id, g);
                        if (progress?.IsMarkedDone == true)
                            doneCount++;
                    }
                }
            }

            summaries.Add(new FlashcardDeckSummary(
                DeckKey: deckKey,
                SourceId: list.Id,
                Name: list.Name,
                IsCustom: false,
                TotalCount: readyCount + unavailableCount,
                ReadyCount: readyCount,
                UnavailableCount: unavailableCount,
                RememberedCount: rememberedCount,
                DoneCount: doneCount));
        }

        // 2. Custom decks
        foreach (var deck in settings.CustomFlashcardDecks)
        {
            var deckKey = FlashcardRules.CustomDeckKey(deck.Id);
            var readyCount = 0;
            var unavailableCount = 0;
            var rememberedCount = 0;
            var doneCount = 0;

            foreach (var card in deck.Cards)
            {
                var cardKey = FlashcardRules.CustomCardKey(deck.Id, card.Id);
                var isDone = progressMap.TryGetValue(cardKey, out var progress) && progress.IsMarkedDone;
                if (isDone)
                    doneCount++;

                if (IsReadyCustomCard(card))
                {
                    readyCount++;
                    if (progress is not null && progress.State == FlashcardLearningState.Remembered)
                    {
                        rememberedCount++;
                    }
                }
                else
                {
                    unavailableCount++;
                }
            }

            summaries.Add(new FlashcardDeckSummary(
                DeckKey: deckKey,
                SourceId: deck.Id,
                Name: deck.Name,
                IsCustom: true,
                TotalCount: deck.Cards.Count,
                ReadyCount: readyCount,
                UnavailableCount: unavailableCount,
                RememberedCount: rememberedCount,
                DoneCount: doneCount));
        }

        return summaries;
    }

    public static IReadOnlyList<FlashcardItem> GetDeckCards(AppSettings settings, string deckKey, bool includeUnavailable = false, bool onlyActive = false)
    {
        var cards = new List<FlashcardItem>();
        var progressMap = FlashcardRules.NormalizeProgress(settings.FlashcardProgress)
            .ToDictionary(p => p.CardKey, p => p, StringComparer.Ordinal);

        if (deckKey.StartsWith("study-list:", StringComparison.OrdinalIgnoreCase))
        {
            var idString = deckKey["study-list:".Length..];
            if (!Guid.TryParse(idString, out var listId))
                return cards;

            var list = settings.StudyLists.FirstOrDefault(l => l.Id == listId);
            if (list is null)
                return cards;

            var groups = GroupStudyListVocabulary(list);
            foreach (var g in groups)
            {
                var readyItem = g.Items.FirstOrDefault(IsReadyVocabulary);
                if (readyItem is not null)
                {
                    var cardKey = FlashcardRules.StudyListCardKey(list.Id, g.NormalizedPhrase);
                    var progress = FindStudyListProgress(progressMap, list.Id, g);

                    var isMarkedDone = progress?.IsMarkedDone ?? false;
                    if (onlyActive && isMarkedDone)
                        continue;

                    var example = !string.IsNullOrWhiteSpace(readyItem.Example)
                        ? readyItem.Example
                        : (g.SourceSentences.FirstOrDefault()?.Text ?? string.Empty);

                    cards.Add(new FlashcardItem(
                        CardKey: cardKey,
                        DeckKey: deckKey,
                        Phrase: readyItem.Phrase,
                        NormalizedPhrase: g.NormalizedPhrase,
                        PronunciationIpa: readyItem.PronunciationIpa,
                        Meaning: readyItem.Meaning ?? string.Empty,
                        Example: example,
                        SourceSentences: g.SourceSentences,
                        State: progress?.State ?? FlashcardLearningState.New,
                        ReviewCount: progress?.ReviewCount ?? 0,
                        AgainCount: progress?.AgainCount ?? 0,
                        LastRating: progress?.LastRating,
                        LastReviewedAt: progress?.LastReviewedAt,
                        IsCustom: false,
                        CustomCardId: null,
                        IsReady: true,
                        IsMarkedDone: isMarkedDone));
                }
                else if (includeUnavailable)
                {
                    var firstItem = g.Items.FirstOrDefault();
                    if (firstItem is null)
                        continue;

                    var cardKey = FlashcardRules.StudyListCardKey(list.Id, g.NormalizedPhrase);
                    var progress = FindStudyListProgress(progressMap, list.Id, g);

                    var isMarkedDone = progress?.IsMarkedDone ?? false;
                    if (onlyActive && isMarkedDone)
                        continue;

                    var reason = firstItem.Status switch
                    {
                        VocabularyStatus.Pending => "Pending explanation...",
                        VocabularyStatus.Failed => !string.IsNullOrWhiteSpace(firstItem.LastError)
                            ? firstItem.LastError
                            : "Explanation failed",
                        _ => "Explanation missing"
                    };

                    cards.Add(new FlashcardItem(
                        CardKey: cardKey,
                        DeckKey: deckKey,
                        Phrase: firstItem.Phrase,
                        NormalizedPhrase: g.NormalizedPhrase,
                        PronunciationIpa: firstItem.PronunciationIpa,
                        Meaning: $"[{reason}]",
                        Example: g.SourceSentences.FirstOrDefault()?.Text ?? string.Empty,
                        SourceSentences: g.SourceSentences,
                        State: progress?.State ?? FlashcardLearningState.New,
                        ReviewCount: progress?.ReviewCount ?? 0,
                        AgainCount: progress?.AgainCount ?? 0,
                        LastRating: progress?.LastRating,
                        LastReviewedAt: progress?.LastReviewedAt,
                        IsCustom: false,
                        CustomCardId: null,
                        IsReady: false,
                        UnavailableReason: reason,
                        IsMarkedDone: isMarkedDone));
                }
            }
        }
        else if (deckKey.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
        {
            var idString = deckKey["custom:".Length..];
            if (!Guid.TryParse(idString, out var deckId))
                return cards;

            var deck = settings.CustomFlashcardDecks.FirstOrDefault(d => d.Id == deckId);
            if (deck is null)
                return cards;

            foreach (var card in deck.Cards)
            {
                var isReady = IsReadyCustomCard(card);
                if (!isReady && !includeUnavailable)
                    continue;

                var cardKey = FlashcardRules.CustomCardKey(deck.Id, card.Id);
                progressMap.TryGetValue(cardKey, out var progress);

                var isMarkedDone = progress?.IsMarkedDone ?? false;
                if (onlyActive && isMarkedDone)
                    continue;

                cards.Add(new FlashcardItem(
                    CardKey: cardKey,
                    DeckKey: deckKey,
                    Phrase: card.Phrase,
                    NormalizedPhrase: card.NormalizedPhrase,
                    PronunciationIpa: card.PronunciationIpa,
                    Meaning: isReady ? card.Meaning : "[Incomplete card]",
                    Example: card.Example,
                    SourceSentences: [],
                    State: progress?.State ?? FlashcardLearningState.New,
                    ReviewCount: progress?.ReviewCount ?? 0,
                    AgainCount: progress?.AgainCount ?? 0,
                    LastRating: progress?.LastRating,
                    LastReviewedAt: progress?.LastReviewedAt,
                    IsCustom: true,
                    CustomCardId: card.Id,
                    IsReady: isReady,
                    UnavailableReason: isReady ? null : "Missing required card fields",
                    IsMarkedDone: isMarkedDone));
            }
        }

        return cards;
    }

    private static bool IsReadyVocabulary(VocabularyItem item) =>
        item.Status == VocabularyStatus.Ready &&
        !string.IsNullOrWhiteSpace(item.Phrase) &&
        !string.IsNullOrWhiteSpace(item.Meaning);

    private static bool IsReadyCustomCard(CustomVocabularyCard card) =>
        !string.IsNullOrWhiteSpace(card.Phrase) &&
        !string.IsNullOrWhiteSpace(card.Meaning) &&
        !string.IsNullOrWhiteSpace(card.Example);

    private sealed class VocabGroup
    {
        public required string NormalizedPhrase { get; init; }
        public List<VocabularyItem> Items { get; } = [];
        public List<FlashcardSourceSentence> SourceSentences { get; } = [];
    }

    private static List<VocabGroup> GroupStudyListVocabulary(StudyList list)
    {
        var groupDict = new Dictionary<string, VocabGroup>(StringComparer.OrdinalIgnoreCase);
        var orderedGroups = new List<VocabGroup>();

        foreach (var sentence in list.Sentences)
        {
            foreach (var vocab in sentence.Vocabulary)
            {
                var norm = VocabularyRules.CleanPhrase(vocab.Phrase);
                if (string.IsNullOrEmpty(norm))
                    continue;

                if (!groupDict.TryGetValue(norm, out var group))
                {
                    group = new VocabGroup { NormalizedPhrase = norm };
                    groupDict[norm] = group;
                    orderedGroups.Add(group);
                }

                group.Items.Add(vocab);
                if (!group.SourceSentences.Any(s => s.SentenceId == sentence.Id))
                {
                    group.SourceSentences.Add(new FlashcardSourceSentence(sentence.Id, sentence.Text));
                }
            }
        }

        return orderedGroups;
    }

    private static FlashcardProgress? FindStudyListProgress(
        IReadOnlyDictionary<string, FlashcardProgress> progressMap,
        Guid listId,
        VocabGroup group)
    {
        var currentKey = FlashcardRules.StudyListCardKey(listId, group.NormalizedPhrase);
        if (progressMap.TryGetValue(currentKey, out var current))
            return current;

        // Compatibility with cards saved before punctuation was cleaned from stable keys.
        foreach (var item in group.Items)
        {
            if (string.IsNullOrWhiteSpace(item.NormalizedPhrase))
                continue;

            var legacyKey = FlashcardRules.StudyListCardKey(listId, item.NormalizedPhrase);
            if (progressMap.TryGetValue(legacyKey, out var legacy))
                return legacy;
        }

        return null;
    }
}
