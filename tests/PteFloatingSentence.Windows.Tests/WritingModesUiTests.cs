using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

/// <summary>Paraphrase, combine and English → Vietnamese on the Writing page and the floating panel.</summary>
[TestClass]
[DoNotParallelize]
public sealed class WritingModesUiTests
{
    private const string ParaphraseSource = "The meeting was postponed because it rained.";
    private const string ReverseEnglish = "The meeting was cancelled at the last minute.";

    [TestMethod]
    public void Page_SwitchingToParaphraseGetsAParaphraseExercise()
    {
        RunOnSta(() =>
        {
            var (page, draft, coach, _) = CreatePage();
            Click(page, "GenerateButton");
            Assert.AreEqual(WritingPracticeMode.Translate, coach.Requests[^1].Mode);

            ((RadioButton)page.FindName("ParaphraseModeButton")).IsChecked = true;

            Assert.AreEqual(WritingPracticeMode.Paraphrase, draft.Settings.WritingPracticeMode);
            Assert.AreEqual(WritingPracticeMode.Paraphrase, coach.Requests[^1].Mode, "a new paraphrase sentence is requested at once");
            Assert.AreEqual(ParaphraseSource, Text(page, "VietnameseText"));
            Assert.AreEqual("Rewrite in your own words", Text(page, "PromptHeaderText"));
            Assert.AreEqual("Your paraphrase", Text(page, "AnswerLabelText"));
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("DictationBar")).Visibility);
        });
    }

    [TestMethod]
    public void Page_ParaphraseCopyIsRejectedAndMeaningShowsAfterCheck()
    {
        RunOnSta(() =>
        {
            var (page, _, coach, _) = CreatePage(AppSettings.Default with { WritingPracticeMode = WritingPracticeMode.Paraphrase });
            Click(page, "GenerateButton");

            Answer(page).Text = ParaphraseSource;
            Click(page, "CheckButton");
            Assert.AreEqual(WritingSession.CopiedParaphraseMessage, Text(page, "StatusText"));
            Assert.AreEqual(0, coach.CheckCalls);

            Answer(page).Text = "Because of the rain, the meeting was put off.";
            Click(page, "CheckButton");
            Assert.AreEqual(1, coach.CheckCalls);
            StringAssert.Contains(Text(page, "MeaningText"), "Cuộc họp");
        });
    }

    [TestMethod]
    public void Page_CombineShowsBothSentencesAndConnector()
    {
        RunOnSta(() =>
        {
            var (page, _, _, _) = CreatePage(AppSettings.Default with { WritingPracticeMode = WritingPracticeMode.Combine });

            Click(page, "GenerateButton");

            Assert.AreEqual("It was raining.\nWe went for a walk.", Text(page, "VietnameseText"));
            Assert.AreEqual("Use: although", Text(page, "InstructionText"));
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("InstructionText")).Visibility);
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)page.FindName("MeaningText")).Visibility);
        });
    }

    [TestMethod]
    public void Page_ReverseSavesAndPlaysTheEnglishSentence()
    {
        RunOnSta(() =>
        {
            var (page, draft, _, audio) = CreatePage(AppSettings.Default with { WritingPracticeMode = WritingPracticeMode.Reverse, WritingRetypeEnabled = false });
            Click(page, "GenerateButton");
            Assert.AreEqual(ReverseEnglish, Text(page, "VietnameseText"));
            Assert.AreEqual("Translate into Vietnamese", Text(page, "PromptHeaderText"));

            Answer(page).Text = "Cuộc họp bị hủy vào phút cuối.";
            Click(page, "CheckButton");
            Click(page, "AudioButton");
            Click(page, "SaveToListButton");

            Assert.AreEqual(ReverseEnglish, audio.LastText);
            Assert.IsTrue(draft.Settings.StudyLists[0].Sentences.Any(sentence => sentence.Text == ReverseEnglish));
        });
    }

    [TestMethod]
    public void Panel_ModeButtonsRaisePreferencesAndRegenerate()
    {
        RunOnSta(() =>
        {
            var coach = new ModeCoach();
            var panel = new FloatingWritingPanel();
            panel.SetCoach(coach);
            panel.ActivateAsync(AppSettings.Default).GetAwaiter().GetResult();
            AppSettings? raised = null;
            panel.PreferencesChanged += settings => raised = settings;

            ((RadioButton)panel.FindName("CombineModeButton")).IsChecked = true;

            Assert.AreEqual(WritingPracticeMode.Combine, raised!.WritingPracticeMode);
            Assert.AreEqual(WritingPracticeMode.Combine, coach.Requests[^1].Mode);
            Assert.AreEqual("Use: although", Text(panel, "InstructionText"));
            StringAssert.Contains(((TextBlock)panel.FindName("AnswerPlaceholder")).Text, "joins both");
            StringAssert.Contains(Text(panel, "TopicsSummaryText"), "Combine");
        });
    }

    [TestMethod]
    public void Panel_ListenPlaysTheParaphraseSource()
    {
        RunOnSta(() =>
        {
            var panel = new FloatingWritingPanel();
            var requests = new List<WritingAudioRequest>();
            panel.AudioRequested += requests.Add;
            panel.SetCoach(new ModeCoach());
            panel.ActivateAsync(AppSettings.Default with { WritingPracticeMode = WritingPracticeMode.Paraphrase }).GetAwaiter().GetResult();

            Click(panel, "ListenButton");

            Assert.AreEqual(ParaphraseSource, requests[^1].Text);
        });
    }

    private static (WritingPage Page, StudyListDraft Draft, ModeCoach Coach, FakeAudio Audio) CreatePage(AppSettings? settings = null)
    {
        var draft = new StudyListDraft(settings ?? AppSettings.Default, _ => { });
        var coach = new ModeCoach();
        var audio = new FakeAudio();
        var page = new WritingPage(audio);
        page.Initialize(draft, coach, null, WritingHistoryBook.InMemory());
        return (page, draft, coach, audio);
    }

    private static TextBox Answer(FrameworkElement page) => (TextBox)page.FindName("AnswerBox");

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

    /// <summary>Returns an exercise of the requested kind and a fixed check result.</summary>
    private sealed class ModeCoach : IWritingCoach
    {
        public List<WritingRequest> Requests { get; } = [];
        public int CheckCalls { get; private set; }

        public Task<WritingPrompt> GenerateAsync(WritingRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var tips = new WritingTips([new("kẹt xe", "traffic jam", "noun")], "Hint.", "The");
            var prompt = request.Mode switch
            {
                WritingPracticeMode.Paraphrase => new WritingPrompt("Cuộc họp bị hoãn vì trời mưa.", "Because of the rain, the meeting was put off.", "Work", request.Level, tips)
                { SourceEnglish = ParaphraseSource },
                WritingPracticeMode.Combine => new WritingPrompt("Mặc dù trời mưa, chúng tôi vẫn đi dạo.", "Although it was raining, we went for a walk.", "Weather", request.Level, tips)
                { SourceEnglish = "It was raining.\nWe went for a walk.", Instruction = "although" },
                WritingPracticeMode.Reverse => new WritingPrompt("Cuộc họp đã bị hủy vào phút chót.", ReverseEnglish, "Work", request.Level, tips),
                _ => new WritingPrompt("Tôi đi làm bằng xe buýt.", "I go to work by bus.", "Daily life", request.Level, tips)
            };
            return Task.FromResult(prompt with { Mode = request.Mode });
        }

        public Task<WritingFeedback> CheckAsync(WritingPrompt prompt, string translation, WritingExplanationLanguage language, CancellationToken cancellationToken = default)
        {
            CheckCalls++;
            var corrected = prompt.Mode == WritingPracticeMode.Reverse ? "Cuộc họp đã bị hủy vào phút chót." : prompt.ReferenceEnglish;
            return Task.FromResult(new WritingFeedback(80, WritingVerdict.Almost, corrected, null, "Good.",
                [new WritingIssue(WritingIssueType.Vocabulary, "x", "y", "z") { Tag = WritingSkillTag.WordChoice }]));
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
            StateChanged?.Invoke(State);
        }

        public void Dispose()
        {
        }
    }
}
