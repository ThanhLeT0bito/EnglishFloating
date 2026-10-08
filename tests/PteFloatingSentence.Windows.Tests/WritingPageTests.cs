using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
[DoNotParallelize]
public sealed class WritingPageTests
{
    private static readonly WritingPrompt Prompt = new(
        "Sáng nay đường bị kẹt xe nên tôi đến công ty muộn.",
        "There was a traffic jam this morning, so I got to the office late.",
        "Traffic & transport",
        WritingLevel.Medium,
        new WritingTips([new("kẹt xe", "traffic jam", "noun")], "Use the past simple.", "There was"));

    private static readonly WritingFeedback Feedback = new(
        70,
        WritingVerdict.Almost,
        "There was a traffic jam this morning, so I got to the office late.",
        "I was late for work because of the traffic this morning.",
        "Good try.",
        [
            new(WritingIssueType.Grammar, "was traffic jam", "there was a traffic jam", "Needs a subject."),
            new(WritingIssueType.Vocabulary, "company", "the office", "Use 'office' for a workplace.")
        ]);

    [TestMethod]
    public void Initialize_RendersBuiltInTopicsAndPreferences()
    {
        RunOnSta(() =>
        {
            var settings = WritingTopicRules.ToggleSelection(AppSettings.Default, "traffic") with { WritingLevel = WritingLevel.Hard };
            var (page, draft, _, _) = CreatePage(settings);

            var chips = Chips(page);
            Assert.AreEqual(WritingTopics.BuiltIn.Count, chips.Count);
            Assert.IsTrue(chips.Single(c => (string)c.Tag == "traffic").IsChecked == true);
            Assert.IsTrue(chips.Single(c => (string)c.Tag == "it-work").IsChecked == false);
            Assert.IsTrue(((RadioButton)page.FindName("HardLevelButton")).IsChecked == true);
            Assert.IsTrue(((RadioButton)page.FindName("EnglishExplanationButton")).IsChecked == true);
            Assert.AreEqual(WritingLevel.Hard, draft.Settings.WritingLevel);
        });
    }

    [TestMethod]
    public void TopicChipAndSelectors_UpdateDraft()
    {
        RunOnSta(() =>
        {
            var (page, draft, _, _) = CreatePage();
            var chip = Chips(page).Single(c => (string)c.Tag == "it-work");

            chip.IsChecked = true;
            chip.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            ((RadioButton)page.FindName("EasyLevelButton")).IsChecked = true;
            ((RadioButton)page.FindName("VietnameseExplanationButton")).IsChecked = true;

            CollectionAssert.AreEqual(new[] { "it-work" }, draft.Settings.SelectedWritingTopicKeys.ToArray());
            Assert.AreEqual(WritingLevel.Easy, draft.Settings.WritingLevel);
            Assert.AreEqual(WritingExplanationLanguage.Vietnamese, draft.Settings.WritingExplanationLanguage);
        });
    }

    [TestMethod]
    public void AddTopic_SelectsItAndRemoveDeletesIt()
    {
        RunOnSta(() =>
        {
            var (page, draft, _, _) = CreatePage();
            ((TextBox)page.FindName("NewTopicInput")).Text = "Job interviews";

            Click(page, "AddTopicButton");

            var topic = draft.Settings.CustomWritingTopics.Single();
            Assert.AreEqual("Job interviews", topic.Name);
            CollectionAssert.AreEqual(new[] { topic.Key }, draft.Settings.SelectedWritingTopicKeys.ToArray());
            Assert.AreEqual(string.Empty, ((TextBox)page.FindName("NewTopicInput")).Text);

            var removeButton = ((Panel)page.FindName("TopicPanel")).Children.OfType<StackPanel>().Single().Children.OfType<Button>().Single();
            removeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.AreEqual(0, draft.Settings.CustomWritingTopics.Count);
            Assert.AreEqual(0, draft.Settings.SelectedWritingTopicKeys.Count);
        });
    }

    [TestMethod]
    public void AddTopic_DuplicateShowsError()
    {
        RunOnSta(() =>
        {
            var (page, draft, _, _) = CreatePage();
            ((TextBox)page.FindName("NewTopicInput")).Text = "travel";

            Click(page, "AddTopicButton");

            Assert.AreEqual("This topic already exists.", ((TextBlock)page.FindName("TopicErrorText")).Text);
            Assert.AreEqual(0, draft.Settings.CustomWritingTopics.Count);
        });
    }

