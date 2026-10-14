using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

/// <summary>The Grammar page, grammar drills on the Writing page and the Learn hints.</summary>
[TestClass]
[DoNotParallelize]
public sealed class GrammarUiTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.FromHours(7));
    private static readonly GrammarTopic PresentSimple = GrammarTopics.Find("present-simple")!;

    private const string LessonJson = """
        {
          "essence": "The present simple is for things that are generally true or happen regularly.",
          "form": [ { "label": "Positive", "pattern": "S + V(s/es)", "example": "She works here." } ],
          "uses": [ { "title": "Habits", "explanation": "Things you do regularly.", "example": "I walk to work." } ],
          "signalWords": [ "always", "usually" ],
          "contrast": "Use the present continuous for actions happening now.",
          "commonMistakes": [ { "wrong": "She go to work.", "right": "She goes to work.", "why": "Add -es for he/she/it." } ],
          "examples": [ { "english": "The train leaves at 8.", "vietnamese": "Tàu chạy lúc 8 giờ." } ],
          "quiz": [ { "question": "He ___ coffee every morning.", "options": ["drink", "drinks", "drinking"], "answerIndex": 1, "explanation": "Third person singular." } ]
        }
        """;

    // ---- Grammar page --------------------------------------------------------------------------

    [TestMethod]
    public void GrammarPage_SearchShowsMatchesAndEnterOpensTheFirst()
    {
        RunOnSta(() =>
        {
            var (page, tutor, _) = CreateGrammarPage();
            var search = (TextBox)page.FindName("SearchInput");

            search.Text = "hiện tại đơn";
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("SearchResultsPanel")).Visibility);
            Assert.IsTrue(((Panel)page.FindName("SearchResultsPanel")).Children.Count >= 1);

            page.SearchAsync().GetAwaiter().GetResult();

            Assert.IsTrue(page.IsLessonOpen);
            Assert.AreEqual("present-simple", page.CurrentTopic!.Key);
            Assert.AreEqual(1, tutor.Calls);
            Assert.AreEqual("Present simple · Thì hiện tại đơn", Text(page, "LessonTitleText"));
        });
    }

    [TestMethod]
    public void GrammarPage_UnknownSearchOpensACustomLesson()
    {
        RunOnSta(() =>
        {
            var (page, tutor, _) = CreateGrammarPage();
            ((TextBox)page.FindName("SearchInput")).Text = "used to";
            StringAssert.Contains(Text(page, "SearchHintText"), "anyway");

            Click(page, "SearchButton");

            Assert.AreEqual("custom:used to", page.CurrentTopic!.Key);
            Assert.AreEqual("custom:used to", tutor.LastTopic!.Key);
        });
    }

    [TestMethod]
    public void GrammarPage_LessonShowsEssenceFirstQuizAndOwnMistakes()
    {
        RunOnSta(() =>
        {
            var history = HistoryWithMistakes(3);
            var (page, tutor, _) = CreateGrammarPage(history);

            page.OpenTopicAsync("present-simple").GetAwaiter().GetResult();

            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("LessonBody")).Visibility);
            Assert.AreEqual("The core idea", Text(page, "EssenceHeading"));
            StringAssert.Contains(Text(page, "EssenceText"), "generally true");
            Assert.AreEqual(1, ((Panel)page.FindName("FormPanel")).Children.Count);
            Assert.AreEqual(1, ((Panel)page.FindName("YourMistakesPanel")).Children.Count, "own mistakes are grouped");
            Assert.AreEqual("Needs review", Text(page, "StatusChipText"));

            var quiz = (Panel)page.FindName("QuizPanel");
            var options = ((Panel)((Panel)quiz.Children[0]).Children[1]).Children.OfType<Button>().ToList();
            options[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.AreEqual(1, page.QuizAnswered);

            page.ShowBrowse();
            page.OpenTopicAsync("present-simple").GetAwaiter().GetResult();
            Assert.AreEqual(1, tutor.Calls, "the saved lesson opens without asking the AI again");

            Click(page, "RegenerateButton");
            Assert.AreEqual(2, tutor.Calls);
        });
    }

    [TestMethod]
    public void GrammarPage_WeakTopicsAndErrorsWithRetry()
    {
        RunOnSta(() =>
        {
            var (page, tutor, _) = CreateGrammarPage(HistoryWithMistakes(4));
            Assert.AreEqual(1, ((Panel)page.FindName("WeakTopicsPanel")).Children.Count);
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)page.FindName("NoWeakTopicsText")).Visibility);

            tutor.Failure = new InvalidOperationException("Groq is busy or your quota is used up (HTTP 429). Try again in a moment.");
            page.OpenTopicAsync("present-simple").GetAwaiter().GetResult();
            StringAssert.Contains(Text(page, "LessonStatusText"), "HTTP 429");
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("LessonRetryButton")).Visibility);

            tutor.Failure = null;
            Click(page, "LessonRetryButton");
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("LessonBody")).Visibility);
        });
    }

    [TestMethod]
    public void GrammarPage_PracticeButtonRaisesTheTopic()
    {
        RunOnSta(() =>
        {
            var (page, _, _) = CreateGrammarPage();
            GrammarTopic? requested = null;
            page.PracticeRequested += topic => requested = topic;
            page.OpenTopicAsync("present-simple").GetAwaiter().GetResult();

            Click(page, "PracticeButton");

            Assert.AreSame(PresentSimple, requested);
            Assert.AreEqual("Practice 5 sentences", ((Button)page.FindName("PracticeButton")).Content);
        });
    }

    // ---- Writing page: drill and hints ---------------------------------------------------------

    [TestMethod]
    public void WritingPage_DrillRunsFiveSentencesAndSavesTheResult()
    {
        RunOnSta(() =>
        {
            var coach = new DrillCoach();
            var history = new WritingHistoryBook(new InMemoryWritingHistoryStore(), () => Now);
            var draft = new StudyListDraft(AppSettings.Default with { WritingRetypeEnabled = false, WritingPracticeMode = WritingPracticeMode.Reverse }, _ => { });
            var page = new WritingPage(new SilentAudio());
            page.Initialize(draft, coach, null, history);

            page.StartDrillAsync(PresentSimple).GetAwaiter().GetResult();

            Assert.AreSame(PresentSimple, coach.Requests[^1].FocusTopic);
            Assert.AreEqual(WritingPracticeMode.Translate, coach.Requests[^1].Mode, "EN → VI drills in Translate");
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("DrillBanner")).Visibility);
            StringAssert.Contains(Text(page, "DrillProgressText"), "sentence 1 of 5");

            for (var i = 0; i < 5; i++)
            {
                coach.MakeTopicMistake = i == 0;
                ((TextBox)page.FindName("AnswerBox")).Text = $"Answer {i}";
                Click(page, "CheckButton");
                if (i < 4)
                    Click(page, "NextButton");
            }

            Assert.IsTrue(page.Drill!.IsComplete);
            Assert.AreEqual(4, page.Drill.Passed);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("DrillSummaryPanel")).Visibility);
            StringAssert.Contains(Text(page, "DrillSummaryText"), "4/5");
            Assert.AreEqual(new GrammarDrillResult(Now, "present-simple", 4, 5), history.Current.Drills.Single());

            Click(page, "NextButton");
            Assert.IsNull(page.Drill, "the next sentence after a finished drill is a normal one");
            Assert.IsNull(coach.Requests[^1].FocusTopic);
        });
    }

    [TestMethod]
    public void WritingPage_RepeatedTopicShowsALearnHint()
    {
        RunOnSta(() =>
        {
            var coach = new DrillCoach { MakeTopicMistake = true };
            var history = HistoryWithMistakes(2);
            var page = new WritingPage(new SilentAudio());
            page.Initialize(new StudyListDraft(AppSettings.Default with { WritingRetypeEnabled = false }, _ => { }), coach, null, history);
            string? requested = null;
            page.GrammarTopicRequested += key => requested = key;

            Click(page, "GenerateButton");
            ((TextBox)page.FindName("AnswerBox")).Text = "She go to work.";
            Click(page, "CheckButton");

            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("GrammarHintButton")).Visibility);
            StringAssert.Contains(Text(page, "GrammarHintText"), "Present simple");
            Click(page, "GrammarHintButton");
            Assert.AreEqual("present-simple", requested);
            Assert.AreEqual(1, ((Panel)page.FindName("GrammarReviewPanel")).Children.Count);
        });
    }

    [TestMethod]
    public void SettingsWindow_GrammarPageAndPracticeGoesToWriting()
    {
        RunOnSta(() =>
        {
            var library = GrammarLessonLibrary.InMemory(new FakeTutor());
            var window = new SettingsWindow(AppSettings.Default, _ => { }, writingCoach: new DrillCoach(), grammarLibrary: library);

            window.OpenGrammarTopic("present-simple");
            Assert.AreEqual(SettingsPageId.Grammar, window.SelectedPage);
            Assert.AreEqual("Grammar", ((TextBlock)window.FindName("PageTitle")).Text);
            var grammar = (GrammarPage)window.FindName("GrammarPageControl");
            Assert.AreEqual("present-simple", grammar.CurrentTopic!.Key);

            Click(grammar, "PracticeButton");
            Assert.AreEqual(SettingsPageId.Writing, window.SelectedPage);
            var writing = (WritingPage)window.FindName("WritingPageControl");
            Assert.AreSame(PresentSimple, writing.Drill!.Topic);
            window.Close();
        });
    }

    [TestMethod]
    public void FloatingPanel_RepeatedTopicHintRaisesTheKey()
    {
        RunOnSta(() =>
        {
            var panel = new FloatingWritingPanel();
            panel.SetHistory(HistoryWithMistakes(2));
            panel.SetCoach(new DrillCoach { MakeTopicMistake = true });
            panel.ActivateAsync(AppSettings.Default with { WritingRetypeEnabled = false }).GetAwaiter().GetResult();
            string? requested = null;
            panel.GrammarTopicRequested += key => requested = key;

            ((TextBox)panel.FindName("AnswerInput")).Text = "She go to work.";
            Click(panel, "CheckButton");
            Click(panel, "GrammarHintButton");

            Assert.AreEqual("present-simple", requested);
        });
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static WritingHistoryBook HistoryWithMistakes(int count)
    {
        var book = new WritingHistoryBook(new InMemoryWritingHistoryStore(), () => Now);
        var feedback = DrillCoach.TopicFeedback();
        for (var i = 0; i < count; i++)
            book.RecordCheck(WritingPracticeMode.Translate, new WritingPrompt($"Câu cũ {i}", "She goes to work.", "Daily life", WritingLevel.Medium, WritingTips.Empty), "She go to work.", feedback);
        return book;
    }

    private static (GrammarPage Page, FakeTutor Tutor, WritingHistoryBook History) CreateGrammarPage(WritingHistoryBook? history = null)
    {
        var tutor = new FakeTutor();
        var book = history ?? new WritingHistoryBook(new InMemoryWritingHistoryStore(), () => Now);
        var page = new GrammarPage(new SilentAudio());
        page.Initialize(new StudyListDraft(AppSettings.Default, _ => { }), GrammarLessonLibrary.InMemory(tutor), book);
        return (page, tutor, book);
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

    private sealed class FakeTutor : IGrammarTutor
    {
        public int Calls { get; private set; }
        public GrammarTopic? LastTopic { get; private set; }
        public Exception? Failure { get; set; }

        public Task<GrammarLesson> CreateLessonAsync(GrammarTopic topic, WritingExplanationLanguage language, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
                return Task.FromException<GrammarLesson>(Failure);
            Calls++;
            LastTopic = topic;
            return Task.FromResult(AiGrammarTutor.ParseLesson(LessonJson, topic, language, Now));
        }
    }

    /// <summary>Echoes the drill topic and returns a present-simple mistake when asked to.</summary>
    private sealed class DrillCoach : IWritingCoach
    {
        public List<WritingRequest> Requests { get; } = [];
        public bool MakeTopicMistake { get; set; }

        public static WritingFeedback TopicFeedback() =>
            new(60, WritingVerdict.Almost, "She goes to work.", null, "Ok.",
                [new WritingIssue(WritingIssueType.Grammar, "go", "goes", "Add -es.") { Tag = WritingSkillTag.Tense, TopicKey = "present-simple" }]);

        public Task<WritingPrompt> GenerateAsync(WritingRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new WritingPrompt($"Câu {Requests.Count}", "She goes to work.", "Daily life", request.Level, WritingTips.Empty)
            {
                Mode = request.Mode,
                FocusTopicKey = request.FocusTopic?.Key
            });
        }

        public Task<WritingFeedback> CheckAsync(WritingPrompt prompt, string translation, WritingExplanationLanguage language, CancellationToken cancellationToken = default) =>
            Task.FromResult(MakeTopicMistake ? TopicFeedback() : new WritingFeedback(95, WritingVerdict.Correct, "She goes to work.", null, "Great.", []));
    }

    private sealed class SilentAudio : ISentenceAudioPlayback
    {
        public AudioPlaybackState State { get; } = new(false, false, null);
        public event Action<AudioPlaybackState>? StateChanged;
        public Task ToggleAsync(string text, string voice, double speed) => Task.CompletedTask;
        public void Stop() => StateChanged?.Invoke(State);
        public void Dispose()
        {
        }
    }
}
