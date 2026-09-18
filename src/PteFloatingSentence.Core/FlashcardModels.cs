namespace PteFloatingSentence.Core;

public enum FlashcardLearningState
{
    New = 0,
    Learning = 1,
    Remembered = 2
}

public enum FlashcardRating
{
    Again = 0,
    Hard = 1,
    Remembered = 2
}

public sealed record CustomVocabularyCard(
    Guid Id,
    string Phrase,
    string NormalizedPhrase,
    string? PronunciationIpa,
    string Meaning,
    string Example,
    string? ImagePath = null);

public sealed record CustomFlashcardDeck(
    Guid Id,
    string Name,
    IReadOnlyList<CustomVocabularyCard> Cards);

public sealed record FlashcardProgress(
    string CardKey,
    FlashcardLearningState State,
    int ReviewCount,
    int AgainCount,
    FlashcardRating? LastRating,
    DateTimeOffset? LastReviewedAt);

public sealed record FlashcardSourceSentence(
    Guid SentenceId,
    string Text);

public sealed record FlashcardItem(
    string CardKey,
    string DeckKey,
    string Phrase,
    string NormalizedPhrase,
    string? PronunciationIpa,
    string Meaning,
    string Example,
    IReadOnlyList<FlashcardSourceSentence> SourceSentences,
    FlashcardLearningState State,
    int ReviewCount,
    int AgainCount,
    FlashcardRating? LastRating,
    DateTimeOffset? LastReviewedAt,
    bool IsCustom,
    Guid? CustomCardId,
    bool IsReady = true,
    string? UnavailableReason = null);

public sealed record FlashcardDeckSummary(
    string DeckKey,
    Guid? SourceId,
    string Name,
    bool IsCustom,
    int TotalCount,
    int ReadyCount,
    int UnavailableCount,
    int RememberedCount);
