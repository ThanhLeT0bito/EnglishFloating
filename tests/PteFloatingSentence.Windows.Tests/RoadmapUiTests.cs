using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

/// <summary>The Roadmap page, guided runs and quick checks, and today's session across the Settings pages.</summary>
[TestClass]
[DoNotParallelize]
public sealed class RoadmapUiTests
{
    // Day 0 of the exercise rotation: stage 1 applies in Translate · Easy (no audio needed).
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.FromHours(7));
    private static readonly GrammarTopic PresentSimple = GrammarTopics.Find("present-simple")!;
    private static readonly GrammarTopic Articles = GrammarTopics.Find("articles")!;
    private static readonly RoadmapPractice TranslateEasy = new(WritingPracticeMode.Translate, WritingLevel.Easy);

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

    // ---- Writing page: guided runs -------------------------------------------------------------

    [TestMethod]
    public void WritingPage_GuidedRunUsesItsExerciseAndCountsItsSentences()
    {
        RunOnSta(() =>
        {
            var coach = new EchoCoach();
            var history = Book();
            var draft = new StudyListDraft(AppSettings.Default with
            {
                WritingRetypeEnabled = false,
                WritingPracticeMode = WritingPracticeMode.Paraphrase,
                WritingLevel = WritingLevel.Hard
            }, _ => { });
            var page = new WritingPage(new SilentAudio());
            page.Initialize(draft, coach, null, history);
            GuidedPractice? completed = null;
            page.GuidedCompleted += run => completed = run;
            var run = GuidedPractice.ForStep(new RoadmapStep(RoadmapStepKind.Review, Articles, TranslateEasy, 2, []));

            page.StartGuidedAsync(run).GetAwaiter().GetResult();

            Assert.AreEqual(WritingPracticeMode.Translate, coach.Requests[^1].Mode);
            Assert.AreEqual(WritingLevel.Easy, coach.Requests[^1].Level);
            Assert.AreSame(Articles, coach.Requests[^1].FocusTopic);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("DrillBanner")).Visibility);
            StringAssert.Contains(Text(page, "DrillProgressText"), "sentence 1 of 2");
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("DrillLessonButton")).Visibility);

            CheckSentences(page, 2);

            Assert.AreSame(run, completed);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("DrillSummaryPanel")).Visibility);
            StringAssert.Contains(Text(page, "DrillSummaryText"), "done");
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)page.FindName("DrillAgainButton")).Visibility);
            Assert.AreEqual(2, history.Current.Checks.Count(check => check.FocusTopicKey == "articles" && check.Level == WritingLevel.Easy));

            Click(page, "NextButton");
            Assert.IsNull(page.Guided, "the next sentence after a finished run is a normal one");
            Assert.AreEqual(WritingPracticeMode.Paraphrase, coach.Requests[^1].Mode);
            Assert.AreEqual(WritingLevel.Hard, coach.Requests[^1].Level);
        });
    }

    [TestMethod]
    public void WritingPage_RoadmapDrillUsesTheStageExerciseAndRaisesCompletion()
    {
        RunOnSta(() =>
        {
            var coach = new EchoCoach();
            var draft = new StudyListDraft(AppSettings.Default with { WritingRetypeEnabled = false, WritingPracticeMode = WritingPracticeMode.Reverse }, _ => { });
            var page = new WritingPage(new SilentAudio());
            page.Initialize(draft, coach, null, Book());
            GrammarDrill? completed = null;
            page.DrillCompleted += drill => completed = drill;

            page.StartDrillAsync(PresentSimple, new RoadmapPractice(WritingPracticeMode.Combine, WritingLevel.Medium)).GetAwaiter().GetResult();

            Assert.AreEqual(WritingPracticeMode.Combine, coach.Requests[^1].Mode);
            Assert.AreEqual(WritingLevel.Medium, coach.Requests[^1].Level);
            CheckSentences(page, GrammarProgressRules.DrillLength);
            Assert.AreSame(page.Drill, completed);
        });
    }

    // ---- Grammar page: quick check -------------------------------------------------------------

    [TestMethod]
    public void GrammarPage_QuickCheckIsSavedAndRaised()
    {
        RunOnSta(() =>
        {
            var history = Book();
            var page = new GrammarPage(new SilentAudio());
            page.Initialize(new StudyListDraft(AppSettings.Default, _ => { }), GrammarLessonLibrary.InMemory(new FakeTutor()), history);
            (GrammarTopic Topic, int Correct, int Total)? completed = null;
            page.QuizCompleted += (topic, correct, total) => completed = (topic, correct, total);
            page.OpenTopicAsync("present-simple").GetAwaiter().GetResult();

            page.AnswerQuiz(0, 0);
            Assert.AreEqual((PresentSimple, 0, 1), completed!.Value);
            Assert.IsFalse(history.Current.Quizzes.Single().IsPass);
            var quiz = (Panel)page.FindName("QuizPanel");
            StringAssert.Contains(((TextBlock)((Panel)((Border)quiz.Children[^1]).Child).Children[0]).Text, "need 1/1");

            page.RetryQuiz();
            Assert.AreEqual(0, page.QuizAnswered);
            page.AnswerQuiz(0, 1);
            page.AnswerQuiz(0, 2);

            Assert.AreEqual(1, page.QuizAnswered, "each question is answered once");
            Assert.AreEqual((1, 1), page.QuizScore());
            Assert.AreEqual(new GrammarQuizResult(Now, "present-simple", 1, 1), history.Current.Quizzes[^1]);
            StringAssert.Contains(((TextBlock)((Panel)((Border)quiz.Children[^1]).Child).Children[0]).Text, "Lesson learned");
        });
    }

    // ---- Roadmap page --------------------------------------------------------------------------

    [TestMethod]
    public void RoadmapPage_ShowsTodayAndEveryStage()
    {
        RunOnSta(() =>
        {
            var history = Book();
            var page = new RoadmapPage();
            page.Initialize(history);
            GrammarTopic? topic = null;
            (RoadmapStage Stage, RoadmapPractice Practice)? practice = null;
            page.TopicRequested += requested => topic = requested;
            page.PracticeRequested += (stage, requested) => practice = (stage, requested);

            Assert.AreEqual("Today · Stage 1 · Simple sentences", Text(page, "TodayTitleText"));
            Assert.AreEqual("Start your streak today", Text(page, "StreakTextBlock"));
            Assert.AreEqual(3, page.Plan.Count);
            Assert.AreEqual(3, ((Panel)page.FindName("TodayPlanPanel")).Children.Count);
            StringAssert.StartsWith((string)((Button)page.FindName("StartSessionButton")).Content, "Start today's session");
            var stages = (Panel)page.FindName("StagesPanel");
            Assert.AreEqual(WritingRoadmap.All.Count, stages.Children.Count);

            var first = (FrameworkElement)stages.Children[0];
            Descendants<Button>(first).First(button => button.Tag is GrammarTopic).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.AreSame(PresentSimple, topic);
            Descendants<Button>(first).First(button => button.Tag is RoadmapPractice).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.AreEqual(("simple-sentences", TranslateEasy), (practice!.Value.Stage.Key, practice.Value.Practice));
            Assert.AreEqual(0, Descendants<Button>((FrameworkElement)stages.Children[^1]).Count(), "the coming-soon stage has nothing to click");

            Descendants<Button>((FrameworkElement)stages.Children[1]).Single(button => "tenses".Equals(button.Tag)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.AreEqual("tenses", page.CurrentStage.Key);
            Assert.AreEqual("Today · Stage 2 · Getting tenses right", Text(page, "TodayTitleText"));
            Assert.AreEqual("present-continuous", page.Plan[0].Topic!.Key);

            Descendants<Button>((FrameworkElement)((Panel)page.FindName("StagesPanel")).Children[1]).Single(button => "follow".Equals(button.Tag))
                .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.AreEqual("simple-sentences", page.CurrentStage.Key);
            Assert.IsNull(history.Current.RoadmapStageKey);
        });
    }

    // ---- Settings window: today's session ------------------------------------------------------

    [TestMethod]
    public void SettingsWindow_TodaySessionRunsLearnDrillAndApplyThenSummarizes()
    {
        RunOnSta(() =>
        {
            var coach = new EchoCoach();
            var history = Book();
            var window = new SettingsWindow(AppSettings.Default with { WritingRetypeEnabled = false }, _ => { }, writingCoach: coach,
                writingHistory: history, grammarLibrary: GrammarLessonLibrary.InMemory(new FakeTutor()));
            window.NavigateTo(SettingsPageId.Roadmap);
            Assert.AreEqual("Roadmap", ((TextBlock)window.FindName("PageTitle")).Text);
            var roadmap = (RoadmapPage)window.FindName("RoadmapPageControl");
            var grammar = (GrammarPage)window.FindName("GrammarPageControl");
            var writing = (WritingPage)window.FindName("WritingPageControl");

            Click(roadmap, "StartSessionButton");

            var session = window.RoadmapSession!;
            CollectionAssert.AreEqual(new[] { RoadmapStepKind.Learn, RoadmapStepKind.Drill, RoadmapStepKind.Apply }, session.Steps.Select(step => step.Kind).ToArray());
            Assert.AreEqual(SettingsPageId.Grammar, window.SelectedPage);
            Assert.AreEqual("present-simple", grammar.CurrentTopic!.Key);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)window.FindName("SessionBanner")).Visibility);
            StringAssert.Contains(Text(window, "SessionBannerText"), "Step 1 of 3 · Learn: Present simple");

            grammar.AnswerQuiz(0, 1);
            Assert.IsTrue(session.IsCurrentDone);
            StringAssert.EndsWith(Text(window, "SessionBannerText"), "done ✓");

            Click(window, "SessionNextButton");
            Assert.AreEqual(SettingsPageId.Writing, window.SelectedPage);
            Assert.AreEqual("present-simple", writing.Drill!.Topic.Key);
            Assert.AreEqual(WritingPracticeMode.Translate, coach.Requests[^1].Mode);
            Assert.AreEqual(WritingLevel.Easy, coach.Requests[^1].Level);
            CheckSentences(writing, GrammarProgressRules.DrillLength);
            Assert.IsTrue(session.IsCurrentDone);

            Click(window, "SessionNextButton");
            Assert.AreEqual(RoadmapStepKind.Apply, session.Current.Kind);
            Assert.AreEqual(RoadmapStepKind.Apply, writing.Guided!.Kind);
            Assert.IsNull(writing.Drill);
            CheckSentences(writing, RoadmapRules.ApplySentences);
            Assert.IsTrue(session.IsCurrentDone);
            Assert.AreEqual("Finish", ((Button)window.FindName("SessionNextButton")).Content);

            Click(window, "SessionNextButton");
            Assert.IsNull(window.RoadmapSession);
            Assert.AreEqual(SettingsPageId.Roadmap, window.SelectedPage);
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)window.FindName("SessionBanner")).Visibility);
            Assert.IsTrue(roadmap.IsSummaryVisible);
            StringAssert.StartsWith(Text(roadmap, "SummaryTextBlock"), "Session done: 3/3 steps · 13 sentences");
            Assert.AreEqual(1, history.Current.Drills.Count);
            Assert.AreEqual("past-simple", roadmap.Plan[0].Topic!.Key, "tomorrow's plan moves on to the next topic");
            window.Close();
        });
    }

    [TestMethod]
    public void SettingsWindow_EndingEarlyStillSummarizes()
    {
        RunOnSta(() =>
        {
            var window = new SettingsWindow(AppSettings.Default with { WritingRetypeEnabled = false }, _ => { }, writingCoach: new EchoCoach(),
                writingHistory: Book(), grammarLibrary: GrammarLessonLibrary.InMemory(new FakeTutor()));
            window.StartRoadmapSession();
            Click(window, "SessionNextButton");
            Assert.AreEqual(SettingsPageId.Writing, window.SelectedPage, "the lesson was skipped");

            Click(window, "SessionEndButton");

            Assert.IsNull(window.RoadmapSession);
            var roadmap = (RoadmapPage)window.FindName("RoadmapPageControl");
            StringAssert.StartsWith(Text(roadmap, "SummaryTextBlock"), "Session done: 0/3 steps · 0 sentences");
            Assert.IsFalse(roadmap.IsSessionActive);
            window.Close();
        });
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static WritingHistoryBook Book() => new(new InMemoryWritingHistoryStore(), () => Now);

    private static void CheckSentences(WritingPage page, int count)
    {
        for (var i = 0; i < count; i++)
        {
            ((TextBox)page.FindName("AnswerBox")).Text = "She goes to work.";
            Click(page, "CheckButton");
            if (i < count - 1)
                Click(page, "NextButton");
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
                yield return match;
            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
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
        public Task<GrammarLesson> CreateLessonAsync(GrammarTopic topic, WritingExplanationLanguage language, CancellationToken cancellationToken = default) =>
            Task.FromResult(AiGrammarTutor.ParseLesson(LessonJson, topic, language, Now));
    }

    /// <summary>Writes the requested exercise and level back, and accepts every answer.</summary>
    private sealed class EchoCoach : IWritingCoach
    {
        public List<WritingRequest> Requests { get; } = [];

        public Task<WritingPrompt> GenerateAsync(WritingRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new WritingPrompt($"Câu {Requests.Count}", "She goes to work.", "Daily life", request.Level, WritingTips.Empty)
            {
                Mode = request.Mode,
                SourceEnglish = request.Mode is WritingPracticeMode.Paraphrase or WritingPracticeMode.Combine ? "He works. He is busy." : string.Empty,
                FocusTopicKey = request.FocusTopic?.Key
            });
        }

        public Task<WritingFeedback> CheckAsync(WritingPrompt prompt, string translation, WritingExplanationLanguage language, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WritingFeedback(95, WritingVerdict.Correct, "She goes to work.", null, "Great.", []));
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
