using System.Windows;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
[DoNotParallelize]
public class FlashcardUiTests
{
    [TestMethod]
    public void FloatingFlashcardWindow_InitialState_FrontVisible_RatingsDisabled()
    {
        RunOnSta(() =>
        {
            var window = new FloatingFlashcardWindow();
            var card = new FlashcardItem(
                CardKey: "test:1",
                DeckKey: "deck:1",
                Phrase: "Ephemeral",
                NormalizedPhrase: "ephemeral",
                PronunciationIpa: "/ɪˈfɛmərəl/",
                Meaning: "Lasting for a very short time",
                Example: "Fashions are ephemeral.",
                SourceSentences: [],
                State: FlashcardLearningState.New,
                ReviewCount: 0,
                AgainCount: 0,
                LastRating: null,
                LastReviewedAt: null,
                IsCustom: true,
                CustomCardId: Guid.NewGuid());

            window.SetDeck("Test Deck", [card]);

            Assert.AreEqual(0, window.CurrentIndex);
            Assert.IsFalse(window.IsShowingBack);
            Assert.AreEqual(Visibility.Visible, window.FrontPanel.Visibility);
            Assert.AreEqual(Visibility.Collapsed, window.BackPanel.Visibility);
            Assert.IsFalse(window.ButtonAgain.IsEnabled);
            Assert.IsFalse(window.ButtonHard.IsEnabled);
            Assert.IsFalse(window.ButtonRemembered.IsEnabled);

            window.Close();
        });
    }

    [TestMethod]
    public void FloatingFlashcardWindow_Flip_ShowsBackAndEnablesRatings()
    {
        RunOnSta(() =>
        {
            var window = new FloatingFlashcardWindow();
            var card = new FlashcardItem(
                CardKey: "test:1",
                DeckKey: "deck:1",
                Phrase: "Ephemeral",
                NormalizedPhrase: "ephemeral",
                PronunciationIpa: "/ɪˈfɛmərəl/",
                Meaning: "Lasting for a very short time",
                Example: "Fashions are ephemeral.",
                SourceSentences: [],
                State: FlashcardLearningState.New,
                ReviewCount: 0,
                AgainCount: 0,
                LastRating: null,
                LastReviewedAt: null,
                IsCustom: true,
                CustomCardId: Guid.NewGuid());

            window.SetDeck("Test Deck", [card]);

            window.Flip();

            Assert.IsTrue(window.IsShowingBack);
            Assert.AreEqual(Visibility.Collapsed, window.FrontPanel.Visibility);
            Assert.AreEqual(Visibility.Visible, window.BackPanel.Visibility);
            Assert.IsTrue(window.ButtonAgain.IsEnabled);
            Assert.IsTrue(window.ButtonHard.IsEnabled);
            Assert.IsTrue(window.ButtonRemembered.IsEnabled);

            // Flip back
            window.Flip();
            Assert.IsFalse(window.IsShowingBack);
            Assert.IsFalse(window.ButtonAgain.IsEnabled);

            window.Close();
        });
    }

    [TestMethod]
    public void FloatingFlashcardWindow_RatingAdvancesAndEmitsEvent()
    {
        RunOnSta(() =>
        {
            var window = new FloatingFlashcardWindow();
            var card1 = new FlashcardItem("k1", "d1", "P1", "p1", null, "M1", "E1", [], FlashcardLearningState.New, 0, 0, null, null, true, null);
            var card2 = new FlashcardItem("k2", "d1", "P2", "p2", null, "M2", "E2", [], FlashcardLearningState.New, 0, 0, null, null, true, null);

            window.SetDeck("Deck", [card1, card2]);

            string? ratedKey = null;
            FlashcardRating? ratedValue = null;
            window.CardRated += (_, args) =>
            {
                ratedKey = args.CardKey;
                ratedValue = args.Rating;
            };

            // Cannot rate while front is visible
            window.SubmitRating(FlashcardRating.Remembered);
            Assert.IsNull(ratedKey);

            // Flip then rate
            window.Flip();
            window.SubmitRating(FlashcardRating.Remembered);

            Assert.AreEqual("k1", ratedKey);
            Assert.AreEqual(FlashcardRating.Remembered, ratedValue);
            Assert.AreEqual(1, window.CurrentIndex);
            Assert.IsFalse(window.IsShowingBack); // Resets to front on advance

            // Rate card 2 (last card) -> wraps to 0 and increments cycle count
            window.Flip();
            window.SubmitRating(FlashcardRating.Again);

            Assert.AreEqual("k2", ratedKey);
            Assert.AreEqual(FlashcardRating.Again, ratedValue);
            Assert.AreEqual(0, window.CurrentIndex);
            Assert.AreEqual(1, window.CycleCount);

            window.Close();
        });
    }

