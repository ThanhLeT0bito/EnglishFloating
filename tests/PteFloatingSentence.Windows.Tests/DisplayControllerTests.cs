using System.Windows;
using System.Windows.Controls;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
[DoNotParallelize]
public class DisplayControllerTests
{
    [TestMethod]
    public void Apply_SentenceTrueCardsTrue_SentenceAndCardsVisible_LauncherHidden()
    {
        RunOnSta(() =>
        {
            var floating = CreateFloatingWindowWithVocabulary();
            var launcher = new OverlayLauncherWindow();
            using var controller = new DisplayController(floating, launcher);

            var settings = CreateSettings(showSentence: true, showCards: true);
            controller.Apply(settings);

            Assert.AreEqual(Visibility.Visible, floating.Visibility);
            var vocabPanel = (ItemsControl)floating.FindName("VocabularyPanel");
            Assert.AreEqual(Visibility.Visible, vocabPanel.Visibility);
            Assert.AreEqual(Visibility.Hidden, launcher.Visibility);
        });
    }

    [TestMethod]
    public void Apply_SentenceTrueCardsFalse_SentenceVisibleCardsHidden_LauncherHidden()
    {
        RunOnSta(() =>
        {
            var floating = CreateFloatingWindowWithVocabulary();
            var launcher = new OverlayLauncherWindow();
            using var controller = new DisplayController(floating, launcher);

            var settings = CreateSettings(showSentence: true, showCards: false);
            controller.Apply(settings);

            Assert.AreEqual(Visibility.Visible, floating.Visibility);
            var vocabPanel = (ItemsControl)floating.FindName("VocabularyPanel");
            Assert.AreEqual(Visibility.Collapsed, vocabPanel.Visibility);
            Assert.AreEqual(Visibility.Hidden, launcher.Visibility);
        });
    }

    [TestMethod]
    public void Apply_ReenablingCardsWithUnchangedContent_RendersCardsAgain()
    {
        RunOnSta(() =>
        {
            var floating = CreateFloatingWindowWithVocabulary();
            var launcher = new OverlayLauncherWindow();
            using var controller = new DisplayController(floating, launcher);
            var settings = CreateSettings(showSentence: true, showCards: true);

            controller.Apply(settings with { ShowVocabularyCards = false });
            controller.Apply(settings);

            var vocabPanel = (ItemsControl)floating.FindName("VocabularyPanel");
            Assert.AreEqual(Visibility.Visible, vocabPanel.Visibility);
            Assert.IsNotNull(vocabPanel.ItemsSource);
        });
    }

    [TestMethod]
    public void Apply_SentenceFalseCardsTrue_SentenceHidden_LauncherVisible()
    {
        RunOnSta(() =>
        {
            var floating = CreateFloatingWindowWithVocabulary();
            var launcher = new OverlayLauncherWindow();
            using var controller = new DisplayController(floating, launcher);

            var settings = CreateSettings(showSentence: false, showCards: true);
            controller.Apply(settings);

            Assert.AreEqual(Visibility.Hidden, floating.Visibility);
            Assert.AreEqual(Visibility.Visible, launcher.Visibility);
        });
    }

    [TestMethod]
    public void Apply_SentenceFalseCardsFalse_SentenceHidden_LauncherVisible()
    {
        RunOnSta(() =>
        {
            var floating = CreateFloatingWindowWithVocabulary();
            var launcher = new OverlayLauncherWindow();
            using var controller = new DisplayController(floating, launcher);

            var settings = CreateSettings(showSentence: false, showCards: false);
            controller.Apply(settings);

            Assert.AreEqual(Visibility.Hidden, floating.Visibility);
            Assert.AreEqual(Visibility.Visible, launcher.Visibility);
        });
    }

    [TestMethod]
    public void Controller_PropagatesSettingsAndRestoreEventsFromLauncher()
    {
        RunOnSta(() =>
        {
            var floating = new FloatingWindow();
            var launcher = new OverlayLauncherWindow();
            using var controller = new DisplayController(floating, launcher);

            var settingsRequestedFired = false;
            var restoreRequestedFired = false;

            controller.ShowSettingsRequested += (_, _) => settingsRequestedFired = true;
            controller.RestoreOverlayRequested += (_, _) => restoreRequestedFired = true;

            launcher.RaiseSettingsRequested();
            launcher.RaiseRestoreRequested();

            Assert.IsTrue(settingsRequestedFired);
            Assert.IsTrue(restoreRequestedFired);
        });
    }

