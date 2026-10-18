namespace PteFloatingSentence.Core.Tests;

/// <summary>Checking vocabulary after flashcards: typed words, hidden answers, which cards are asked, saved translations.</summary>
[TestClass]
public sealed class VocabularyCheckTests
{
    private static FlashcardItem Card(string phrase, string meaning, FlashcardLearningState state = FlashcardLearningState.New,
        int again = 0, bool done = false, string example = "") =>
        new($"custom:{phrase}", "custom:deck", phrase, phrase.ToLowerInvariant(), null, meaning, example, [], state, 0, again, null, null,
            IsCustom: true, CustomCardId: Guid.NewGuid(), IsMarkedDone: done);

    [TestMethod]
    public void CheckWord_IgnoresCaseSpacesAndPunctuation_AndGivesASecondTryForATypo()
    {
        Assert.AreEqual(WordAnswerResult.Correct, VocabularyCheckRules.CheckWord("Bibliography", "  bibliography. "));
        Assert.AreEqual(WordAnswerResult.Correct, VocabularyCheckRules.CheckWord("carry out", "Carry   out"));
        Assert.AreEqual(WordAnswerResult.Correct, VocabularyCheckRules.CheckWord("well-known", "well known"));
        Assert.AreEqual(WordAnswerResult.Close, VocabularyCheckRules.CheckWord("bibliography", "bibliografy"));
        Assert.AreEqual(WordAnswerResult.Close, VocabularyCheckRules.CheckWord("university", "univeristy"));
        Assert.AreEqual(WordAnswerResult.Wrong, VocabularyCheckRules.CheckWord("cat", "car"), "short words must be exact");
        Assert.AreEqual(WordAnswerResult.Wrong, VocabularyCheckRules.CheckWord("university", "college"));
        Assert.AreEqual(WordAnswerResult.Wrong, VocabularyCheckRules.CheckWord("university", "   "));
    }

    [TestMethod]
    public void MaskPhrase_HidesTheWordAndItsForms()
    {
        Assert.AreEqual("Many _____ offer online courses.", VocabularyCheckRules.MaskPhrase("Many universities offer online courses.", "university"));
        Assert.AreEqual("We _____ the survey.", VocabularyCheckRules.MaskPhrase("We carried out the survey.", "carry out"));
        Assert.AreEqual("A list of sources.", VocabularyCheckRules.MaskPhrase("A list of sources.", "bibliography"));
        Assert.AreEqual(string.Empty, VocabularyCheckRules.MaskPhrase(null, "word"));
    }

    [TestMethod]
    public void PickQuestions_AsksWeakCardsFirst_SkipsDoneCards_AndAlternatesWhenMixed()
    {
        var cards = new[]
        {
            Card("remembered", "m", FlashcardLearningState.Remembered),
            Card("learning", "m", FlashcardLearningState.Learning, again: 3),
            Card("new", "m"),
            Card("done", "m", FlashcardLearningState.Learning, done: true),
            Card("nomeaning", "")
        };

        var two = VocabularyCheckRules.PickQuestions(cards, VocabularyCheckDirection.WordToMeaning, 2, new Random(1));
        CollectionAssert.AreEquivalent(new[] { "learning", "new" }, two.Select(q => q.Card.Phrase).ToArray());
        Assert.IsTrue(two.All(q => q.Direction == VocabularyCheckDirection.WordToMeaning));

        var mixed = VocabularyCheckRules.PickQuestions(cards, VocabularyCheckDirection.Mixed, 10, new Random(1));
        Assert.AreEqual(3, mixed.Count);
        Assert.AreEqual(VocabularyCheckDirection.MeaningToWord, mixed[0].Direction);
        Assert.AreEqual(VocabularyCheckDirection.WordToMeaning, mixed[1].Direction);
    }

