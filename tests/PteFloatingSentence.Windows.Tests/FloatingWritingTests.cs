using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;
using ValidationResult = PteFloatingSentence.Core.ValidationResult;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
[DoNotParallelize]
public sealed class FloatingWritingTests
{
    private static readonly WritingPrompt Prompt = new(
        "Sáng nay đường bị kẹt xe nên tôi đến công ty muộn.",
        "There was a traffic jam this morning, so I got to the office late.",
        "Traffic & transport",
        WritingLevel.Medium,
        new WritingTips([new("kẹt xe", "traffic jam", "noun")], "Use the past simple.", "There was"));

    private static readonly WritingFeedback Feedback = new(
        72,
        WritingVerdict.Almost,
        "There was a traffic jam this morning, so I got to the office late.",
        null,
        "Good try.",
        [new(WritingIssueType.Grammar, "was traffic jam", "there was a traffic jam", "Needs a subject.")]);

    [TestMethod]
    public void Panel_ActivateGeneratesFirstSentenceAndFoldsTopics()
    {
        RunOnSta(() =>
        {
            var (panel, coach) = CreatePanel(WritingTopicRules.ToggleSelection(AppSettings.Default, "traffic"));

            panel.ActivateAsync(WritingTopicRules.ToggleSelection(AppSettings.Default, "traffic")).GetAwaiter().GetResult();

            Assert.AreEqual(Prompt.Vietnamese, Text(panel, "VietnameseText"));
            CollectionAssert.AreEqual(new[] { "Traffic & transport" }, coach.Requests[0].TopicNames.ToArray());
            Assert.IsTrue(panel.IsTopicsCollapsed);
            StringAssert.Contains(Text(panel, "TopicsSummaryText"), "Traffic & transport");
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)panel.FindName("ResultToggleButton")).Visibility);
        });
    }

    [TestMethod]
    public void Panel_TopicsArrowTogglesAndChangesRaisePreferences()
    {
        RunOnSta(() =>
        {
            var (panel, _) = CreatePanel();
            AppSettings? changed = null;
            panel.PreferencesChanged += settings => changed = settings;

            Click(panel, "TopicsToggleButton");
            Assert.IsFalse(panel.IsTopicsCollapsed);
            var chip = ((Panel)panel.FindName("TopicPanel")).Children.OfType<ToggleButton>().Single(c => (string)c.Tag == "it-work");
            chip.IsChecked = true;
            chip.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            ((RadioButton)panel.FindName("HardLevelButton")).IsChecked = true;

            Assert.IsNotNull(changed);
            CollectionAssert.AreEqual(new[] { "it-work" }, changed!.SelectedWritingTopicKeys.ToArray());
            Assert.AreEqual(WritingLevel.Hard, changed.WritingLevel);
            StringAssert.Contains(Text(panel, "TopicsSummaryText"), "Hard");

            Click(panel, "TopicsToggleButton");
            Assert.IsTrue(panel.IsTopicsCollapsed);
        });
    }

    [TestMethod]
    public void Panel_CheckShowsResultSectionThatCanBeHidden()
    {
        RunOnSta(() =>
        {
            var (panel, coach) = CreatePanel();
            panel.ActivateAsync(AppSettings.Default).GetAwaiter().GetResult();
            ((TextBox)panel.FindName("AnswerInput")).Text = "This morning road was traffic jam.";

            Click(panel, "CheckButton");

            Assert.AreEqual("This morning road was traffic jam.", coach.LastAnswer);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)panel.FindName("ResultToggleButton")).Visibility);
            Assert.IsFalse(panel.IsResultCollapsed);
            StringAssert.Contains(Text(panel, "ResultSummaryText"), "72 · Almost there");
            Assert.AreEqual(Feedback.CorrectedSentence, Text(panel, "CorrectedText"));
            Assert.AreEqual(1, ((Panel)panel.FindName("IssuesPanel")).Children.Count);

            Click(panel, "ResultToggleButton");
            Assert.IsTrue(panel.IsResultCollapsed);
            Click(panel, "CheckButton");
            Assert.IsTrue(panel.IsResultCollapsed, "a hidden result stays hidden on the next check");
        });
    }

    [TestMethod]
    public void Panel_TipSaveAndAudioUseHostCallbacks()
    {
        RunOnSta(() =>
        {
            var (panel, _) = CreatePanel();
            string? saved = null;
            string? played = null;
            panel.SaveSentence = text => { saved = text; return new ValidationResult(true, null); };
            panel.AudioRequested += request => played = request.Text;
            panel.ActivateAsync(AppSettings.Default).GetAwaiter().GetResult();

            Click(panel, "TipButton");
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)panel.FindName("TipsPanel")).Visibility);
            Assert.AreEqual("1/3", Text(panel, "TipButtonText"));

            ((TextBox)panel.FindName("AnswerInput")).Text = "There was traffic jam.";
            Click(panel, "CheckButton");
            Click(panel, "SaveToListButton");
            Click(panel, "AudioButton");

            Assert.AreEqual(Feedback.CorrectedSentence, saved);
            Assert.AreEqual(Feedback.CorrectedSentence, played);
            Assert.AreEqual("Saved to your active study list.", Text(panel, "SaveStatusText"));
        });
    }

    [TestMethod]
    public void FloatingWindow_StartAndExitWriting()
    {
        RunOnSta(() =>
        {
            var window = new FloatingWindow(new NoopPlayer(), null, null) { WritingCoach = new FakeCoach() };
            window.ApplySettings(AppSettings.Default);
            var panel = (FrameworkElement)window.FindName("WritingPanel");
            var normal = (FrameworkElement)window.FindName("NormalSentenceContainer");

            window.StartWriting();
            Assert.IsTrue(window.IsWritingMode);
            Assert.AreEqual(Visibility.Visible, panel.Visibility);
            Assert.AreEqual(Visibility.Collapsed, normal.Visibility);
            Assert.AreEqual("Exit Writing", ((MenuItem)window.FindName("WritingMenuItem")).Header);

            window.ApplySettings(AppSettings.Default with { FontSize = 40 });
            Assert.AreEqual(Visibility.Visible, panel.Visibility, "settings updates keep writing on screen");

            window.StartPractice();
            Assert.IsFalse(window.IsWritingMode, "practice replaces writing");
            Assert.AreEqual(Visibility.Collapsed, panel.Visibility);

            window.ExitPractice();
            window.StartWriting();
            window.ExitWriting();
            Assert.AreEqual(Visibility.Visible, normal.Visibility);
            Assert.AreEqual("Start Writing", ((MenuItem)window.FindName("WritingMenuItem")).Header);
            window.Close();
        });
    }

    [TestMethod]
    public void FloatingWindow_ForwardsWritingPreferences()
    {
        RunOnSta(() =>
        {
            var window = new FloatingWindow(new NoopPlayer(), null, null) { WritingCoach = new FakeCoach() };
            window.ApplySettings(AppSettings.Default);
            AppSettings? forwarded = null;
            window.WritingPreferencesChanged += (_, settings) => forwarded = settings;
            window.StartWriting();
            var panel = (FloatingWritingPanel)window.FindName("WritingPanel");

            ((RadioButton)panel.FindName("EasyLevelButton")).IsChecked = true;

            Assert.AreEqual(WritingLevel.Easy, forwarded!.WritingLevel);
            window.Close();
        });
    }

    private static (FloatingWritingPanel Panel, FakeCoach Coach) CreatePanel(AppSettings? settings = null)
    {
        var coach = new FakeCoach();
        var panel = new FloatingWritingPanel();
        panel.SetCoach(coach);
        panel.UpdateSettings(settings ?? AppSettings.Default);
        return (panel, coach);
    }

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
        public List<WritingRequest> Requests { get; } = [];
        public string? LastAnswer { get; private set; }

        public Task<WritingPrompt> GenerateAsync(WritingRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Prompt);
        }

        public Task<WritingFeedback> CheckAsync(WritingPrompt prompt, string translation, WritingExplanationLanguage language, CancellationToken cancellationToken = default)
        {
            LastAnswer = translation;
            return Task.FromResult(Feedback);
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
