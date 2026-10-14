using System.IO;
using System.Text.Json;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

/// <summary>Grammar topics in checks and generation, the AI tutor, the lesson library and drills.</summary>
[TestClass]
public sealed class GrammarAiTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.FromHours(7));
    private static readonly GrammarTopic PresentSimple = GrammarTopics.Find("present-simple")!;

    private const string LessonJson = """
        {
          "essence": "Thì hiện tại đơn diễn tả những điều luôn đúng hoặc lặp lại.",
          "form": [ { "label": "Khẳng định", "pattern": "S + V(s/es)", "example": "She works here." },
                    { "label": "Phủ định", "pattern": "S + do/does not + V", "example": "He doesn't smoke." } ],
          "uses": [ { "title": "Thói quen", "explanation": "Việc lặp đi lặp lại.", "example": "I walk to work." } ],
          "signalWords": [ "always", "usually", "every day" ],
          "contrast": "Hiện tại tiếp diễn dùng cho việc đang xảy ra.",
          "commonMistakes": [ { "wrong": "She go to work.", "right": "She goes to work.", "why": "Ngôi thứ ba số ít thêm -es." } ],
          "examples": [ { "english": "The train leaves at 8.", "vietnamese": "Tàu chạy lúc 8 giờ." } ],
          "quiz": [ { "question": "He ___ coffee every morning.", "options": ["drink", "drinks", "drinking"], "answerIndex": 1, "explanation": "Ngôi thứ ba số ít." } ]
        }
        """;

    private const string CheckJson = """
        {
          "score": 60, "correctedSentence": "She goes to work by bus.", "naturalAlternative": "", "summary": "Ok.",
          "issues": [
            { "type": "grammar", "tag": "tense", "grammarTopic": "present-simple", "original": "go", "suggestion": "goes", "explanation": "-es." },
            { "type": "vocabulary", "tag": "word_choice", "grammarTopic": "nonsense", "original": "by", "suggestion": "by", "explanation": "x" },
            { "type": "grammar", "tag": "other", "grammarTopic": "custom:used to", "original": "use", "suggestion": "used", "explanation": "y" }
          ]
        }
        """;

    private static (T Service, GeminiTestHandler Handler) With<T>(Func<IJsonLlmClient, T> create, params string[] texts)
    {
        var handler = GeminiTestHandler.Returning(texts);
        var client = new GeminiJsonClient(() => "test-key", new HttpClient(handler), ["model-a"]);
        return (create(client), handler);
    }

    private static WritingPrompt Prompt(string? focusTopicKey = null) =>
        new("Cô ấy đi làm bằng xe buýt.", "She goes to work by bus.", "Daily life", WritingLevel.Medium, WritingTips.Empty) { FocusTopicKey = focusTopicKey };

    // ---- Coach ----------------------------------------------------------------------------------

    [TestMethod]
    public async Task Check_AsksForAGrammarTopicAndKeepsOnlyKnownKeys()
    {
        var (coach, handler) = With(client => new AiWritingCoach(client), CheckJson);

        var feedback = await coach.CheckAsync(Prompt(), "She go to work by bus.", WritingExplanationLanguage.English);

        Assert.AreEqual("present-simple", feedback.Issues[0].TopicKey);
        Assert.IsNull(feedback.Issues[1].TopicKey);
        Assert.IsNull(feedback.Issues[2].TopicKey, "a custom key only counts when the sentence drills it");
        StringAssert.Contains(handler.SystemInstruction(0), "grammarTopic");
        StringAssert.Contains(handler.SystemInstruction(0), "present-perfect (Present perfect)");
    }

    [TestMethod]
    public async Task Check_DrillSentenceMentionsTheTopicAndAcceptsItsCustomKey()
    {
        var (coach, handler) = With(client => new AiWritingCoach(client), CheckJson);

        var feedback = await coach.CheckAsync(Prompt("custom:used to"), "She use to go.", WritingExplanationLanguage.English);

        Assert.AreEqual("custom:used to", feedback.Issues[2].TopicKey);
        StringAssert.Contains(handler.SystemInstruction(0), "This sentence drills custom:used to");
    }

    [TestMethod]
    public async Task Generate_WithFocusTopicDrillsItAndIgnoresWeakPoints()
    {
        const string exercise = """
            { "vietnamese": "Anh ấy thường đi bộ.", "english": "He usually walks.", "topic": "Daily life",
              "tips": { "vocabulary": [], "grammar": "", "starter": "He" } }
            """;
        var (coach, handler) = With(client => new AiWritingCoach(client), exercise);

        var prompt = await coach.GenerateAsync(new WritingRequest(["Daily life"], WritingLevel.Easy, WritingExplanationLanguage.English, [])
        {
            FocusTopic = PresentSimple,
            FocusTags = [WritingSkillTag.Article]
        });

        Assert.AreEqual("present-simple", prompt.FocusTopicKey);
        Assert.AreEqual(0, prompt.FocusTags.Count);
        var system = handler.SystemInstruction(0);
        StringAssert.Contains(system, "drills one grammar point: Present simple");
        Assert.IsFalse(system.Contains("weak points", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Session_PassesTheFocusTopicAndKeysThePrefetchByIt()
    {
        var coach = new StubCoach();
        using var session = new WritingSession(coach, prefetchNext: true);

        await session.GenerateAsync(["Daily life"], WritingLevel.Medium, WritingExplanationLanguage.English, null, PresentSimple);
        await session.GenerateAsync(["Daily life"], WritingLevel.Medium, WritingExplanationLanguage.English, null, PresentSimple);
        Assert.IsTrue(session.PromptWasPrefetched);
        Assert.AreSame(PresentSimple, coach.Requests[0].FocusTopic);

        await session.GenerateAsync(["Daily life"], WritingLevel.Medium, WritingExplanationLanguage.English);
        Assert.IsFalse(session.PromptWasPrefetched, "a drill sentence is not reused outside the drill");
    }

    // ---- Tutor and library ---------------------------------------------------------------------------

    [TestMethod]
    public async Task Tutor_WritesAnEssenceFirstLessonInTheChosenLanguage()
    {
        var (tutor, handler) = With(client => new AiGrammarTutor(client, () => Now), LessonJson);

        var lesson = await tutor.CreateLessonAsync(PresentSimple, WritingExplanationLanguage.Vietnamese);

        Assert.AreEqual("present-simple", lesson.TopicKey);
        Assert.AreEqual("Present simple", lesson.Title);
        Assert.AreEqual(WritingExplanationLanguage.Vietnamese, lesson.Language);
        StringAssert.StartsWith(lesson.Essence, "Thì hiện tại đơn");
        Assert.AreEqual(2, lesson.Form.Count);
        Assert.AreEqual("S + V(s/es)", lesson.Form[0].Pattern);
        Assert.AreEqual(1, lesson.Quiz[0].AnswerIndex);
        Assert.AreEqual(Now, lesson.CreatedAt);
        Assert.IsNotNull(lesson.Call);

        var system = handler.SystemInstruction(0);
        StringAssert.Contains(system, "essence");
        StringAssert.Contains(system, "in Vietnamese");
        using var data = JsonDocument.Parse(handler.UserText(0));
        Assert.AreEqual("Present simple", data.RootElement.GetProperty("topic").GetString());
        Assert.AreEqual("Thì hiện tại đơn", data.RootElement.GetProperty("vietnameseName").GetString());
    }

    [TestMethod]
    public async Task Tutor_LessonWithoutEssenceIsInvalid()
    {
        var (tutor, _) = With(client => new AiGrammarTutor(client), LessonJson.Replace("\"essence\": \"Thì hiện tại đơn diễn tả những điều luôn đúng hoặc lặp lại.\"", "\"essence\": \"\""));

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => tutor.CreateLessonAsync(PresentSimple, WritingExplanationLanguage.English));

        Assert.AreEqual(AiGrammarTutor.InvalidLessonMessage, error.Message);
    }

    [TestMethod]
    public async Task Library_CachesPerTopicAndLanguageAndRefreshes()
    {
        var tutor = new CountingTutor();
        var store = new InMemoryGrammarLessonStore();
        var library = new GrammarLessonLibrary(tutor, store);

        var first = await library.GetAsync(PresentSimple, WritingExplanationLanguage.English);
        var again = await library.GetAsync(PresentSimple, WritingExplanationLanguage.English);
        Assert.AreSame(first, again);
        Assert.AreEqual(1, tutor.Calls);
        Assert.AreSame(first, library.Cached(PresentSimple, WritingExplanationLanguage.English));
        Assert.IsNull(library.Cached(PresentSimple, WritingExplanationLanguage.Vietnamese));

        await library.GetAsync(PresentSimple, WritingExplanationLanguage.Vietnamese);
        var refreshed = await library.GetAsync(PresentSimple, WritingExplanationLanguage.English, refresh: true);
        Assert.AreEqual(3, tutor.Calls);
        Assert.AreNotSame(first, refreshed);
        Assert.AreEqual(2, store.Load().Count);

        var reopened = new GrammarLessonLibrary(null, store);
        Assert.AreEqual(2, reopened.Count);
        Assert.IsFalse(reopened.CanWriteLessons);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => reopened.GetAsync(GrammarTopics.Find("articles")!, WritingExplanationLanguage.English));
    }

    [TestMethod]
    public void JsonLessonStore_RoundTripsLessons()
    {
        var directory = Path.Combine(Path.GetTempPath(), "grammar-lessons-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "grammar-lessons.json");
        try
        {
            var lesson = AiGrammarTutor.ParseLesson(LessonJson, PresentSimple, WritingExplanationLanguage.Vietnamese, Now);
            new JsonGrammarLessonStore(path).Save([lesson]);

            var loaded = new JsonGrammarLessonStore(path).Load();

            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual(lesson.Essence, loaded[0].Essence);
            Assert.AreEqual("drinks", loaded[0].Quiz[0].Options[1]);
            Assert.AreEqual(Now, loaded[0].CreatedAt);

            File.WriteAllText(path, "broken");
            Assert.AreEqual(0, new JsonGrammarLessonStore(path).Load().Count);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    // ---- Drill and notebook ---------------------------------------------------------------------

    [TestMethod]
    public void Drill_CountsEachSentenceOnceAndCompletesAfterFive()
    {
        var drill = new GrammarDrill(PresentSimple);
        var mistake = new WritingFeedback(60, WritingVerdict.Almost, "x", null, "", [new WritingIssue(WritingIssueType.Grammar, "go", "goes", "") { TopicKey = "present-simple" }]);
        var clean = new WritingFeedback(95, WritingVerdict.Correct, "x", null, "", []);

        var first = Prompt();
        Assert.IsFalse(drill.Record(first, mistake));
        Assert.IsFalse(drill.Record(first, clean), "checking the same sentence again does not count");
        for (var i = 0; i < 3; i++)
            Assert.IsFalse(drill.Record(Prompt(), clean));
        Assert.IsTrue(drill.Record(Prompt(), clean));
        Assert.IsFalse(drill.Record(Prompt(), clean));

        Assert.AreEqual(5, drill.Done);
        Assert.AreEqual(4, drill.Passed);
        Assert.IsTrue(drill.IsComplete);
        Assert.AreEqual(new GrammarDrillResult(Now, "present-simple", 4, 5), drill.ToResult(Now));
        Assert.AreEqual(WritingPracticeMode.Translate, GrammarDrill.ModeFor(WritingPracticeMode.Reverse));
        Assert.AreEqual(WritingPracticeMode.Combine, GrammarDrill.ModeFor(WritingPracticeMode.Combine));
    }

    [TestMethod]
    public void HistoryBook_RecordsDrillsAndRecommendsTopics()
    {
        var book = new WritingHistoryBook(new InMemoryWritingHistoryStore(), () => Now);
        var feedback = new WritingFeedback(60, WritingVerdict.Almost, "She goes.", null, "", [new WritingIssue(WritingIssueType.Grammar, "go", "goes", "") { TopicKey = "present-simple" }]);
        for (var i = 0; i < 3; i++)
            book.RecordCheck(WritingPracticeMode.Translate, Prompt() with { Vietnamese = $"Câu {i}" }, "She go.", feedback);

        Assert.AreEqual("present-simple", book.GrammarRecommendations()[0].Topic.Key);
        Assert.AreEqual(1, book.RepeatedTopics(feedback.Issues).Count);
        Assert.AreEqual(3, book.MistakesFor("present-simple", 5)[0].Count);

        book.RecordDrill(new GrammarDrillResult(Now, "present-simple", 5, 5));
        Assert.AreEqual(GrammarTopicStatus.Good, book.StatFor(PresentSimple).Status);
        Assert.AreEqual(0, book.GrammarRecommendations().Count);
    }

    [TestMethod]
    public void Presentation_GrammarTexts()
    {
        Assert.AreEqual("Present simple · Thì hiện tại đơn", GrammarPresentation.TopicTitle(PresentSimple));
        Assert.AreEqual("used to", GrammarPresentation.TopicTitle(GrammarTopics.Custom("used to")!));
        Assert.AreEqual("Bản chất", GrammarPresentation.EssenceHeading(WritingExplanationLanguage.Vietnamese));
        Assert.AreEqual("Practice 5 sentences", GrammarPresentation.PracticeButtonText(WritingExplanationLanguage.English));
        var stat = new GrammarTopicStat(PresentSimple, 6, 4, new GrammarDrillResult(Now, "present-simple", 2, 5), GrammarTopicStatus.NeedsReview, Now);
        Assert.AreEqual("6 mistakes in 30 days · last drill 2/5", GrammarPresentation.StatLine(stat));
        StringAssert.Contains(GrammarPresentation.RepeatHint(stat), "Present simple wrong (4 times)");
        var drill = new GrammarDrill(PresentSimple);
        Assert.AreEqual("Drill · Present simple · sentence 1 of 5 · 0 without Present simple mistakes", GrammarPresentation.DrillProgress(drill));
        StringAssert.Contains(GrammarPresentation.SearchHint("used to", 0), "anyway");
    }

    private sealed class CountingTutor : IGrammarTutor
    {
        public int Calls { get; private set; }

        public Task<GrammarLesson> CreateLessonAsync(GrammarTopic topic, WritingExplanationLanguage language, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(AiGrammarTutor.ParseLesson(LessonJson, topic, language, Now.AddMinutes(Calls)));
        }
    }

    private sealed class StubCoach : IWritingCoach
    {
        public List<WritingRequest> Requests { get; } = [];

        public Task<WritingPrompt> GenerateAsync(WritingRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Prompt(request.FocusTopic?.Key) with { Vietnamese = $"Câu {Requests.Count}" });
        }

        public Task<WritingFeedback> CheckAsync(WritingPrompt prompt, string translation, WritingExplanationLanguage language, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WritingFeedback(90, WritingVerdict.Correct, "x", null, "", []));
    }
}