    [TestMethod]
    public void Generate_ShowsPromptAndSendsSelectedTopics()
    {
        RunOnSta(() =>
        {
            var settings = WritingTopicRules.ToggleSelection(AppSettings.Default, "traffic");
            var (page, _, coach, _) = CreatePage(settings);

            Click(page, "GenerateButton");

            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("PromptCard")).Visibility);
            Assert.AreEqual(Prompt.Vietnamese, ((TextBlock)page.FindName("VietnameseText")).Text);
            Assert.AreEqual("Traffic & transport · Medium", ((TextBlock)page.FindName("PromptMetaText")).Text);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("AnswerSection")).Visibility);
            CollectionAssert.AreEqual(new[] { "Traffic & transport" }, coach.LastRequest!.TopicNames.ToArray());
            Assert.AreEqual("Tip 0/3", ((TextBlock)page.FindName("TipButtonText")).Text);
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)page.FindName("TipsPanel")).Visibility);
        });
    }

    [TestMethod]
    public void TipButton_RevealsTiersInOrder()
    {
        RunOnSta(() =>
        {
            var (page, _, _, _) = CreatePage();
            Click(page, "GenerateButton");

            Click(page, "TipButton");
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("VocabularyTipSection")).Visibility);
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)page.FindName("GrammarTipSection")).Visibility);
            Assert.AreEqual("Tip 1/3", ((TextBlock)page.FindName("TipButtonText")).Text);

            Click(page, "TipButton");
            Click(page, "TipButton");
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("StarterTipSection")).Visibility);
            Assert.IsFalse(((Button)page.FindName("TipButton")).IsEnabled);
        });
    }

    [TestMethod]
    public void Check_ShowsScoreHighlightsIssuesAndCorrection()
    {
        RunOnSta(() =>
        {
            var (page, _, coach, _) = CreatePage();
            Click(page, "GenerateButton");
            Click(page, "TipButton");
            ((TextBox)page.FindName("AnswerBox")).Text = "This morning road was traffic jam so I came to company late.";

            Click(page, "CheckButton");

            Assert.AreEqual("This morning road was traffic jam so I came to company late.", coach.LastAnswer);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("ResultCard")).Visibility);
            Assert.AreEqual("70", ((TextBlock)page.FindName("ScoreText")).Text);
            Assert.AreEqual("Almost there", ((TextBlock)page.FindName("VerdictText")).Text);
            Assert.AreEqual("Used 1 tip", ((TextBlock)page.FindName("TipsUsedText")).Text);
            Assert.AreEqual(2, ((Panel)page.FindName("IssuesPanel")).Children.Count);
            Assert.AreEqual(Feedback.CorrectedSentence, ((TextBlock)page.FindName("CorrectedText")).Text);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("NaturalSection")).Visibility);
            var inlines = ((TextBlock)page.FindName("AnswerHighlightText")).Inlines.Count;
            Assert.IsTrue(inlines >= 4, $"expected highlighted runs, got {inlines}");
            Assert.AreEqual("Check again", ((Button)page.FindName("CheckButton")).Content);
        });
    }

    [TestMethod]
    public void SaveToList_AddsCorrectedSentenceToDraft()
    {
        RunOnSta(() =>
        {
            var changed = 0;
            var (page, draft, _, _) = CreatePage(onDraftChanged: () => changed++);
            Click(page, "GenerateButton");
            ((TextBox)page.FindName("AnswerBox")).Text = "There was traffic jam.";
            Click(page, "CheckButton");

            Click(page, "SaveToListButton");

            Assert.IsTrue(draft.Settings.StudyLists[0].Sentences.Any(s => s.Text == Feedback.CorrectedSentence));
            Assert.AreEqual(1, changed);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("SaveStatusText")).Visibility);

            Click(page, "SaveToListButton");
            StringAssert.Contains(((TextBlock)page.FindName("SaveStatusText")).Text, "already");
        });
    }

    [TestMethod]
    public void AudioButton_PlaysCorrectedSentence()
    {
        RunOnSta(() =>
        {
            var (page, _, _, audio) = CreatePage();
            Click(page, "GenerateButton");
            ((TextBox)page.FindName("AnswerBox")).Text = "There was traffic jam.";
            Click(page, "CheckButton");

            Click(page, "AudioButton");

            Assert.AreEqual(Feedback.CorrectedSentence, audio.LastText);
        });
    }

    [TestMethod]
    public void CheckError_ShowsRetryAndKeepsAnswer()
    {
        RunOnSta(() =>
        {
            var (page, _, coach, _) = CreatePage();
            Click(page, "GenerateButton");
            coach.CheckFailure = new InvalidOperationException("Gemini took too long to respond. Please try again.");
            ((TextBox)page.FindName("AnswerBox")).Text = "There was traffic jam.";

            Click(page, "CheckButton");

            Assert.AreEqual("Gemini took too long to respond. Please try again.", ((TextBlock)page.FindName("StatusText")).Text);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("RetryButton")).Visibility);
            Assert.AreEqual("There was traffic jam.", ((TextBox)page.FindName("AnswerBox")).Text);

            coach.CheckFailure = null;
            Click(page, "RetryButton");
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("ResultCard")).Visibility);
        });
    }

    [TestMethod]
    public void WithoutCoach_ShowsGeminiKeyMessage()
    {
        RunOnSta(() =>
        {
            var page = new WritingPage(new FakeAudio());
            page.Initialize(new StudyListDraft(AppSettings.Default, _ => { }), coach: null);

            Click(page, "GenerateButton");

            Assert.AreEqual(WritingPresentation.NoCoachMessage, ((TextBlock)page.FindName("StatusText")).Text);
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)page.FindName("PromptCard")).Visibility);
        });
    }

    [TestMethod]
    public void SettingsWindow_NavigatesToWritingPage()
    {
        RunOnSta(() =>
        {
            var window = new SettingsWindow(AppSettings.Default, _ => { }, writingCoach: new FakeCoach());

            window.NavigateTo(SettingsPageId.Writing);

            Assert.AreEqual(SettingsPageId.Writing, window.SelectedPage);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)window.FindName("WritingSection")).Visibility);
            Assert.AreEqual("Writing", ((TextBlock)window.FindName("PageTitle")).Text);
            window.Close();
        });
    }

    private static (WritingPage Page, StudyListDraft Draft, FakeCoach Coach, FakeAudio Audio) CreatePage(
        AppSettings? settings = null,
        Action? onDraftChanged = null)
    {
        var draft = new StudyListDraft(settings ?? AppSettings.Default, _ => { });
        var coach = new FakeCoach();
        var audio = new FakeAudio();
        var page = new WritingPage(audio);
        page.Initialize(draft, coach, onDraftChanged);
        return (page, draft, coach, audio);
    }

    private static List<ToggleButton> Chips(WritingPage page)
    {
        var panel = (Panel)page.FindName("TopicPanel");
        return panel.Children.OfType<ToggleButton>()
            .Concat(panel.Children.OfType<StackPanel>().SelectMany(group => group.Children.OfType<ToggleButton>()))
            .ToList();
    }

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
        public string? LastAnswer { get; private set; }
        public Exception? CheckFailure { get; set; }
        public Exception? GenerateFailure { get; set; }

        public Task<WritingPrompt> GenerateAsync(WritingRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return GenerateFailure is null ? Task.FromResult(Prompt) : Task.FromException<WritingPrompt>(GenerateFailure);
        }

        public Task<WritingFeedback> CheckAsync(WritingPrompt prompt, string translation, WritingExplanationLanguage language, CancellationToken cancellationToken = default)
        {
            LastAnswer = translation;
            return CheckFailure is null ? Task.FromResult(Feedback) : Task.FromException<WritingFeedback>(CheckFailure);
        }
    }

    private sealed class FakeAudio : ISentenceAudioPlayback
    {
        public AudioPlaybackState State { get; private set; } = new(false, false, null);
        public event Action<AudioPlaybackState>? StateChanged;
        public string? LastText { get; private set; }

        public Task ToggleAsync(string text, string voice, double speed)
        {
            LastText = text;
            State = new AudioPlaybackState(false, true, null);
            StateChanged?.Invoke(State);
            return Task.CompletedTask;
        }

        public void Stop()
        {
            State = new AudioPlaybackState(false, false, null);
        }

        public void Dispose()
        {
        }
    }
    [TestMethod]
    public void SetupArrow_FoldsAfterGenerateAndTogglesOnClick()
    {
        RunOnSta(() =>
        {
            var settings = WritingTopicRules.ToggleSelection(AppSettings.Default, "traffic");
            var (page, _, _, _) = CreatePage(settings);
            var chevron = (TextBlock)page.FindName("SetupChevron");
            var summary = (TextBlock)page.FindName("SetupSummaryText");
            Assert.IsFalse(page.IsSetupCollapsed, "options start open");
            Assert.AreEqual("\uE70D", chevron.Text);
            Assert.AreEqual(Visibility.Collapsed, summary.Visibility);

            Click(page, "GenerateButton");

            Assert.IsTrue(page.IsSetupCollapsed, "options fold once a sentence is shown");
            Assert.AreEqual("\uE76C", chevron.Text);
            Assert.AreEqual(Visibility.Visible, summary.Visibility);
            Assert.AreEqual("Traffic & transport · Medium · Explain in English", summary.Text);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("GenerateButton")).Visibility);

            Click(page, "SetupToggleButton");
            Assert.IsFalse(page.IsSetupCollapsed);
            ((RadioButton)page.FindName("HardLevelButton")).IsChecked = true;

            Click(page, "SetupToggleButton");
            Assert.IsTrue(page.IsSetupCollapsed);
            StringAssert.Contains(summary.Text, "Hard");

            Click(page, "SkipButton");
            Assert.IsTrue(page.IsSetupCollapsed, "a new sentence keeps the options folded");
        });
    }

    [TestMethod]
    public void SetupArrow_CanFoldBeforeAnySentenceAndGenerateStillWorks()
    {
        RunOnSta(() =>
        {
            var (page, _, coach, _) = CreatePage();

            Click(page, "SetupToggleButton");
            Assert.IsTrue(page.IsSetupCollapsed);
            Click(page, "GenerateButton");

            Assert.IsNotNull(coach.LastRequest);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("PromptCard")).Visibility);
        });
    }
}
