using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

/// <summary>Retype, dictation, weak points and the mistake notebook on the Writing page and the floating panel.</summary>
[TestClass]
[DoNotParallelize]
public sealed class WritingPracticeUiTests
{
    private const string Reference = "She has lived here since 2019.";
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.FromHours(7));

    private static readonly WritingPrompt Prompt = new(
        "Cô ấy đã sống ở đây từ năm 2019.", Reference, "Daily life", WritingLevel.Medium,
        new WritingTips([new("sống", "live", "verb")], "Use the present perfect.", "She has"));

    private static readonly WritingFeedback Feedback = new(
        70, WritingVerdict.Almost, Reference, null, "Close.",
        [new WritingIssue(WritingIssueType.Grammar, "lives", "has lived", "Present perfect with since.") { Tag = WritingSkillTag.Tense }]);

    // ---- Writing page --------------------------------------------------------------------------

    [TestMethod]
    public void Page_RetypeFlow_WrongThenRightThenNext()
    {
        RunOnSta(() =>
        {
            var (page, _, coach, audio, _) = CreatePage();
            Click(page, "GenerateButton");
            Answer(page).Text = "She lives here since 2019.";
            Click(page, "CheckButton");

            Answer(page).Text = "She have lived here since 2019";
            Click(page, "CheckButton");
            StringAssert.Contains(Text(page, "RetypeMessageText"), "expected \"has\"");
            Assert.AreEqual(1, coach.CheckCalls, "a retype is compared locally");

            Answer(page).Text = "she has lived here since 2019";
            Click(page, "CheckButton");
            StringAssert.Contains(Text(page, "RetypeMessageText"), "Well done");
            Assert.AreEqual(Reference, audio.LastText, "a correct retype plays the sentence");
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)page.FindName("SkipRetypeButton")).Visibility);
            Assert.AreEqual("Check again", ((Button)page.FindName("CheckButton")).Content);
        });
    }

    [TestMethod]
    public void Page_SkipRetypeHidesThePanel()
    {
        RunOnSta(() =>
        {
            var (page, _, _, _, _) = CreatePage();
            Click(page, "GenerateButton");
            Answer(page).Text = "She lives here since 2019.";
            Click(page, "CheckButton");

            Click(page, "SkipRetypeButton");

            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)page.FindName("RetypePanel")).Visibility);
            Assert.IsFalse(page.Session!.IsRetyping);
        });
    }

    [TestMethod]
    public void Page_RetypeOffAndAutoPlayOn_PlaysCorrectedSentenceAfterCheck()
    {
        RunOnSta(() =>
        {
            var settings = AppSettings.Default with { WritingRetypeEnabled = false, WritingAutoPlayCorrected = true };
            var (page, _, _, audio, _) = CreatePage(settings);
            Click(page, "GenerateButton");
            Answer(page).Text = "She lives here since 2019.";

            Click(page, "CheckButton");

            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)page.FindName("RetypePanel")).Visibility);
            Assert.AreEqual(Reference, audio.LastText);
            Assert.AreEqual("She lives here since 2019.", Answer(page).Text);
        });
    }

    [TestMethod]
    public void Page_OptionsUpdateTheDraft()
    {
        RunOnSta(() =>
        {
            var (page, draft, _, _, _) = CreatePage();

            ((RadioButton)page.FindName("DictationModeButton")).IsChecked = true;
            var retype = (CheckBox)page.FindName("RetypeCheckBox");
            retype.IsChecked = false;
            retype.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var autoPlay = (CheckBox)page.FindName("AutoPlayCheckBox");
            autoPlay.IsChecked = true;
            autoPlay.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.AreEqual(WritingPracticeMode.Dictation, draft.Settings.WritingPracticeMode);
            Assert.IsFalse(draft.Settings.WritingRetypeEnabled);
            Assert.IsTrue(draft.Settings.WritingAutoPlayCorrected);
            Assert.IsFalse(((CheckBox)page.FindName("WeakPointsCheckBox")).IsEnabled, "no mistakes yet");
        });
    }

    [TestMethod]
    public void Page_Dictation_HidesMeaningPlaysSentenceAndScoresLocally()
    {
        RunOnSta(() =>
        {
            var settings = AppSettings.Default with { WritingPracticeMode = WritingPracticeMode.Dictation };
            var (page, _, coach, audio, book) = CreatePage(settings);

            Click(page, "GenerateButton");

            Assert.AreEqual(Reference, audio.LastText, "the sentence plays as soon as it is ready");
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)page.FindName("VietnameseText")).Visibility);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("DictationBar")).Visibility);
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)page.FindName("TipButton")).Visibility);

            Answer(page).Text = "She has livd here 2019";
            Click(page, "CheckButton");

            Assert.AreEqual(0, coach.CheckCalls);
            Assert.AreEqual("67", Text(page, "ScoreText"));
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("VietnameseText")).Visibility);
            Assert.AreEqual(2, book.Current.Mistakes.Count);
        });
    }

    [TestMethod]
    public void Page_MistakesCardShowsStatsAndClearsWithTwoClicks()
    {
        RunOnSta(() =>
        {
            var (page, _, _, _, book) = CreatePage();
            Assert.IsTrue(page.IsMistakesCollapsed);
            Assert.AreEqual("No mistakes yet", Text(page, "MistakesSummaryText"));

            Click(page, "GenerateButton");
            Answer(page).Text = "She lives here since 2019.";
            Click(page, "CheckButton");

            StringAssert.Contains(Text(page, "MistakesSummaryText"), "Most often: Tense");
            Click(page, "MistakesToggleButton");
            Assert.IsFalse(page.IsMistakesCollapsed);
            Assert.AreEqual(1, ((Panel)page.FindName("TagStatsPanel")).Children.Count);
            Assert.AreEqual(1, ((Panel)page.FindName("RecentMistakesPanel")).Children.Count);

            Click(page, "ClearHistoryButton");
            Assert.AreEqual(1, book.Current.Mistakes.Count, "the first click only arms the button");
            Click(page, "ClearHistoryButton");
            Assert.AreEqual(0, book.Current.Mistakes.Count);
            Assert.AreEqual("No mistakes yet", Text(page, "MistakesSummaryText"));
        });
    }

    [TestMethod]
    public void Page_WeakPoints_SendsFocusTagsWhenEnabled()
    {
        RunOnSta(() =>
        {
            var book = BookWithTenseMistakes(5);
            var settings = AppSettings.Default with { WritingFocusWeakPoints = true };
            var (page, _, coach, _, _) = CreatePage(settings, book);

            var weak = (CheckBox)page.FindName("WeakPointsCheckBox");
            Assert.IsTrue(weak.IsEnabled);
            Assert.IsTrue(weak.IsChecked == true);
            StringAssert.Contains(Text(page, "WeakPointsText"), "Tense");

            Click(page, "GenerateButton");

            CollectionAssert.AreEqual(new[] { WritingSkillTag.Tense }, coach.LastRequest!.FocusTags.ToArray());
        });
    }

    // ---- Floating panel ------------------------------------------------------------------------

    [TestMethod]
    public void Panel_RetypeAndDictationUseTheInputBar()
    {
        RunOnSta(() =>
        {
            var coach = new FakeCoach();
            var panel = new FloatingWritingPanel();
            var requests = new List<WritingAudioRequest>();
            panel.AudioRequested += requests.Add;
            panel.SetHistory(new WritingHistoryBook(new InMemoryWritingHistoryStore(), () => Now));
            panel.SetCoach(coach);
            panel.ActivateAsync(AppSettings.Default).GetAwaiter().GetResult();

            Input(panel).Text = "She lives here since 2019.";
            Click(panel, "CheckButton");
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)panel.FindName("RetypePanel")).Visibility);
            Assert.AreEqual("Submit", ((Button)panel.FindName("CheckButton")).Content);
            Assert.AreEqual("", Input(panel).Text);

            Input(panel).Text = Reference;
            Click(panel, "CheckButton");
            Assert.IsFalse(panel.Session!.IsRetyping);
            Assert.AreEqual(new WritingAudioRequest(Reference, Restart: true), requests[^1]);

            ((RadioButton)panel.FindName("DictationModeButton")).IsChecked = true;
            Click(panel, "NextButton");
            Assert.AreEqual(new WritingAudioRequest(Reference, Restart: true), requests[^1]);
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)panel.FindName("VietnameseText")).Visibility);

            Click(panel, "SlowListenButton");
            Assert.IsTrue(requests[^1].Slow);
        });
    }

    [TestMethod]
    public void Panel_PracticeOptionsRaisePreferences()
    {
        RunOnSta(() =>
        {
            var panel = new FloatingWritingPanel();
            panel.SetCoach(new FakeCoach());
            panel.SetHistory(BookWithTenseMistakes(6));
            panel.UpdateSettings(AppSettings.Default);
            AppSettings? raised = null;
            panel.PreferencesChanged += settings => raised = settings;

            var weak = (CheckBox)panel.FindName("WeakPointsCheckBox");
            Assert.IsTrue(weak.IsEnabled);
            weak.IsChecked = true;
            weak.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.IsTrue(raised!.WritingFocusWeakPoints);
            StringAssert.Contains(Text(panel, "TopicsSummaryText"), "Weak points");
        });
    }

    [TestMethod]
    public void FloatingWindow_ForwardsNewPreferences()
    {
        RunOnSta(() =>
        {
            var window = new FloatingWindow(new NoopPlayer());
            AppSettings? forwarded = null;
            window.WritingPreferencesChanged += (_, settings) => forwarded = settings;
            window.WritingCoach = new FakeCoach();
            window.WritingHistory = WritingHistoryBook.InMemory();
            var panel = (FloatingWritingPanel)window.FindName("WritingPanel");

            ((RadioButton)panel.FindName("DictationModeButton")).IsChecked = true;

            Assert.AreEqual(WritingPracticeMode.Dictation, forwarded!.WritingPracticeMode);
            window.Close();
        });
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static WritingHistoryBook BookWithTenseMistakes(int count)
    {
        var book = new WritingHistoryBook(new InMemoryWritingHistoryStore(), () => Now);
        for (var i = 0; i < count; i++)
            book.RecordCheck(WritingPracticeMode.Translate, Prompt, "She lives here.", Feedback);
        return book;
    }

    private static (WritingPage Page, StudyListDraft Draft, FakeCoach Coach, FakeAudio Audio, WritingHistoryBook Book) CreatePage(
        AppSettings? settings = null,
        WritingHistoryBook? book = null)
    {
        var draft = new StudyListDraft(settings ?? AppSettings.Default, _ => { });
        var coach = new FakeCoach();
        var audio = new FakeAudio();
        var history = book ?? new WritingHistoryBook(new InMemoryWritingHistoryStore(), () => Now);
        var page = new WritingPage(audio);
        page.Initialize(draft, coach, null, history);
        return (page, draft, coach, audio, history);
    }

    private static TextBox Answer(FrameworkElement page) => (TextBox)page.FindName("AnswerBox");

    private static TextBox Input(FrameworkElement panel) => (TextBox)panel.FindName("AnswerInput");

    private static string Text(FrameworkElement root, string name) => ((TextBlock)root.FindName(name)).Text;

    private static void Click(FrameworkElement root, string name) =>
        ((ButtonBase)root.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

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
            ExceptionDispatchInfo.Capture(exception).Throw();
    }

    private sealed class FakeCoach : IWritingCoach
    {
        public WritingRequest? LastRequest { get; private set; }
        public int CheckCalls { get; private set; }

        public Task<WritingPrompt> GenerateAsync(WritingRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(Prompt with { FocusTags = request.FocusTags });
        }

        public Task<WritingFeedback> CheckAsync(WritingPrompt prompt, string translation, WritingExplanationLanguage language, CancellationToken cancellationToken = default)
        {
            CheckCalls++;
            return Task.FromResult(Feedback);
        }
    }

    private sealed class FakeAudio : ISentenceAudioPlayback
    {
        public AudioPlaybackState State { get; private set; } = new(false, false, null);
        public event Action<AudioPlaybackState>? StateChanged;
        public string? LastText { get; private set; }
        public double LastSpeed { get; private set; }

        public Task ToggleAsync(string text, string voice, double speed)
        {
            LastText = text;
            LastSpeed = speed;
            State = new AudioPlaybackState(false, true, null);
            StateChanged?.Invoke(State);
            return Task.CompletedTask;
        }

        public void Stop()
        {
            State = new AudioPlaybackState(false, false, null);
            StateChanged?.Invoke(State);
        }

        public void Dispose()
        {
        }
    }

    private sealed class NoopPlayer : IAudioPlayer
    {
        public bool IsPlaying { get; private set; }

        public Task PlayFileAsync(string filePath, Action onEnded, Action<Exception> onError)
        {
            IsPlaying = true;
            return Task.CompletedTask;
        }

        public void Stop() => IsPlaying = false;

        public void Dispose()
        {
        }
    }
}
