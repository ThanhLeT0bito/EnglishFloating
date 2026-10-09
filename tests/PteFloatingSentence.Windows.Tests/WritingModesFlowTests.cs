using System.Text.Json;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

/// <summary>Paraphrase, combine and English → Vietnamese exercises: AI prompts, parsing and the session flow.</summary>
[TestClass]
public sealed class WritingModesFlowTests
{
    private static readonly string[] Topics = ["Work & meetings"];

    private const string ParaphraseJson = """
        {
          "source": "The meeting was postponed because it rained.",
          "english": "Because of the rain, the meeting was put off.",
          "vietnamese": "Cuộc họp bị hoãn vì trời mưa.",
          "topic": "Work & meetings",
          "tips": { "vocabulary": [ { "vietnamese": "postponed", "english": "put off", "partOfSpeech": "verb" } ], "grammar": "Start with 'Because of'.", "starter": "Because of" }
        }
        """;

    private const string CombineJson = """
        {
          "sentences": ["It was raining.", "We went for a walk."],
          "connector": "although",
          "english": "Although it was raining, we went for a walk.",
          "vietnamese": "Mặc dù trời mưa, chúng tôi vẫn đi dạo.",
          "topic": "Work & meetings",
          "tips": { "vocabulary": [], "grammar": "Although + clause, main clause.", "starter": "Although" }
        }
        """;

    private const string ReverseJson = """
        {
          "vietnamese": "Cuộc họp đã bị hủy vào phút chót.",
          "english": "The meeting was cancelled at the last minute.",
          "topic": "Work & meetings",
          "tips": { "vocabulary": [ { "vietnamese": "vào phút chót", "english": "at the last minute", "partOfSpeech": "phrase" } ], "grammar": "Passive voice.", "starter": "Cuộc họp" }
        }
        """;

    private const string CheckJson = """
        { "score": 85, "correctedSentence": "Fixed.", "naturalAlternative": "", "summary": "Good.", "issues": [] }
        """;

    private static (AiWritingCoach Coach, GeminiTestHandler Handler) CoachReturning(params string[] texts)
    {
        var handler = GeminiTestHandler.Returning(texts);
        var client = new GeminiJsonClient(() => "test-key", new HttpClient(handler), ["model-a"]);
        return (new AiWritingCoach(client), handler);
    }

    private static WritingRequest Request(WritingPracticeMode mode) =>
        new(Topics, WritingLevel.Medium, WritingExplanationLanguage.English, []) { Mode = mode };

    // ---- AI coach -------------------------------------------------------------------------------

    [TestMethod]
    public async Task Paraphrase_GenerateParsesSourceAndAsksForADifferentWording()
    {
        var (coach, handler) = CoachReturning(ParaphraseJson);

        var prompt = await coach.GenerateAsync(Request(WritingPracticeMode.Paraphrase));

        Assert.AreEqual(WritingPracticeMode.Paraphrase, prompt.Mode);
        Assert.AreEqual("The meeting was postponed because it rained.", prompt.SourceEnglish);
        Assert.AreEqual("Because of the rain, the meeting was put off.", prompt.ReferenceEnglish);
        Assert.AreEqual("postponed", prompt.Tips.Vocabulary[0].Vietnamese);
        StringAssert.Contains(handler.SystemInstruction(0), "paraphrasing exercises");
        StringAssert.Contains(handler.SystemInstruction(0), "different wording and structure");
        using var data = JsonDocument.Parse(handler.UserText(0));
        StringAssert.Contains(data.RootElement.GetProperty("levelRules").GetString()!, "paraphrase");
    }

    [TestMethod]
    public async Task Paraphrase_ModelThatRepeatsTheSourceIsRejected()
    {
        var (coach, _) = CoachReturning(ParaphraseJson.Replace("Because of the rain, the meeting was put off.", "The meeting was postponed because it rained."));

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => coach.GenerateAsync(Request(WritingPracticeMode.Paraphrase)));