    [TestMethod]
    public void Translations_AreSavedPerLanguageAndRedoneWhenTheMeaningChanges()
    {
        var card = Card("bibliography", "A list of books and articles used in a piece of work.");
        var settings = AppSettings.Default with { FlashcardMeaningLanguage = MeaningLanguages.Vietnamese };

        Assert.AreEqual(1, VocabularyCheckRules.MissingTranslations(settings, [card, card], "vi").Count);
        Assert.AreEqual(0, VocabularyCheckRules.MissingTranslations(settings, [card], "en").Count);
        Assert.AreEqual(card.Meaning, VocabularyCheckRules.MeaningIn(settings, card, "en"));

        settings = VocabularyCheckRules.AddTranslations(settings, "vi", [(card, "Danh mục tài liệu tham khảo.")]);
        Assert.AreEqual("Danh mục tài liệu tham khảo.", VocabularyCheckRules.MeaningIn(settings, card, "vi"));
        Assert.AreEqual(0, VocabularyCheckRules.MissingTranslations(settings, [card], "vi").Count);

        var edited = card with { Meaning = "A list of sources." };
        Assert.IsNull(VocabularyCheckRules.MeaningIn(settings, edited, "vi"), "a changed meaning is translated again");

        settings = VocabularyCheckRules.AddTranslations(settings, "vi", [(card, "Thư mục sách.")]);
        Assert.AreEqual("Thư mục sách.", VocabularyCheckRules.MeaningIn(settings, card, "vi"));
        Assert.AreEqual(1, settings.MeaningTranslations.Count, "the newer translation replaces the old one");
    }

    [TestMethod]
    public void Normalize_FixesUnknownValues()
    {
        var settings = VocabularyCheckRules.Normalize(AppSettings.Default with
        {
            FlashcardMeaningLanguage = "klingon",
            VocabularyCheckDirection = (VocabularyCheckDirection)9,
            MeaningTranslations = [null!, new("xx", "a", "b", "c"), new("en", "a", "b", "c"), new("VI", " Word ", "m", " Nghĩa  ")]
        });

        Assert.AreEqual(MeaningLanguages.English, settings.FlashcardMeaningLanguage);
        Assert.AreEqual(VocabularyCheckDirection.MeaningToWord, settings.VocabularyCheckDirection);
        Assert.AreEqual(new MeaningTranslation("vi", "word", "m", "Nghĩa"), settings.MeaningTranslations.Single());
        Assert.AreEqual("vi", VocabularyCheckRules.Normalize(AppSettings.Default with { FlashcardMeaningLanguage = " VI " }).FlashcardMeaningLanguage);
    }

    [TestMethod]
    public void Merge_KeepsTheCheckOptionsAndEveryTranslation()
    {
        var card = Card("word", "meaning");
        var latest = VocabularyCheckRules.AddTranslations(AppSettings.Default, "vi", [(Card("other", "x"), "khác")]);
        var submitted = VocabularyCheckRules.AddTranslations(AppSettings.Default, "vi", [(card, "nghĩa")]) with
        {
            FlashcardMeaningLanguage = "vi",
            VocabularyCheckDirection = VocabularyCheckDirection.Mixed
        };

        var merged = SettingsUpdateMerger.MergeEditableFields(latest, submitted);

        Assert.AreEqual("vi", merged.FlashcardMeaningLanguage);
        Assert.AreEqual(VocabularyCheckDirection.Mixed, merged.VocabularyCheckDirection);
        Assert.AreEqual(2, merged.MeaningTranslations.Count);
    }

    [TestMethod]
    public void RatingFor_MapsAnswersToFlashcardProgress()
    {
        Assert.AreEqual(FlashcardRating.Remembered, VocabularyCheckRules.RatingFor(true, false));
        Assert.AreEqual(FlashcardRating.Hard, VocabularyCheckRules.RatingFor(true, true));
        Assert.AreEqual(FlashcardRating.Again, VocabularyCheckRules.RatingFor(false, false));
    }
}