    [TestMethod]
    public void Apply_LauncherRecovery_HidesLauncherAndRestoresSentence()
    {
        RunOnSta(() =>
        {
            var floating = CreateFloatingWindowWithVocabulary();
            var launcher = new OverlayLauncherWindow();
            using var controller = new DisplayController(floating, launcher);

            // 1. Hide both layers
            var hiddenSettings = CreateSettings(showSentence: false, showCards: false);
            controller.Apply(hiddenSettings);

            Assert.AreEqual(Visibility.Hidden, floating.Visibility);
            Assert.AreEqual(Visibility.Visible, launcher.Visibility);

            // 2. Request settings from launcher
            var openedSettings = false;
            controller.ShowSettingsRequested += (_, _) => openedSettings = true;
            launcher.RaiseSettingsRequested();
            Assert.IsTrue(openedSettings);

            // 3. User enables sentence display and saves
            var restoredSettings = CreateSettings(showSentence: true, showCards: true);
            controller.Apply(restoredSettings);

            // 4. Launcher hides, sentence returns
            Assert.AreEqual(Visibility.Hidden, launcher.Visibility);
            Assert.AreEqual(Visibility.Visible, floating.Visibility);
            var vocabPanel = (ItemsControl)floating.FindName("VocabularyPanel");
            Assert.AreEqual(Visibility.Visible, vocabPanel.Visibility);
        });
    }

    [TestMethod]
    public void Apply_RestoringHiddenOverlayWithSameContent_RendersVocabularyCards()
    {
        RunOnSta(() =>
        {
            var floating = new FloatingWindow();
            var launcher = new OverlayLauncherWindow();
            using var controller = new DisplayController(floating, launcher);
            var visibleSettings = CreateSettings(showSentence: true, showCards: true);

            controller.Apply(visibleSettings);
            controller.Apply(visibleSettings with { ShowSentenceOverlay = false });
            controller.Apply(visibleSettings);

            var vocabPanel = (ItemsControl)floating.FindName("VocabularyPanel");
            Assert.AreEqual(Visibility.Visible, vocabPanel.Visibility);
            Assert.IsNotNull(vocabPanel.ItemsSource);
        });
    }

    private static FloatingWindow CreateFloatingWindowWithVocabulary()
    {
        var window = new FloatingWindow();
        var vocabItem = new VocabularyItem(
            Id: Guid.NewGuid(),
            Phrase: "practice",
            NormalizedPhrase: "practice",
            Meaning: "To do repeatedly.",
            Status: VocabularyStatus.Ready,
            IsHidden: false);

        var sentence = new StudySentence(Guid.NewGuid(), "Practice daily.", Vocabulary: [vocabItem]);
        var list = new StudyList(Guid.NewGuid(), "List", 10, 0, [sentence]);
        var settings = AppSettings.Default with
        {
            StudyLists = [list],
            ActiveListId = list.Id,
            ShowSentenceOverlay = true,
            ShowVocabularyCards = true
        };
        window.ApplySettings(settings);
        return window;
    }

    private static AppSettings CreateSettings(bool showSentence, bool showCards)
    {
        var vocabItem = new VocabularyItem(
            Id: Guid.NewGuid(),
            Phrase: "practice",
            NormalizedPhrase: "practice",
            Meaning: "To do repeatedly.",
            Status: VocabularyStatus.Ready,
            IsHidden: false);

        var sentence = new StudySentence(Guid.NewGuid(), "Practice daily.", Vocabulary: [vocabItem]);
        var list = new StudyList(Guid.NewGuid(), "List", 10, 0, [sentence]);
        return AppSettings.Default with
        {
            StudyLists = [list],
            ActiveListId = list.Id,
            ShowSentenceOverlay = showSentence,
            ShowVocabularyCards = showCards
        };
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