        Assert.AreEqual(AiWritingCoach.InvalidExerciseMessage, error.Message);
    }

    [TestMethod]
    public async Task Combine_GenerateParsesTwoSentencesAndConnector()
    {
        var (coach, handler) = CoachReturning(CombineJson);

        var prompt = await coach.GenerateAsync(Request(WritingPracticeMode.Combine));

        Assert.AreEqual(WritingPracticeMode.Combine, prompt.Mode);
        Assert.AreEqual("It was raining.\nWe went for a walk.", prompt.SourceEnglish);
        Assert.AreEqual("although", prompt.Instruction);
        StringAssert.Contains(handler.SystemInstruction(0), "sentence-combining");
        using var data = JsonDocument.Parse(handler.UserText(0));
        StringAssert.Contains(data.RootElement.GetProperty("levelRules").GetString()!, "although");
    }

    [TestMethod]
    public async Task Combine_WithoutConnectorOrSecondSentenceIsRejected()
    {
        var (noConnector, _) = CoachReturning(CombineJson.Replace("\"connector\": \"although\"", "\"connector\": \"\""));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => noConnector.GenerateAsync(Request(WritingPracticeMode.Combine)));

        var (oneSentence, _) = CoachReturning(CombineJson.Replace("\"sentences\": [\"It was raining.\", \"We went for a walk.\"]", "\"sentences\": [\"It was raining.\"]"));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => oneSentence.GenerateAsync(Request(WritingPracticeMode.Combine)));
    }

    [TestMethod]
    public async Task Reverse_GenerateKeepsVietnameseStarterAndIgnoresWeakPoints()
    {
        var (coach, handler) = CoachReturning(ReverseJson);

        var prompt = await coach.GenerateAsync(Request(WritingPracticeMode.Reverse) with { FocusTags = [WritingSkillTag.Tense] });

        Assert.AreEqual(WritingPracticeMode.Reverse, prompt.Mode);
        Assert.AreEqual("Cuộc họp", prompt.Tips.Starter);
        Assert.AreEqual(0, prompt.FocusTags.Count);
        StringAssert.Contains(handler.SystemInstruction(0), "English-to-Vietnamese");
        Assert.IsFalse(handler.SystemInstruction(0).Contains("weak points", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Dictation_RequestsATranslatePrompt()
    {
        var coach = new StubCoach();
        using var session = new WritingSession(coach) { Mode = WritingPracticeMode.Dictation };

        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);

        Assert.AreEqual(WritingPracticeMode.Translate, coach.Requests[0].Mode);
    }

    [TestMethod]
    public async Task Check_SendsModeSpecificDataAndRules()
    {
        var (coach, handler) = CoachReturning(CheckJson, CheckJson, CheckJson);
        var paraphrase = new WritingPrompt("Cuộc họp bị hoãn.", "Because of the rain, the meeting was put off.", "Work", WritingLevel.Medium, WritingTips.Empty)
        { Mode = WritingPracticeMode.Paraphrase, SourceEnglish = "The meeting was postponed because it rained." };
        var combine = new WritingPrompt("Mặc dù trời mưa…", "Although it was raining, we went for a walk.", "Weather", WritingLevel.Medium, WritingTips.Empty)
        { Mode = WritingPracticeMode.Combine, SourceEnglish = "It was raining.\nWe went for a walk.", Instruction = "although" };
        var reverse = new WritingPrompt("Cuộc họp đã bị hủy.", "The meeting was cancelled.", "Work", WritingLevel.Medium, WritingTips.Empty)
        { Mode = WritingPracticeMode.Reverse };

        await coach.CheckAsync(paraphrase, "The rain caused the meeting to be delayed.", WritingExplanationLanguage.English);
        await coach.CheckAsync(combine, "Although it was raining we walked.", WritingExplanationLanguage.English);
        await coach.CheckAsync(reverse, "Cuộc hop bị huỷ", WritingExplanationLanguage.Vietnamese);

        using (var data = JsonDocument.Parse(handler.UserText(0)))
            Assert.AreEqual(paraphrase.SourceEnglish, data.RootElement.GetProperty("originalSentence").GetString());
        StringAssert.Contains(handler.SystemInstruction(0), "copied a long part");

        using (var data = JsonDocument.Parse(handler.UserText(1)))
        {
            Assert.AreEqual("although", data.RootElement.GetProperty("connector").GetString());
            Assert.AreEqual(2, data.RootElement.GetProperty("sentences").GetArrayLength());
        }
        StringAssert.Contains(handler.SystemInstruction(1), "sentence_structure");

        using (var data = JsonDocument.Parse(handler.UserText(2)))
            Assert.AreEqual("The meeting was cancelled.", data.RootElement.GetProperty("englishSentence").GetString());
        StringAssert.Contains(handler.SystemInstruction(2), "diacritics");
    }

    // ---- Session --------------------------------------------------------------------------------

    [TestMethod]
    public async Task Session_ParaphraseThatCopiesTheSourceIsStoppedWithoutAi()
    {
        var coach = new StubCoach();
        using var session = new WritingSession(coach) { Mode = WritingPracticeMode.Paraphrase };
        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);

        await session.CheckAsync("the meeting was postponed because it rained", WritingExplanationLanguage.English);

        Assert.AreEqual(0, coach.CheckCalls);
        Assert.AreEqual(WritingSession.CopiedParaphraseMessage, session.Error);
        Assert.AreEqual(WritingSessionState.Answering, session.State);
        Assert.IsFalse(session.CanRetry);
    }

    [TestMethod]
    public async Task Session_RecordsTheEnglishSourceForParaphraseMistakes()
    {
        var coach = new StubCoach
        {
            Feedback = new WritingFeedback(60, WritingVerdict.Almost, "The rain made them delay the meeting.", null, "Ok.",
                [new WritingIssue(WritingIssueType.Grammar, "delayed", "delay", "Base form after make.") { Tag = WritingSkillTag.WordForm }])
        };
        var book = WritingHistoryBook.InMemory();
        using var session = new WritingSession(coach) { Mode = WritingPracticeMode.Paraphrase, History = book };
        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);

        await session.CheckAsync("The rain made them delayed the meeting.", WritingExplanationLanguage.English);

        Assert.AreEqual(1, coach.CheckCalls);
        Assert.AreEqual(WritingPracticeMode.Paraphrase, session.FeedbackMode);
        Assert.AreEqual("The meeting was postponed because it rained.", book.Current.Mistakes[0].Source);
        Assert.AreEqual(WritingPracticeMode.Paraphrase, book.Current.Mistakes[0].Mode);
    }

    [TestMethod]
    public async Task Session_ModeChangeMarksPromptAndGenerationSwitchesKind()
    {
        var coach = new StubCoach();
        using var session = new WritingSession(coach, prefetchNext: true);
        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);
        Assert.IsFalse(session.PromptNeedsNewMode);

        session.Mode = WritingPracticeMode.Dictation;
        Assert.IsFalse(session.PromptNeedsNewMode, "dictation reuses a translate sentence");
        Assert.AreEqual(WritingPracticeMode.Dictation, session.PromptMode);

        session.Mode = WritingPracticeMode.Combine;
        Assert.IsTrue(session.PromptNeedsNewMode);
        Assert.AreEqual(WritingPracticeMode.Translate, session.PromptMode, "until a new sentence comes, the old one keeps its mode");

        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);
        Assert.IsFalse(session.PromptWasPrefetched, "the prefetched translate sentence is not reused for combine");
        Assert.AreEqual(WritingPracticeMode.Combine, session.Prompt!.Mode);
        Assert.AreEqual(WritingPracticeMode.Combine, coach.Requests[^1].Mode);
    }

    [TestMethod]
    public async Task Session_MismatchedPromptIsCheckedInItsOwnMode()
    {
        var coach = new StubCoach();
        using var session = new WritingSession(coach);
        await session.GenerateAsync(Topics, WritingLevel.Medium, WritingExplanationLanguage.English);
        session.Mode = WritingPracticeMode.Reverse;

        await session.CheckAsync("I go to work by bus.", WritingExplanationLanguage.English);

        Assert.AreEqual(1, coach.CheckCalls);
        Assert.AreEqual(WritingPracticeMode.Translate, session.FeedbackMode);
    }

    [TestMethod]
    public void Presentation_LabelsForEveryMode()
    {
        var combine = new WritingPrompt("vi", "en", "t", WritingLevel.Easy, WritingTips.Empty) { Mode = WritingPracticeMode.Combine, Instruction = "although", SourceEnglish = "a\nb" };
        Assert.AreEqual("Combine into one sentence", WritingPresentation.PromptHeader(WritingPracticeMode.Combine));
        Assert.AreEqual("Use: although", WritingPresentation.InstructionText(combine, WritingPracticeMode.Combine));
        Assert.AreEqual("Nghĩa: vi", WritingPresentation.MeaningLine(combine, WritingPracticeMode.Combine));
        Assert.AreEqual("Your paraphrase", WritingPresentation.AnswerLabel(WritingPracticeMode.Paraphrase, false));
        Assert.AreEqual("Type the corrected sentence", WritingPresentation.AnswerLabel(WritingPracticeMode.Reverse, true));
        StringAssert.Contains(WritingPresentation.AnswerPlaceholder(WritingPracticeMode.Reverse, false), "Vietnamese");
        Assert.IsFalse(WritingPresentation.ShowsListenBar(WritingPracticeMode.Translate));
        Assert.IsTrue(WritingPresentation.ShowsListenBar(WritingPracticeMode.Paraphrase));
        Assert.AreEqual("EN → VI", WritingPresentation.ModeLabel(WritingPracticeMode.Reverse));
        Assert.AreEqual("traffic jam → kẹt xe (noun)", WritingPresentation.VocabularyLine(new WritingVocabularyHint("kẹt xe", "traffic jam", "noun"), WritingPracticeMode.Reverse));
        Assert.AreEqual("Combine · Mixed topics · Easy · Explain in English",
            WritingPresentation.SetupSummary([], WritingLevel.Easy, WritingExplanationLanguage.English, WritingPracticeMode.Combine));
    }

    /// <summary>Returns a prompt of the requested kind.</summary>
    private sealed class StubCoach : IWritingCoach
    {
        public List<WritingRequest> Requests { get; } = [];
        public int CheckCalls { get; private set; }
        public WritingFeedback Feedback { get; set; } = new(90, WritingVerdict.Correct, "Fixed.", null, "Good.", []);

        public Task<WritingPrompt> GenerateAsync(WritingRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var prompt = request.Mode switch
            {
                WritingPracticeMode.Paraphrase => new WritingPrompt("Cuộc họp bị hoãn vì mưa.", "Because of the rain, the meeting was put off.", "Work & meetings", request.Level, WritingTips.Empty)
                { SourceEnglish = "The meeting was postponed because it rained." },
                WritingPracticeMode.Combine => new WritingPrompt("Mặc dù trời mưa, chúng tôi vẫn đi.", "Although it was raining, we went.", "Work & meetings", request.Level, WritingTips.Empty)
                { SourceEnglish = "It was raining.\nWe went.", Instruction = "although" },
                _ => new WritingPrompt($"Câu {Requests.Count}", "I go to work by bus.", "Work & meetings", request.Level, WritingTips.Empty)
            };
            return Task.FromResult(prompt with { Mode = request.Mode });
        }

        public Task<WritingFeedback> CheckAsync(WritingPrompt prompt, string translation, WritingExplanationLanguage language, CancellationToken cancellationToken = default)
        {
            CheckCalls++;
            return Task.FromResult(Feedback);
        }
    }
}