    [TestMethod]
    public void DisplayController_LayerMatrix_SentenceOverlayAndFlashcard()
    {
        RunOnSta(() =>
        {
            var floating = new FloatingWindow();
            var launcher = new OverlayLauncherWindow();
            using var controller = new DisplayController(floating, launcher);

            var customDeck = new CustomFlashcardDeck(
                Guid.NewGuid(),
                "My Deck",
                [new CustomVocabularyCard(Guid.NewGuid(), "Word", "word", null, "Meaning", "Example")]);

            var deckKey = FlashcardRules.CustomDeckKey(customDeck.Id);

            var baseSettings = AppSettings.Default with
            {
                CustomFlashcardDecks = [customDeck],
                ActiveFlashcardDeckKey = deckKey
            };

            // Case 1: Both enabled -> Sentence and Flashcard visible, Launcher hidden
            controller.Apply(baseSettings with { ShowSentenceOverlay = true, ShowFloatingFlashcard = true });
            Assert.AreEqual(Visibility.Visible, floating.Visibility);
            Assert.IsNotNull(controller.FlashcardWindow);
            Assert.AreEqual(Visibility.Visible, controller.FlashcardWindow.Visibility);
            Assert.AreEqual(Visibility.Hidden, launcher.Visibility);

            // Case 2: Sentence hidden, Flashcard visible -> Sentence hidden, Flashcard visible, Launcher hidden!
            controller.Apply(baseSettings with { ShowSentenceOverlay = false, ShowFloatingFlashcard = true });
            Assert.AreEqual(Visibility.Hidden, floating.Visibility);
            Assert.AreEqual(Visibility.Visible, controller.FlashcardWindow.Visibility);
            Assert.AreEqual(Visibility.Hidden, launcher.Visibility);

            // Case 3: Both hidden -> Launcher visible!
            controller.Apply(baseSettings with { ShowSentenceOverlay = false, ShowFloatingFlashcard = false });
            Assert.AreEqual(Visibility.Hidden, floating.Visibility);
            Assert.AreEqual(Visibility.Hidden, controller.FlashcardWindow.Visibility);
            Assert.AreEqual(Visibility.Visible, launcher.Visibility);

            // Case 4: Sentence visible, Flashcard hidden -> Launcher hidden
            controller.Apply(baseSettings with { ShowSentenceOverlay = true, ShowFloatingFlashcard = false });
            Assert.AreEqual(Visibility.Visible, floating.Visibility);
            Assert.AreEqual(Visibility.Hidden, controller.FlashcardWindow.Visibility);
            Assert.AreEqual(Visibility.Hidden, launcher.Visibility);
        });
    }

    [TestMethod]
    public void DisplayController_LazyCreationOfFlashcardWindow()
    {
        RunOnSta(() =>
        {
            var floating = new FloatingWindow();
            var launcher = new OverlayLauncherWindow();
            using var controller = new DisplayController(floating, launcher);

            // Before enabling flashcard, window is not created
            Assert.IsNull(controller.FlashcardWindow);

            var customDeck = new CustomFlashcardDeck(
                Guid.NewGuid(),
                "Deck",
                [new CustomVocabularyCard(Guid.NewGuid(), "Word", "word", null, "Meaning", "Example")]);

            controller.Apply(AppSettings.Default with
            {
                ShowSentenceOverlay = true,
                ShowFloatingFlashcard = false,
                CustomFlashcardDecks = [customDeck]
            });

            Assert.IsNull(controller.FlashcardWindow);
        });
    }

