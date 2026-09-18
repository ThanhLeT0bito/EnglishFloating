namespace PteFloatingSentence.Core;

public static class FlashcardRules
{
    public static string StudyListDeckKey(Guid studyListId) => $"study-list:{studyListId}";

    public static string CustomDeckKey(Guid customDeckId) => $"custom:{customDeckId}";

    public static string StudyListCardKey(Guid studyListId, string normalizedPhrase) =>
        $"study-list:{studyListId}:{normalizedPhrase}";

    public static string CustomCardKey(Guid customDeckId, Guid cardId) =>
        $"custom:{customDeckId}:{cardId}";

    public static FlashcardProgress ApplyRating(FlashcardProgress current, FlashcardRating rating, DateTimeOffset timestamp)
    {
        var reviewCount = current.ReviewCount + 1;
        var againCount = current.AgainCount + (rating == FlashcardRating.Again ? 1 : 0);
        var newState = rating switch
        {
            FlashcardRating.Again => FlashcardLearningState.Learning,
            FlashcardRating.Hard => FlashcardLearningState.Learning,
            FlashcardRating.Remembered => FlashcardLearningState.Remembered,
            _ => current.State
        };

        return current with
        {
            State = newState,
            ReviewCount = reviewCount,
            AgainCount = againCount,
            LastRating = rating,
            LastReviewedAt = timestamp
        };
    }

    public static AppSettings ApplyRating(AppSettings settings, string cardKey, FlashcardRating rating, DateTimeOffset timestamp)
    {
        var existing = settings.FlashcardProgress.FirstOrDefault(p => p.CardKey == cardKey);
        var baseProgress = existing ?? new FlashcardProgress(cardKey, FlashcardLearningState.New, 0, 0, null, null);
        var updated = ApplyRating(baseProgress, rating, timestamp);

        var list = new List<FlashcardProgress>(settings.FlashcardProgress.Count + 1);
        var replaced = false;
        foreach (var p in settings.FlashcardProgress)
        {
            if (p.CardKey == cardKey)
            {
                list.Add(updated);
                replaced = true;
            }
            else
            {
                list.Add(p);
            }
        }

        if (!replaced)
        {
            list.Add(updated);
        }

        return settings with { FlashcardProgress = list };
    }

    public static AppSettings SetMarkedDone(AppSettings settings, string cardKey, bool isMarkedDone, DateTimeOffset timestamp)
    {
        var existing = settings.FlashcardProgress.FirstOrDefault(p => p.CardKey == cardKey);
        var baseProgress = existing ?? new FlashcardProgress(cardKey, FlashcardLearningState.New, 0, 0, null, null);
        var updated = baseProgress with
        {
            IsMarkedDone = isMarkedDone,
            LastReviewedAt = timestamp
        };

        var list = new List<FlashcardProgress>(settings.FlashcardProgress.Count + 1);
        var replaced = false;
        foreach (var p in settings.FlashcardProgress)
        {
            if (p.CardKey == cardKey)
            {
                list.Add(updated);
                replaced = true;
            }
            else
            {
                list.Add(p);
            }
        }

        if (!replaced)
        {
            list.Add(updated);
        }

        return settings with { FlashcardProgress = list };
    }

    public static IReadOnlyList<FlashcardProgress> NormalizeProgress(IEnumerable<FlashcardProgress> progress)
    {
        var dict = new Dictionary<string, FlashcardProgress>(StringComparer.Ordinal);
        foreach (var item in progress)
        {
            if (!dict.TryGetValue(item.CardKey, out var existing))
            {
                dict[item.CardKey] = item;
            }
            else
            {
                var itemTime = item.LastReviewedAt ?? DateTimeOffset.MinValue;
                var existingTime = existing.LastReviewedAt ?? DateTimeOffset.MinValue;
                var isDone = itemTime >= existingTime ? item.IsMarkedDone : existing.IsMarkedDone;
                var winner = (itemTime > existingTime || (itemTime == existingTime && item.ReviewCount >= existing.ReviewCount))
                    ? item
                    : existing;
                dict[item.CardKey] = winner with { IsMarkedDone = isDone };
            }
        }

        return dict.Values.ToList();
    }

    public static ValidationResult ValidateCustomDeckName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new(false, "Deck name is required.");

        var trimmed = name.Trim();
        if (trimmed.Length > 50)
            return new(false, "Deck name must be 50 characters or fewer.");

