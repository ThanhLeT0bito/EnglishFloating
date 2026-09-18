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

            foreach (var g in groups)
            {
                var readyItem = g.Items.FirstOrDefault(IsReadyVocabulary);
                if (readyItem is not null)
                {
                    readyCount++;
                    var cardKey = FlashcardRules.StudyListCardKey(list.Id, readyItem.NormalizedPhrase);
                    if (progressMap.TryGetValue(cardKey, out var progress) && progress.State == FlashcardLearningState.Remembered)
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
                SourceId: list.Id,
                Name: list.Name,
                IsCustom: false,
                TotalCount: readyCount + unavailableCount,
                ReadyCount: readyCount,
                UnavailableCount: unavailableCount,
                RememberedCount: rememberedCount));
        }

        // 2. Custom decks
        foreach (var deck in settings.CustomFlashcardDecks)
        {
            var deckKey = FlashcardRules.CustomDeckKey(deck.Id);
            var readyCount = 0;
            var unavailableCount = 0;
            var rememberedCount = 0;

            foreach (var card in deck.Cards)
            {
                if (IsReadyCustomCard(card))
                {
                    readyCount++;
                    var cardKey = FlashcardRules.CustomCardKey(deck.Id, card.Id);
                    if (progressMap.TryGetValue(cardKey, out var progress) && progress.State == FlashcardLearningState.Remembered)
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
                RememberedCount: rememberedCount));
        }

        return summaries;
    }

    public static IReadOnlyList<FlashcardItem> GetDeckCards(AppSettings settings, string deckKey)
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
                if (readyItem is null)
                    continue;

                var cardKey = FlashcardRules.StudyListCardKey(list.Id, readyItem.NormalizedPhrase);
                progressMap.TryGetValue(cardKey, out var progress);

                cards.Add(new FlashcardItem(
                    CardKey: cardKey,
                    DeckKey: deckKey,
                    Phrase: readyItem.Phrase,
                    NormalizedPhrase: readyItem.NormalizedPhrase,
                    PronunciationIpa: readyItem.PronunciationIpa,
                    Meaning: readyItem.Meaning ?? string.Empty,
                    Example: readyItem.Example ?? string.Empty,
                    SourceSentences: g.SourceSentences,
                    State: progress?.State ?? FlashcardLearningState.New,
                    ReviewCount: progress?.ReviewCount ?? 0,
                    AgainCount: progress?.AgainCount ?? 0,
                    LastRating: progress?.LastRating,
                    LastReviewedAt: progress?.LastReviewedAt,
                    IsCustom: false,
                    CustomCardId: null));
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
                if (!IsReadyCustomCard(card))
                    continue;

                var cardKey = FlashcardRules.CustomCardKey(deck.Id, card.Id);
                progressMap.TryGetValue(cardKey, out var progress);

                cards.Add(new FlashcardItem(
                    CardKey: cardKey,
                    DeckKey: deckKey,
                    Phrase: card.Phrase,
                    NormalizedPhrase: card.NormalizedPhrase,
                    PronunciationIpa: card.PronunciationIpa,
                    Meaning: card.Meaning,
                    Example: card.Example,
                    SourceSentences: [],
                    State: progress?.State ?? FlashcardLearningState.New,
                    ReviewCount: progress?.ReviewCount ?? 0,
                    AgainCount: progress?.AgainCount ?? 0,
                    LastRating: progress?.LastRating,
                    LastReviewedAt: progress?.LastReviewedAt,
                    IsCustom: true,
                    CustomCardId: card.Id));
            }
        }

        return cards;
    }

    private static bool IsReadyVocabulary(VocabularyItem item) =>
        item.Status == VocabularyStatus.Ready &&
        !string.IsNullOrWhiteSpace(item.Phrase) &&
        !string.IsNullOrWhiteSpace(item.Meaning) &&
        !string.IsNullOrWhiteSpace(item.Example);

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
                if (vocab.IsHidden)
                    continue;

                var norm = VocabularyRules.NormalizePhrase(vocab.Phrase).ToLowerInvariant();
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
}