    [TestMethod]
    public void DisplayController_EnablingWithoutValidDeck_RequestsSettingsAndShowsLauncher()
    {
        RunOnSta(() =>
        {
            var floating = new FloatingWindow();
            var launcher = new OverlayLauncherWindow();
            using var controller = new DisplayController(floating, launcher);

            var openSettingsRequested = false;
            controller.OpenFlashcardsRequested += (_, _) => openSettingsRequested = true;

            // Enable floating flashcard without active deck
            controller.Apply(AppSettings.Default with
            {
                ShowSentenceOverlay = false,
                ShowFloatingFlashcard = true,
                ActiveFlashcardDeckKey = null
            });

            Assert.IsTrue(openSettingsRequested);
            Assert.AreEqual(Visibility.Visible, launcher.Visibility);
        });
    }

    [TestMethod]
    public void FlashcardsPage_LoadAndSelectDeck_RendersCardsAndStats()
    {
        RunOnSta(() =>
        {
            var page = new FlashcardsPage();
            var customDeck = new CustomFlashcardDeck(
                Guid.NewGuid(),
                "Vocabulary 101",
                [new CustomVocabularyCard(Guid.NewGuid(), "Resilient", "resilient", "/rɪˈzɪljənt/", "Able to recover quickly", "She is remarkably resilient.")]);

            var deckKey = FlashcardRules.CustomDeckKey(customDeck.Id);
            var settings = AppSettings.Default with
            {
                CustomFlashcardDecks = [customDeck],
                ActiveFlashcardDeckKey = deckKey
            };

            page.LoadSettings(settings);

            Assert.IsNotNull(page.SelectedDeckKey);
            Assert.AreEqual(deckKey, page.SelectedDeckKey);
            Assert.AreEqual("Vocabulary 101", page.CurrentDeckName);
            Assert.IsTrue(page.StartButton.IsEnabled);
        });
    }

    [TestMethod]
    public void FlashcardsPage_InPageStudy_FlipAndRateAdvancesCard()
    {
        RunOnSta(() =>
        {
            var page = new FlashcardsPage();
            var card1 = new CustomVocabularyCard(Guid.NewGuid(), "Card1", "card1", null, "Meaning 1", "Example 1");
            var card2 = new CustomVocabularyCard(Guid.NewGuid(), "Card2", "card2", null, "Meaning 2", "Example 2");
            var customDeck = new CustomFlashcardDeck(Guid.NewGuid(), "Deck", [card1, card2]);
            var deckKey = FlashcardRules.CustomDeckKey(customDeck.Id);

            AppSettings? savedSettings = null;
            var settings = AppSettings.Default with
            {
                CustomFlashcardDecks = [customDeck],
                ActiveFlashcardDeckKey = deckKey
            };

            page.LoadSettings(settings, s => savedSettings = s);

            // Start in-page study
            page.StartInPageStudy();
            Assert.IsTrue(page.IsStudying);
            Assert.AreEqual(Visibility.Visible, page.StudyView.Visibility);
            Assert.AreEqual(Visibility.Collapsed, page.ManagementView.Visibility);

            // Front is shown initially, ratings disabled
            Assert.IsFalse(page.ButtonAgain.IsEnabled);
            Assert.IsFalse(page.ButtonRemembered.IsEnabled);

            // Flip
            page.FlipStudyCard();
            Assert.IsTrue(page.ButtonRemembered.IsEnabled);

            // Rate Remembered
            page.SubmitStudyRating(FlashcardRating.Remembered);

            // Verify settings saved with updated progress
            Assert.IsNotNull(savedSettings);
            Assert.AreEqual(1, savedSettings.FlashcardProgress.Count);
            Assert.AreEqual(FlashcardLearningState.Remembered, savedSettings.FlashcardProgress[0].State);

            // Card advanced to Card 2, front face visible again
            Assert.AreEqual("Card2", page.CurrentStudyPhrase);
            Assert.IsFalse(page.ButtonRemembered.IsEnabled);

            // Exit study
            page.ExitInPageStudy();
            Assert.IsFalse(page.IsStudying);
            Assert.AreEqual(Visibility.Collapsed, page.StudyView.Visibility);
            Assert.AreEqual(Visibility.Visible, page.ManagementView.Visibility);
        });
    }

