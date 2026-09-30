using System.Text.Json;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class FlashcardDomainTests
{
    [TestMethod]
    public void SettingsUpdateMerger_UsesSubmittedStartupPreference()
    {
        var merged = SettingsUpdateMerger.MergeEditableFields(
            AppSettings.Default with { LaunchAtWindowsSignIn = true },
            AppSettings.Default with { LaunchAtWindowsSignIn = false });

        Assert.IsFalse(merged.LaunchAtWindowsSignIn);
    }

    [TestMethod]
    public void StudyListProjection_MergesDuplicatePhrasesAndAggregatesSourceSentences()
    {
        var listId = Guid.NewGuid();
        var sentence1Id = Guid.NewGuid();
        var sentence2Id = Guid.NewGuid();

        var vocab1 = new VocabularyItem(
            Id: Guid.NewGuid(),
            Phrase: "Take Into Account",
            NormalizedPhrase: "take into account",
            Meaning: "To consider something",
            Example: "You should take into account the weather.",
            PronunciationIpa: "/teɪk/",
            Status: VocabularyStatus.Ready);

        var vocab2 = new VocabularyItem(
            Id: Guid.NewGuid(),
            Phrase: "take into account",
            NormalizedPhrase: "take into account",
            Meaning: "Secondary meaning",
            Example: "Secondary example",
            PronunciationIpa: "/teɪk/",
            Status: VocabularyStatus.Ready);

        var sentence1 = new StudySentence(sentence1Id, "Please take into account this factor.", false, [vocab1]);
        var sentence2 = new StudySentence(sentence2Id, "We must take into account all risks.", false, [vocab2]);

        var studyList = new StudyList(listId, "List 1", 2, 0, [sentence1, sentence2]);
        var settings = AppSettings.Default with
        {
            ActiveListId = listId,
            StudyLists = [studyList]
        };

        var deckSummaries = FlashcardDeckProjection.GetDeckSummaries(settings);
        var deckKey = FlashcardRules.StudyListDeckKey(listId);
        var summary = deckSummaries.FirstOrDefault(d => d.DeckKey == deckKey);

        Assert.IsNotNull(summary);
        Assert.AreEqual("List 1", summary.Name);
        Assert.AreEqual(1, summary.ReadyCount);
        Assert.AreEqual(0, summary.UnavailableCount);

        var cards = FlashcardDeckProjection.GetDeckCards(settings, deckKey);
        Assert.AreEqual(1, cards.Count);
        var card = cards[0];
        Assert.AreEqual("Take Into Account", card.Phrase);
        Assert.AreEqual("take into account", card.NormalizedPhrase);
        Assert.AreEqual("To consider something", card.Meaning);
        Assert.AreEqual("You should take into account the weather.", card.Example);
        Assert.AreEqual("/teɪk/", card.PronunciationIpa);
        Assert.AreEqual(2, card.SourceSentences.Count);
        Assert.AreEqual(sentence1Id, card.SourceSentences[0].SentenceId);
        Assert.AreEqual(sentence2Id, card.SourceSentences[1].SentenceId);
        Assert.AreEqual(FlashcardRules.StudyListCardKey(listId, "take into account"), card.CardKey);
        Assert.AreEqual(FlashcardLearningState.New, card.State);
    }

    [TestMethod]
    public void StudyListProjection_IncludesHiddenCardPopupsAndCountsUnavailable()
    {
        var listId = Guid.NewGuid();
        var sId = Guid.NewGuid();

        var hiddenItem = new VocabularyItem(
            Id: Guid.NewGuid(),
            Phrase: "hidden phrase",
            NormalizedPhrase: "hidden phrase",
            Meaning: "hidden",
            Example: "hidden",
            Status: VocabularyStatus.Ready,
            IsHidden: true);

        var pendingItem = new VocabularyItem(
            Id: Guid.NewGuid(),
            Phrase: "pending phrase",
            NormalizedPhrase: "pending phrase",
            Status: VocabularyStatus.Pending);

        var readyItem = new VocabularyItem(
            Id: Guid.NewGuid(),
            Phrase: "ready phrase",
            NormalizedPhrase: "ready phrase",
            Meaning: "ready meaning",
            Example: "ready example",
            Status: VocabularyStatus.Ready);

        var sentence = new StudySentence(sId, "Sample sentence.", false, [hiddenItem, pendingItem, readyItem]);
        var studyList = new StudyList(listId, "Test List", 1, 0, [sentence]);
        var settings = AppSettings.Default with
        {
            StudyLists = [studyList]
        };

        var deckKey = FlashcardRules.StudyListDeckKey(listId);
        var summaries = FlashcardDeckProjection.GetDeckSummaries(settings);
        var summary = summaries.First(s => s.DeckKey == deckKey);

        Assert.AreEqual(2, summary.ReadyCount);
        Assert.AreEqual(1, summary.UnavailableCount); // pendingItem is unavailable
        Assert.AreEqual(3, summary.TotalCount); // 2 ready (including hidden popup) + 1 unavailable

        var readyCards = FlashcardDeckProjection.GetDeckCards(settings, deckKey);
        Assert.AreEqual(2, readyCards.Count);
        Assert.IsTrue(readyCards.Any(c => c.Phrase == "hidden phrase"));
        Assert.IsTrue(readyCards.Any(c => c.Phrase == "ready phrase"));

        var allCards = FlashcardDeckProjection.GetDeckCards(settings, deckKey, includeUnavailable: true);
        Assert.AreEqual(3, allCards.Count);
        var pendingCard = allCards.First(c => c.Phrase == "pending phrase");
        Assert.IsFalse(pendingCard.IsReady);
        Assert.AreEqual("Pending explanation...", pendingCard.UnavailableReason);
    }

    [TestMethod]
    public void StudyListProjection_UsesSourceSentenceWhenExampleMissing()
    {
        var listId = Guid.NewGuid();
        var sId = Guid.NewGuid();

        var itemWithoutExample = new VocabularyItem(
            Id: Guid.NewGuid(),
            Phrase: "curious",
            NormalizedPhrase: "curious",
            Meaning: "eager to know",
            Example: null,
            Status: VocabularyStatus.Ready);

        var sentence = new StudySentence(sId, "She was curious about the ancient map.", false, [itemWithoutExample]);
        var studyList = new StudyList(listId, "Curious List", 1, 0, [sentence]);
        var settings = AppSettings.Default with { StudyLists = [studyList] };

        var deckKey = FlashcardRules.StudyListDeckKey(listId);
        var cards = FlashcardDeckProjection.GetDeckCards(settings, deckKey);

        Assert.AreEqual(1, cards.Count);
        Assert.AreEqual("curious", cards[0].Phrase);
        Assert.AreEqual("eager to know", cards[0].Meaning);
        Assert.AreEqual("She was curious about the ancient map.", cards[0].Example);
    }

    [TestMethod]
    public void CustomDeck_RejectsDuplicateNormalizedPhrase()
    {
        var deckId = Guid.NewGuid();
        var card1 = new CustomVocabularyCard(
            Id: Guid.NewGuid(),
            Phrase: "Ephemeral",
            NormalizedPhrase: "ephemeral",
            PronunciationIpa: "/ɪˈfɛmərəl/",
            Meaning: "Lasting for a very short time",
            Example: "Fashions are ephemeral.");

        var deck = new CustomFlashcardDeck(deckId, "My Deck", [card1]);

        var validationValid = FlashcardRules.ValidateCustomCard("Serendipity", "/ˌsɛrənˈdɪpɪti/", "Good luck", "A serendipitous encounter", deck);
        Assert.IsTrue(validationValid.IsValid);

        var validationDuplicate = FlashcardRules.ValidateCustomCard("  EPHEMERAL  ", null, "Different meaning", "Different example", deck);
        Assert.IsFalse(validationDuplicate.IsValid);
        Assert.IsTrue(validationDuplicate.Error?.Contains("already exists", StringComparison.OrdinalIgnoreCase));

        // When editing the same card, its own phrase should be valid
        var validationSelfEdit = FlashcardRules.ValidateCustomCard("ephemeral", null, "Updated meaning", "Updated example", deck, editingCardId: card1.Id);
        Assert.IsTrue(validationSelfEdit.IsValid);
    }

    [TestMethod]
    public void CustomDeck_RequiresPhraseMeaningAndExample()
    {
        var deck = new CustomFlashcardDeck(Guid.NewGuid(), "Deck", []);

        Assert.IsFalse(FlashcardRules.ValidateCustomCard("", null, "Meaning", "Example", deck).IsValid);
        Assert.IsFalse(FlashcardRules.ValidateCustomCard("Phrase", null, "   ", "Example", deck).IsValid);
        Assert.IsFalse(FlashcardRules.ValidateCustomCard("Phrase", null, "Meaning", "", deck).IsValid);
        Assert.IsTrue(FlashcardRules.ValidateCustomCard("Phrase", null, "Meaning", "Example", deck).IsValid);
    }

    [TestMethod]
    public void FlashcardRating_TransitionsStateCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var cardKey = "test-card-1";

        // Initial state: New
        var progress0 = new FlashcardProgress(cardKey, FlashcardLearningState.New, 0, 0, null, null);

        // 1. Rate Hard -> Learning, ReviewCount=1, AgainCount=0
        var p1 = FlashcardRules.ApplyRating(progress0, FlashcardRating.Hard, now);
        Assert.AreEqual(FlashcardLearningState.Learning, p1.State);
        Assert.AreEqual(1, p1.ReviewCount);
        Assert.AreEqual(0, p1.AgainCount);
        Assert.AreEqual(FlashcardRating.Hard, p1.LastRating);
        Assert.AreEqual(now, p1.LastReviewedAt);

        // 2. Rate Again -> Learning, ReviewCount=2, AgainCount=1
        var later1 = now.AddMinutes(5);
        var p2 = FlashcardRules.ApplyRating(p1, FlashcardRating.Again, later1);
        Assert.AreEqual(FlashcardLearningState.Learning, p2.State);
        Assert.AreEqual(2, p2.ReviewCount);
        Assert.AreEqual(1, p2.AgainCount);
        Assert.AreEqual(FlashcardRating.Again, p2.LastRating);

        // 3. Rate Remembered -> Remembered, ReviewCount=3, AgainCount=1
        var later2 = now.AddMinutes(10);
        var p3 = FlashcardRules.ApplyRating(p2, FlashcardRating.Remembered, later2);
        Assert.AreEqual(FlashcardLearningState.Remembered, p3.State);
        Assert.AreEqual(3, p3.ReviewCount);
        Assert.AreEqual(1, p3.AgainCount);
        Assert.AreEqual(FlashcardRating.Remembered, p3.LastRating);

        // 4. Rate Again on Remembered card -> transitions back to Learning
        var later3 = now.AddMinutes(15);
        var p4 = FlashcardRules.ApplyRating(p3, FlashcardRating.Again, later3);
        Assert.AreEqual(FlashcardLearningState.Learning, p4.State);
        Assert.AreEqual(4, p4.ReviewCount);
        Assert.AreEqual(2, p4.AgainCount);
    }

    [TestMethod]
    public void NormalizeProgress_DeduplicatesAndPrefersLatestEntry()
    {
        var cardKey1 = "card-1";
        var cardKey2 = "card-2";
        var t1 = new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 9, 18, 11, 0, 0, TimeSpan.Zero);

        var p1Old = new FlashcardProgress(cardKey1, FlashcardLearningState.Learning, 1, 1, FlashcardRating.Again, t1);
        var p1New = new FlashcardProgress(cardKey1, FlashcardLearningState.Remembered, 2, 1, FlashcardRating.Remembered, t2);
        var p2 = new FlashcardProgress(cardKey2, FlashcardLearningState.Learning, 1, 0, FlashcardRating.Hard, t1);

        var list = new[] { p1Old, p2, p1New };
        var normalized = FlashcardRules.NormalizeProgress(list);

        Assert.AreEqual(2, normalized.Count);
        var result1 = normalized.First(p => p.CardKey == cardKey1);
        Assert.AreEqual(FlashcardLearningState.Remembered, result1.State);
        Assert.AreEqual(2, result1.ReviewCount);
        Assert.AreEqual(t2, result1.LastReviewedAt);
    }

    [TestMethod]
    public void AppSettings_LegacyJsonDeserialization_InitializesDefaults()
    {
        var legacyJson = """
        {
          "Version": 2,
          "Sentence": "Test sentence",
          "FontSize": 24,
          "TextColor": "#FFFFFFFF",
          "BackgroundOpacity": 0.5,
          "ShowSentenceOverlay": true,
          "ShowVocabularyCards": true
        }
        """;

        var settings = JsonSerializer.Deserialize<AppSettings>(legacyJson);
        Assert.IsNotNull(settings);
        Assert.AreEqual("Test sentence", settings.Sentence);
        Assert.IsNotNull(settings.CustomFlashcardDecks);
        Assert.AreEqual(0, settings.CustomFlashcardDecks.Count);
        Assert.IsNotNull(settings.FlashcardProgress);
        Assert.AreEqual(0, settings.FlashcardProgress.Count);
        Assert.IsFalse(settings.ShowFloatingFlashcard);
        Assert.IsNull(settings.ActiveFlashcardDeckKey);
    }

    [TestMethod]
    public void SettingsUpdateMerger_PreservesConcurrentFlashcardRatings()
    {
        var t1 = DateTimeOffset.UtcNow;
        var initialSettings = AppSettings.Default with
        {
            Sentence = "Original sentence",
            FontSize = 20,
            FlashcardProgress = [new FlashcardProgress("card-1", FlashcardLearningState.Learning, 1, 0, FlashcardRating.Hard, t1)]
        };

        // Meanwhile, floating flashcard rates card-1 to Remembered
        var updatedLatest = initialSettings with
        {
            FlashcardProgress = [new FlashcardProgress("card-1", FlashcardLearningState.Remembered, 2, 0, FlashcardRating.Remembered, t1.AddMinutes(1))]
        };

        // User in Settings UI submits form that was loaded from initialSettings
        var submittedSettings = initialSettings with
        {
            Sentence = "Updated sentence",
            FontSize = 32
        };

        var merged = SettingsUpdateMerger.MergeEditableFields(updatedLatest, submittedSettings);

        Assert.AreEqual("Updated sentence", merged.Sentence);
        Assert.AreEqual(32, merged.FontSize);
        // Latest progress should be preserved!
        Assert.AreEqual(1, merged.FlashcardProgress.Count);
        Assert.AreEqual(FlashcardLearningState.Remembered, merged.FlashcardProgress[0].State);
        Assert.AreEqual(2, merged.FlashcardProgress[0].ReviewCount);
    }

    [TestMethod]
    public void ValidateCustomCard_RejectsPunctuationEquivalentPhrase()
    {
        var existing = new CustomVocabularyCard(
            Guid.NewGuid(),
            "business",
            "business",
            null,
            "A company or commercial activity.",
            "She runs a small business.");
        var deck = new CustomFlashcardDeck(Guid.NewGuid(), "Words", [existing]);

        var result = FlashcardRules.ValidateCustomCard(
            "business,",
            null,
            "Commercial activity.",
            "The business is growing.",
            deck);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.Error, "already exists");
    }

    [TestMethod]
    public void ValidateCustomCard_RejectsEquivalentLegacyNormalizedPhrase()
    {
        var legacyCard = new CustomVocabularyCard(
            Guid.NewGuid(),
            "business,",
            "business,",
            null,
            "A company or commercial activity.",
            "She runs a small business.");
        var deck = new CustomFlashcardDeck(Guid.NewGuid(), "Words", [legacyCard]);

        var result = FlashcardRules.ValidateCustomCard(
            "business",
            null,
            "Commercial activity.",
            "The business is growing.",
            deck);

        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void StudyListProjection_PreservesProgressStoredUnderLegacyPunctuationKey()
    {
        var listId = Guid.NewGuid();
        var legacyVocabulary = new VocabularyItem(
            Guid.NewGuid(),
            "business,",
            "business,",
            "A company or commercial activity.",
            "She runs a small business.",
            null,
            VocabularyStatus.Ready);
        var list = new StudyList(
            listId,
            "List",
            1,
            0,
            [new StudySentence(Guid.NewGuid(), "She runs a business.", Vocabulary: [legacyVocabulary])]);
        var legacyKey = FlashcardRules.StudyListCardKey(listId, "business,");
        var settings = AppSettings.Default with
        {
            ActiveListId = listId,
            StudyLists = [list],
            FlashcardProgress =
            [
                new FlashcardProgress(
                    legacyKey,
                    FlashcardLearningState.Remembered,
                    3,
                    0,
                    FlashcardRating.Remembered,
                    DateTimeOffset.UtcNow)
            ]
        };

        var card = FlashcardDeckProjection.GetDeckCards(
            settings,
            FlashcardRules.StudyListDeckKey(listId)).Single();

        Assert.AreEqual(FlashcardRules.StudyListCardKey(listId, "business"), card.CardKey);
        Assert.AreEqual(FlashcardLearningState.Remembered, card.State);
        Assert.AreEqual(3, card.ReviewCount);
    }

    [TestMethod]
    public void CustomDeck_CrudAndProgressCleanup()
    {
        var settings = AppSettings.Default;

        // 1. Create deck
        settings = FlashcardRules.CreateCustomDeck(settings, "Idioms");
        Assert.AreEqual(1, settings.CustomFlashcardDecks.Count);
        var deck = settings.CustomFlashcardDecks[0];
        Assert.AreEqual("Idioms", deck.Name);

        // 2. Add card
        settings = FlashcardRules.AddCustomCard(settings, deck.Id, "Bite the bullet", "/baɪt/", "Face a difficult situation with courage", "I had to bite the bullet.");
        deck = settings.CustomFlashcardDecks[0];
        Assert.AreEqual(1, deck.Cards.Count);
        var card = deck.Cards[0];
        Assert.AreEqual("Bite the bullet", card.Phrase);
        Assert.AreEqual("bite the bullet", card.NormalizedPhrase);

        // 3. Rate card
        var cardKey = FlashcardRules.CustomCardKey(deck.Id, card.Id);
        settings = FlashcardRules.ApplyRating(settings, cardKey, FlashcardRating.Remembered, DateTimeOffset.UtcNow);
        Assert.AreEqual(1, settings.FlashcardProgress.Count);

        // 4. Update card
        settings = FlashcardRules.UpdateCustomCard(settings, deck.Id, card.Id, "Bite the bullet", "/baɪt/", "Updated meaning", "Updated example");
        deck = settings.CustomFlashcardDecks[0];
        Assert.AreEqual("Updated meaning", deck.Cards[0].Meaning);

        // 5. Rename deck
        settings = FlashcardRules.RenameCustomDeck(settings, deck.Id, "Essential Idioms");
        Assert.AreEqual("Essential Idioms", settings.CustomFlashcardDecks[0].Name);

        // 6. Delete card cleans up its progress
        settings = FlashcardRules.DeleteCustomCard(settings, deck.Id, card.Id);
        Assert.AreEqual(0, settings.CustomFlashcardDecks[0].Cards.Count);
        Assert.AreEqual(0, settings.FlashcardProgress.Count);

        // 7. Delete deck cleans up active deck key if matching
        var deckKey = FlashcardRules.CustomDeckKey(deck.Id);
        settings = settings with { ActiveFlashcardDeckKey = deckKey };
        settings = FlashcardRules.DeleteCustomDeck(settings, deck.Id);
        Assert.AreEqual(0, settings.CustomFlashcardDecks.Count);
        Assert.IsNull(settings.ActiveFlashcardDeckKey);
    }

    [TestMethod]
    public void CustomDeck_Projection_ReturnsReadyCardsAndSummaries()
    {
        var settings = AppSettings.Default;
        settings = FlashcardRules.CreateCustomDeck(settings, "Vocab 1");
        var deckId = settings.CustomFlashcardDecks[0].Id;
        settings = FlashcardRules.AddCustomCard(settings, deckId, "Ephemeral", null, "Short-lived", "Ephemeral fashion");

        var deckKey = FlashcardRules.CustomDeckKey(deckId);
        var summaries = FlashcardDeckProjection.GetDeckSummaries(settings);
        var summary = summaries.First(s => s.DeckKey == deckKey);
        Assert.AreEqual("Vocab 1", summary.Name);
        Assert.AreEqual(1, summary.TotalCount);
        Assert.AreEqual(1, summary.ReadyCount);
        Assert.AreEqual(0, summary.RememberedCount);

        var cards = FlashcardDeckProjection.GetDeckCards(settings, deckKey);
        Assert.AreEqual(1, cards.Count);
        Assert.AreEqual("Ephemeral", cards[0].Phrase);
        Assert.IsTrue(cards[0].IsCustom);
    }

    [TestMethod]
    public void SetMarkedDone_TogglesCardDoneState_AndPersistsInSettings()
    {
        var settings = AppSettings.Default;
        var cardKey = "deck:test:card1";
        var now = DateTimeOffset.UtcNow;

        // Initially not done
        Assert.IsNull(settings.FlashcardProgress.FirstOrDefault(p => p.CardKey == cardKey));

        // Mark done
        settings = FlashcardRules.SetMarkedDone(settings, cardKey, true, now);
        var progress = settings.FlashcardProgress.FirstOrDefault(p => p.CardKey == cardKey);
        Assert.IsNotNull(progress);
        Assert.IsTrue(progress.IsMarkedDone);
        Assert.AreEqual(now, progress.LastReviewedAt);

        // Restore / Unmark done
        var later = now.AddMinutes(5);
        settings = FlashcardRules.SetMarkedDone(settings, cardKey, false, later);
        progress = settings.FlashcardProgress.FirstOrDefault(p => p.CardKey == cardKey);
        Assert.IsNotNull(progress);
        Assert.IsFalse(progress.IsMarkedDone);
        Assert.AreEqual(later, progress.LastReviewedAt);
    }

    [TestMethod]
    public void DeckProjection_WithOnlyActive_ExcludesMarkedDoneCards_AndComputesDoneCount()
    {
        var listId = Guid.NewGuid();
        var sId = Guid.NewGuid();
        var vocab1 = new VocabularyItem(Guid.NewGuid(), "phrase one", "phrase one", "m1", "e1", null, VocabularyStatus.Ready);
        var vocab2 = new VocabularyItem(Guid.NewGuid(), "phrase two", "phrase two", "m2", "e2", null, VocabularyStatus.Ready);
        var sentence = new StudySentence(sId, "Two phrases.", false, [vocab1, vocab2]);
        var studyList = new StudyList(listId, "List With Done", 1, 0, [sentence]);

        var settings = AppSettings.Default with
        {
            ActiveListId = listId,
            StudyLists = [studyList]
        };

        var deckKey = FlashcardRules.StudyListDeckKey(listId);
        var cardKey1 = FlashcardRules.StudyListCardKey(listId, "phrase one");

        // Mark card 1 as done
        settings = FlashcardRules.SetMarkedDone(settings, cardKey1, true, DateTimeOffset.UtcNow);

        // Deck summaries should show DoneCount = 1
        var summaries = FlashcardDeckProjection.GetDeckSummaries(settings);
        var summary = summaries.First(s => s.DeckKey == deckKey);
        Assert.AreEqual(2, summary.TotalCount);
        Assert.AreEqual(2, summary.ReadyCount);
        Assert.AreEqual(1, summary.DoneCount);

        // GetDeckCards with onlyActive: false returns both, with IsMarkedDone correctly set
        var allCards = FlashcardDeckProjection.GetDeckCards(settings, deckKey, onlyActive: false);
        Assert.AreEqual(2, allCards.Count);
        var card1 = allCards.First(c => c.Phrase == "phrase one");
        var card2 = allCards.First(c => c.Phrase == "phrase two");
        Assert.IsTrue(card1.IsMarkedDone);
        Assert.IsFalse(card2.IsMarkedDone);

        // GetDeckCards with onlyActive: true returns only card 2
        var activeCards = FlashcardDeckProjection.GetDeckCards(settings, deckKey, onlyActive: true);
        Assert.AreEqual(1, activeCards.Count);
        Assert.AreEqual("phrase two", activeCards[0].Phrase);
    }
}
