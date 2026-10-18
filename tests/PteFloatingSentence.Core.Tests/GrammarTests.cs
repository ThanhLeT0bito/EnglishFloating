namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class GrammarTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.FromHours(7));

    private static WritingFeedback Feedback(params WritingIssue[] issues) =>
        new(70, WritingVerdict.Almost, "Corrected.", null, "Ok.", issues);

    private static WritingIssue TopicIssue(string topicKey, string original = "go") =>
        new(WritingIssueType.Grammar, original, "goes", "Third person -s.") { Tag = WritingSkillTag.Tense, TopicKey = topicKey };

    private static WritingHistory WithTopicMistakes(string topicKey, int count, DateTimeOffset at, WritingHistory? history = null)
    {
        var result = history ?? WritingHistory.Empty;
        for (var i = 0; i < count; i++)
            result = WritingHistoryRules.RecordFeedback(result, WritingPracticeMode.Translate, $"Câu {topicKey} {at:O} {i}", "She go", Feedback(TopicIssue(topicKey)), at);
        return result;
    }

    // ---- Catalog and search -------------------------------------------------------------------

    [TestMethod]
    public void Catalog_HasUniqueKeysAndBothNames()
    {
        Assert.IsTrue(GrammarTopics.All.Count >= 25);
        Assert.AreEqual(GrammarTopics.All.Count, GrammarTopics.All.Select(topic => topic.Key).Distinct().Count());
        Assert.IsTrue(GrammarTopics.All.All(topic => topic.Name.Length > 0 && topic.VietnameseName.Length > 0 && topic.FocusHint.Length > 0));
        Assert.IsTrue(GrammarTopics.All.All(topic => topic.Category != GrammarCategory.Custom));
        StringAssert.Contains(GrammarTopics.PromptList, "present-simple (Present simple)");
    }

    [TestMethod]
    public void Search_FindsVietnameseWithOrWithoutDiacritics()
    {
        Assert.AreEqual("present-simple", GrammarTopics.Search("hiện tại đơn")[0].Key);
        Assert.AreEqual("present-simple", GrammarTopics.Search("hien tai don")[0].Key);
        Assert.AreEqual("present-perfect", GrammarTopics.Search("Hiện tại hoàn thành")[0].Key);
        Assert.AreEqual("passive-voice", GrammarTopics.Search("bị động")[0].Key);
        Assert.AreEqual("passive-voice", GrammarTopics.Search("passive")[0].Key);
        Assert.AreEqual("conditionals", GrammarTopics.Search("câu điều kiện")[0].Key);
        Assert.AreEqual("articles", GrammarTopics.Search("a an the")[0].Key);
    }

    [TestMethod]
    public void Search_EmptyQueryListsEverythingAndNonsenseFindsNothing()
    {
        Assert.AreEqual(GrammarTopics.All.Count, GrammarTopics.Search("  ").Count);
        Assert.AreEqual(0, GrammarTopics.Search("zzzqqq").Count);
    }

    [TestMethod]
    public void Fold_RemovesDiacriticsAndPunctuation()
    {
        Assert.AreEqual("hien tai don", GrammarTopics.Fold("  Hiện tại   đơn! "));
        Assert.AreEqual("dong tu", GrammarTopics.Fold("Động từ"));
    }

    [TestMethod]
    public void CustomTopics_AreKeyedByTheirFoldedName()
    {
        var custom = GrammarTopics.Custom("  Used   to ")!;
        Assert.AreEqual("custom:used to", custom.Key);
        Assert.AreEqual("Used to", custom.Name);
        Assert.IsTrue(custom.IsCustom);
        Assert.AreEqual(GrammarCategory.Custom, custom.Category);
        Assert.IsNull(GrammarTopics.Custom(""));
        Assert.IsNull(GrammarTopics.Custom(new string('x', GrammarTopics.MaxCustomLength + 1)));
        Assert.AreEqual("custom:used to", GrammarTopics.Resolve("custom:used to")!.Key);
        Assert.AreEqual("Past simple", GrammarTopics.Resolve("past-simple")!.Name);
        Assert.IsNull(GrammarTopics.Resolve("nope"));
    }

    // ---- Mistakes and progress ------------------------------------------------------------------

    [TestMethod]
    public void RecordFeedback_KeepsKnownTopicKeysOnly()
    {
        var history = WritingHistoryRules.RecordFeedback(WritingHistory.Empty, WritingPracticeMode.Translate, "Câu", "a",
            Feedback(TopicIssue("present-simple", "go"), TopicIssue("made-up", "went")), Now);

        Assert.AreEqual("present-simple", history.Mistakes[0].TopicKey);
        Assert.IsNull(history.Mistakes[1].TopicKey);
    }

    [TestMethod]
    public void Status_NeedsReviewAfterThreeMistakes()
    {
        var topic = GrammarTopics.Find("present-simple")!;
        Assert.AreEqual(GrammarTopicStatus.NotStarted, GrammarProgressRules.StatFor(WritingHistory.Empty, topic, Now).Status);
        Assert.AreEqual(GrammarTopicStatus.Improving, GrammarProgressRules.StatFor(WithTopicMistakes(topic.Key, 2, Now), topic, Now).Status);

        var stat = GrammarProgressRules.StatFor(WithTopicMistakes(topic.Key, 3, Now), topic, Now);
        Assert.AreEqual(GrammarTopicStatus.NeedsReview, stat.Status);
        Assert.AreEqual(3, stat.RecentMistakes);
        Assert.AreEqual(Now, stat.LastMistake);
    }

    [TestMethod]
    public void Status_PassedDrillMakesItGoodUntilTheNextMistake()
    {
        var topic = GrammarTopics.Find("articles")!;
        var history = WithTopicMistakes(topic.Key, 4, Now.AddDays(-2));
        history = GrammarProgressRules.RecordDrill(history, new GrammarDrillResult(Now.AddDays(-1), topic.Key, 4, 5));

        var good = GrammarProgressRules.StatFor(history, topic, Now);
        Assert.AreEqual(GrammarTopicStatus.Good, good.Status);
        Assert.AreEqual(4, good.RecentMistakes);
        Assert.AreEqual(0, good.MistakesSinceDrill);

        history = WithTopicMistakes(topic.Key, 1, Now, history);
        Assert.AreEqual(GrammarTopicStatus.Improving, GrammarProgressRules.StatFor(history, topic, Now).Status);
    }

    [TestMethod]
    public void Status_FailedDrillIsImproving()
    {
        var topic = GrammarTopics.Find("articles")!;
        var history = GrammarProgressRules.RecordDrill(WritingHistory.Empty, new GrammarDrillResult(Now, topic.Key, 2, 5));

        Assert.AreEqual(GrammarTopicStatus.Improving, GrammarProgressRules.StatFor(history, topic, Now).Status);
        Assert.IsFalse(history.Drills[0].IsPass);
        Assert.IsTrue(new GrammarDrillResult(Now, topic.Key, 4, 5).IsPass);
    }

    [TestMethod]
    public void Recommendations_NeedsReviewFirstThenMostMistakes()
    {
        var history = WithTopicMistakes("past-simple", 2, Now);
        history = WithTopicMistakes("present-simple", 3, Now.AddDays(-3), history);
        history = WithTopicMistakes("articles", 5, Now.AddDays(-1), history);
        history = WithTopicMistakes("prepositions", 4, Now.AddDays(-40), history);

        var keys = GrammarProgressRules.Recommendations(history, Now).Select(stat => stat.Topic.Key).ToArray();

        CollectionAssert.AreEqual(new[] { "articles", "present-simple", "past-simple" }, keys);
    }

    [TestMethod]
    public void RepeatedTopics_OnlyTopicsThatNeedReview()
    {
        var history = WithTopicMistakes("present-simple", 3, Now);
        history = WithTopicMistakes("past-simple", 1, Now, history);

        var repeated = GrammarProgressRules.RepeatedTopics(history, [TopicIssue("present-simple"), TopicIssue("past-simple"), TopicIssue("present-simple")], Now);

        Assert.AreEqual(1, repeated.Count);
        Assert.AreEqual("present-simple", repeated[0].Topic.Key);
    }

    [TestMethod]
    public void MistakesFor_GroupsTheTopicsOwnMistakes()
    {
        var history = WithTopicMistakes("present-simple", 3, Now);
        history = WithTopicMistakes("past-simple", 2, Now, history);

        var groups = GrammarProgressRules.MistakesFor(history, "present-simple", 5);

        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(3, groups[0].Count);
    }

    [TestMethod]
    public void PassesTopic_ChecksTheTopicKeyOrTheScoreForCustomTopics()
    {
        var topic = GrammarTopics.Find("present-simple")!;
        Assert.IsFalse(GrammarProgressRules.PassesTopic(Feedback(TopicIssue("present-simple")), topic));
        Assert.IsTrue(GrammarProgressRules.PassesTopic(Feedback(TopicIssue("articles")), topic));

        var custom = GrammarTopics.Custom("used to")!;
        Assert.IsTrue(GrammarProgressRules.PassesTopic(new WritingFeedback(85, WritingVerdict.Almost, "x", null, "", []), custom));
        Assert.IsFalse(GrammarProgressRules.PassesTopic(new WritingFeedback(50, WritingVerdict.NeedsWork, "x", null, "", []), custom));
    }

    [TestMethod]
    public void Normalize_KeepsValidDrillsAndTopicKeys()
    {
        var history = new WritingHistory
        {
            Mistakes = [new WritingMistake(Now, WritingPracticeMode.Translate, WritingIssueType.Grammar, WritingSkillTag.Tense, "go", "went", "", "s", "a", "c") { TopicKey = "past-simple" }],
            Drills = [new GrammarDrillResult(Now, "past-simple", 3, 5), new GrammarDrillResult(Now, "past-simple", 9, 5), null!]
        };

        var normalized = WritingHistoryRules.Normalize(history);

        Assert.AreEqual("past-simple", normalized.Mistakes[0].TopicKey);
        Assert.AreEqual(1, normalized.Drills.Count);
    }

    // ---- Lessons --------------------------------------------------------------------------------

    private static GrammarLesson Lesson(params GrammarQuizItem[] quiz) => new(
        "present-simple", " Present simple ", WritingExplanationLanguage.Vietnamese,
        "  Diễn tả thói quen   và sự thật. ",
        [new("Khẳng định", "S + V(s/es)", "She works here."), new("", "", "")],
        [new("Thói quen", "Việc lặp lại.", "I walk to work.")],
        ["always", "Always", "usually", ""],
        "Khác hiện tại tiếp diễn.",
        [new("She work here.", "She works here.", "Ngôi thứ ba thêm -s.")],
        [new("He plays football.", "Anh ấy chơi bóng đá."), new("", "x")],
        quiz,
        Now);

    [TestMethod]
    public void Sanitize_CleansListsAndDropsBrokenQuizItems()
    {
        var lesson = GrammarLessonRules.Sanitize(Lesson(
            new GrammarQuizItem("She ___ coffee.", ["drink", "drinks"], 1, "Third person."),
            new GrammarQuizItem("Bad", ["a"], 0, ""),
            new GrammarQuizItem("Bad index", ["a", "b"], 5, ""),
            new GrammarQuizItem("Duplicate", ["a", "A"], 0, "")))!;

        Assert.AreEqual("Present simple", lesson.Title);
        Assert.AreEqual("Diễn tả thói quen và sự thật.", lesson.Essence);
        Assert.AreEqual(1, lesson.Form.Count);
        CollectionAssert.AreEqual(new[] { "always", "usually" }, lesson.SignalWords.ToArray());
        Assert.AreEqual(1, lesson.Examples.Count);
        Assert.AreEqual(1, lesson.Quiz.Count);
        Assert.AreEqual(1, lesson.Quiz[0].AnswerIndex);
    }

    [TestMethod]
    public void Sanitize_RejectsALessonWithoutEssenceOrExamples()
    {
        Assert.IsNull(GrammarLessonRules.Sanitize(Lesson() with { Essence = " " }));
        Assert.IsNull(GrammarLessonRules.Sanitize(Lesson() with { Examples = [] }));
        Assert.IsNull(GrammarLessonRules.Sanitize(null));
        Assert.AreEqual("present-simple|Vietnamese", GrammarLessonRules.CacheKey("present-simple", WritingExplanationLanguage.Vietnamese));
    }
}