    [TestMethod]
    public void SettingsWindow_NavigateToFlashcards_SelectsPage()
    {
        RunOnSta(() =>
        {
            var settings = AppSettings.Default;
            var window = new SettingsWindow(settings, _ => { });

            window.OpenFlashcards();

            Assert.AreEqual(SettingsPageId.Flashcards, window.SelectedPage);
            Assert.AreEqual(Visibility.Visible, window.SectionFlashcards.Visibility);

            window.Close();
        });
    }

    [TestMethod]
    public void FloatingFlashcardWindow_MarkDone_EmitsCardMarkedDoneRequested()
    {
        RunOnSta(() =>
        {
            var window = new FloatingFlashcardWindow();
            var card1 = new FlashcardItem("card:key:1", "d1", "P1", "p1", null, "M1", "E1", [], FlashcardLearningState.New, 0, 0, null, null, true, null);
            window.SetDeck("Deck", [card1]);

            Assert.IsNotNull(window.ButtonMarkDone);

            string? markedKey = null;
            window.CardMarkedDoneRequested += (_, key) => markedKey = key;

            window.MarkCurrentCardDone();

            Assert.AreEqual("card:key:1", markedKey);

            window.Close();
        });
    }

    [TestMethod]
    public void DisplayController_CardMarkedDone_ForwardsEventAndFiltersOutCardOnApply()
    {
        RunOnSta(() =>
        {
            var floatingWindow = new FloatingWindow();
            var flashcardWindow = new FloatingFlashcardWindow();
            var controller = new DisplayController(
                floatingWindow,
                flashcardWindow: flashcardWindow);

            var listId = Guid.NewGuid();
            var vocab1 = new VocabularyItem(Guid.NewGuid(), "p1", "p1", "m1", "e1", null, VocabularyStatus.Ready);
            var vocab2 = new VocabularyItem(Guid.NewGuid(), "p2", "p2", "m2", "e2", null, VocabularyStatus.Ready);
            var sentence = new StudySentence(Guid.NewGuid(), "Sentence.", false, [vocab1, vocab2]);
            var list = new StudyList(listId, "List", 1, 0, [sentence]);
            var deckKey = FlashcardRules.StudyListDeckKey(listId);

            var settings = AppSettings.Default with
            {
                ActiveListId = listId,
                ActiveFlashcardDeckKey = deckKey,
                ShowFloatingFlashcard = true,
                StudyLists = [list]
            };

            controller.Apply(settings);
            Assert.AreEqual(2, flashcardWindow.CardCount);

            string? reportedDoneKey = null;
            controller.CardMarkedDone += (_, key) => reportedDoneKey = key;

            flashcardWindow.MarkCurrentCardDone();
            Assert.IsNotNull(reportedDoneKey);

            // Update settings with marked done
            settings = FlashcardRules.SetMarkedDone(settings, reportedDoneKey, true, DateTimeOffset.UtcNow);
            controller.Apply(settings);

            // Floating overlay now only contains the 1 active card
            Assert.AreEqual(1, flashcardWindow.CardCount);
            Assert.AreNotEqual(reportedDoneKey, flashcardWindow.CurrentCard?.CardKey);

            controller.Dispose();
            floatingWindow.Close();
        });
    }

    [TestMethod]
    public void SettingsWindow_FlashcardsPage_DisablesOuterScrollViewer_ToKeepDeckSidebarPinned()
    {
        RunOnSta(() =>
        {
            var window = new SettingsWindow(AppSettings.Default, _ => { });
            var scrollViewer = window.FindName("PageScrollViewer") as System.Windows.Controls.ScrollViewer;
            Assert.IsNotNull(scrollViewer);

            // Setup page has Auto scroll
            window.NavigateTo(SettingsPageId.Setup);
            Assert.AreEqual(System.Windows.Controls.ScrollBarVisibility.Auto, scrollViewer.VerticalScrollBarVisibility);

            // Flashcards page has Disabled outer scroll so deck sidebar does not scroll away
            window.NavigateTo(SettingsPageId.Flashcards);
            Assert.AreEqual(System.Windows.Controls.ScrollBarVisibility.Disabled, scrollViewer.VerticalScrollBarVisibility);

            // Navigating to Review restores Auto
            window.NavigateTo(SettingsPageId.Review);
            Assert.AreEqual(System.Windows.Controls.ScrollBarVisibility.Auto, scrollViewer.VerticalScrollBarVisibility);

            window.Close();
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