        return new(true, null);
    }

    public static ValidationResult ValidateCustomCard(
        string? phrase,
        string? pronunciation,
        string? meaning,
        string? example,
        CustomFlashcardDeck? existingDeck,
        Guid? editingCardId = null)
    {
        var phraseValidation = VocabularyRules.ValidatePhrase(phrase);
        if (!phraseValidation.IsValid)
            return phraseValidation;

        if (string.IsNullOrWhiteSpace(meaning))
            return new(false, "Meaning is required.");

        if (string.IsNullOrWhiteSpace(example))
            return new(false, "Example sentence is required.");

        var normalized = VocabularyRules.NormalizePhrase(phrase).ToLowerInvariant();

        if (existingDeck is not null)
        {
            var duplicate = existingDeck.Cards.Any(c =>
                c.Id != editingCardId &&
                string.Equals(c.NormalizedPhrase, normalized, StringComparison.OrdinalIgnoreCase));

            if (duplicate)
                return new(false, "A card with this phrase already exists in this deck.");
        }

        return new(true, null);
    }

    public static AppSettings CreateCustomDeck(AppSettings settings, string name)
    {
        var validation = ValidateCustomDeckName(name);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Error, nameof(name));

        var deck = new CustomFlashcardDeck(Guid.NewGuid(), name.Trim(), []);
        var updatedDecks = settings.CustomFlashcardDecks.Append(deck).ToList();
        return settings with { CustomFlashcardDecks = updatedDecks };
    }

    public static AppSettings RenameCustomDeck(AppSettings settings, Guid deckId, string newName)
    {
        var validation = ValidateCustomDeckName(newName);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Error, nameof(newName));

        var updatedDecks = settings.CustomFlashcardDecks.Select(d =>
            d.Id == deckId ? d with { Name = newName.Trim() } : d).ToList();

        return settings with { CustomFlashcardDecks = updatedDecks };
    }

    public static AppSettings DeleteCustomDeck(AppSettings settings, Guid deckId)
    {
        var prefix = $"custom:{deckId}:";
        var deckKey = CustomDeckKey(deckId);

        var updatedDecks = settings.CustomFlashcardDecks.Where(d => d.Id != deckId).ToList();
        var updatedProgress = settings.FlashcardProgress.Where(p => !p.CardKey.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        var activeDeckKey = settings.ActiveFlashcardDeckKey == deckKey ? null : settings.ActiveFlashcardDeckKey;

        return settings with
        {
            CustomFlashcardDecks = updatedDecks,
            FlashcardProgress = updatedProgress,
            ActiveFlashcardDeckKey = activeDeckKey
        };
    }

    public static AppSettings AddCustomCard(
        AppSettings settings,
        Guid deckId,
        string phrase,
        string? pronunciation,
        string meaning,
        string example)
    {
        var deck = settings.CustomFlashcardDecks.FirstOrDefault(d => d.Id == deckId)
            ?? throw new InvalidOperationException($"Deck {deckId} not found.");

        var validation = ValidateCustomCard(phrase, pronunciation, meaning, example, deck);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Error);

        var normalized = VocabularyRules.NormalizePhrase(phrase).ToLowerInvariant();
        var trimmedPhrase = VocabularyRules.NormalizePhrase(phrase);
        var card = new CustomVocabularyCard(
            Guid.NewGuid(),
            trimmedPhrase,
            normalized,
            string.IsNullOrWhiteSpace(pronunciation) ? null : pronunciation.Trim(),
            meaning.Trim(),
            example.Trim());

        var updatedCards = deck.Cards.Append(card).ToList();
        var updatedDeck = deck with { Cards = updatedCards };

        var updatedDecks = settings.CustomFlashcardDecks.Select(d => d.Id == deckId ? updatedDeck : d).ToList();
        return settings with { CustomFlashcardDecks = updatedDecks };
    }

    public static AppSettings UpdateCustomCard(
        AppSettings settings,
        Guid deckId,
        Guid cardId,
        string phrase,
        string? pronunciation,
        string meaning,
        string example)
    {
        var deck = settings.CustomFlashcardDecks.FirstOrDefault(d => d.Id == deckId)
            ?? throw new InvalidOperationException($"Deck {deckId} not found.");

        var validation = ValidateCustomCard(phrase, pronunciation, meaning, example, deck, cardId);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Error);

        var normalized = VocabularyRules.NormalizePhrase(phrase).ToLowerInvariant();
        var trimmedPhrase = VocabularyRules.NormalizePhrase(phrase);

        var updatedCards = deck.Cards.Select(c => c.Id == cardId
            ? c with
            {
                Phrase = trimmedPhrase,
                NormalizedPhrase = normalized,
                PronunciationIpa = string.IsNullOrWhiteSpace(pronunciation) ? null : pronunciation.Trim(),
                Meaning = meaning.Trim(),
                Example = example.Trim()
            }
            : c).ToList();

        var updatedDeck = deck with { Cards = updatedCards };
        var updatedDecks = settings.CustomFlashcardDecks.Select(d => d.Id == deckId ? updatedDeck : d).ToList();
        return settings with { CustomFlashcardDecks = updatedDecks };
    }

    public static AppSettings DeleteCustomCard(AppSettings settings, Guid deckId, Guid cardId)
    {
        var cardKey = CustomCardKey(deckId, cardId);
        var updatedDecks = settings.CustomFlashcardDecks.Select(d =>
        {
            if (d.Id != deckId) return d;
            return d with { Cards = d.Cards.Where(c => c.Id != cardId).ToList() };
        }).ToList();

        var updatedProgress = settings.FlashcardProgress.Where(p => p.CardKey != cardKey).ToList();
        return settings with
        {
            CustomFlashcardDecks = updatedDecks,
            FlashcardProgress = updatedProgress
        };
    }
}
