namespace PteFloatingSentence.Core.Tests;

[TestClass]
public sealed class WritingHistoryRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.FromHours(7));

    private static WritingIssue Issue(WritingIssueType type, WritingSkillTag? tag = null, string original = "x") =>
        tag is null ? new(type, original, "y", "why") : new(type, original, "y", "why") { Tag = tag.Value };

    private static WritingFeedback Feedback(params WritingIssue[] issues) =>
        new(70, WritingVerdict.Almost, "Corrected.", null, "Ok.", issues);

    private static WritingHistory WithMistakes(WritingSkillTag tag, int count, DateTimeOffset at, WritingHistory? history = null)
    {
        var result = history ?? WritingHistory.Empty;
        for (var i = 0; i < count; i++)
            result = WritingHistoryRules.RecordFeedback(result, WritingPracticeMode.Translate, $"Câu {tag} {at:O} {i}", "Answer", Feedback(Issue(WritingIssueType.Grammar, tag)), at);
        return result;
    }

    [TestMethod]
    public void Issue_TagDefaultsFromType()
    {
        Assert.AreEqual(WritingSkillTag.Spelling, Issue(WritingIssueType.Spelling).Tag);
        Assert.AreEqual(WritingSkillTag.MissingWord, Issue(WritingIssueType.MissingMeaning).Tag);
        Assert.AreEqual(WritingSkillTag.WordChoice, Issue(WritingIssueType.Vocabulary).Tag);
        Assert.AreEqual(WritingSkillTag.Other, Issue(WritingIssueType.Grammar).Tag);
        Assert.AreEqual(WritingSkillTag.Tense, Issue(WritingIssueType.Grammar, WritingSkillTag.Tense).Tag);
    }

    [TestMethod]
    public void ParseTag_AcceptsCommonSpellingsAndFallsBackToType()
    {
        Assert.AreEqual(WritingSkillTag.SubjectVerbAgreement, WritingSkillTags.Parse("subject_verb_agreement", WritingIssueType.Grammar));
        Assert.AreEqual(WritingSkillTag.Article, WritingSkillTags.Parse("Articles", WritingIssueType.Grammar));
        Assert.AreEqual(WritingSkillTag.WordForm, WritingSkillTags.Parse("wordForm", WritingIssueType.Grammar));
        Assert.AreEqual(WritingSkillTag.Spelling, WritingSkillTags.Parse("???", WritingIssueType.Spelling));
        Assert.AreEqual(WritingSkillTag.Other, WritingSkillTags.Parse(null, WritingIssueType.Grammar));
        Assert.AreEqual(WritingSkillTag.WordOrder, WritingSkillTags.Parse("other", WritingIssueType.WordOrder));
    }

    [TestMethod]
    public void RecordFeedback_SkipsPunctuationAndKeepsDetails()
    {
        var history = WritingHistoryRules.RecordFeedback(
            WritingHistory.Empty,
            WritingPracticeMode.Translate,
            "Tôi đã sống ở đây.",
            "I live here.",
            Feedback(Issue(WritingIssueType.Grammar, WritingSkillTag.Tense, "live"), Issue(WritingIssueType.Punctuation, original: "here")),
            Now);

        Assert.AreEqual(1, history.Mistakes.Count);
        var mistake = history.Mistakes[0];
        Assert.AreEqual(WritingSkillTag.Tense, mistake.Tag);
        Assert.AreEqual("live", mistake.Original);
        Assert.AreEqual("Tôi đã sống ở đây.", mistake.Source);
        Assert.AreEqual("I live here.", mistake.Answer);
        Assert.AreEqual("Corrected.", mistake.Corrected);
        Assert.AreEqual(Now, mistake.At);
    }

    [TestMethod]
    public void RecordFeedback_WithoutMistakesReturnsSameHistory()
    {
        var history = WritingHistory.Empty;
        Assert.AreSame(history, WritingHistoryRules.RecordFeedback(history, WritingPracticeMode.Translate, "a", "b", Feedback(), Now));
    }

    [TestMethod]
    public void RecordFeedback_KeepsNewestMistakesWithinCap()
    {
        var history = WithMistakes(WritingSkillTag.Article, WritingHistoryRules.MaxMistakes, Now.AddDays(-1));
        history = WithMistakes(WritingSkillTag.Tense, 3, Now, history);

        Assert.AreEqual(WritingHistoryRules.MaxMistakes, history.Mistakes.Count);
        Assert.AreEqual(WritingSkillTag.Tense, history.Mistakes[^1].Tag);
    }

    [TestMethod]
    public void TagStats_CountsRecentMistakesWeakestFirst()
    {
        var history = WithMistakes(WritingSkillTag.Article, 2, Now.AddDays(-2));
        history = WithMistakes(WritingSkillTag.Tense, 4, Now.AddDays(-1), history);
        history = WithMistakes(WritingSkillTag.Preposition, 9, Now.AddDays(-40), history);

        var stats = WritingHistoryRules.TagStats(history, Now);

        Assert.AreEqual(2, stats.Count);
        Assert.AreEqual(WritingSkillTag.Tense, stats[0].Tag);
        Assert.AreEqual(4, stats[0].Mistakes);
        Assert.AreEqual(WritingSkillTag.Article, stats[1].Tag);
        Assert.AreEqual(6, WritingHistoryRules.RecentMistakeCount(history, Now));
    }

    [TestMethod]
    public void PassedReviews_LowerTheWeight()
    {
        var history = WithMistakes(WritingSkillTag.Tense, 3, Now.AddDays(-1));
        history = WithMistakes(WritingSkillTag.Article, 2, Now.AddDays(-1), history);
        for (var i = 0; i < 3; i++)
            history = WritingHistoryRules.RecordFocusResult(history, [WritingSkillTag.Tense], Feedback(), Now);

        var stats = WritingHistoryRules.TagStats(history, Now);
        var tense = stats.Single(stat => stat.Tag == WritingSkillTag.Tense);

        Assert.AreEqual(3, tense.Passed);
        Assert.AreEqual(0, tense.Weight);
        Assert.AreEqual(WritingSkillTag.Article, stats[0].Tag);
        CollectionAssert.AreEqual(new[] { WritingSkillTag.Article }, WritingHistoryRules.WeakTags(history, Now).ToArray());
    }

    [TestMethod]
    public void RecordFocusResult_FailsTagsThatStillHaveMistakes()
    {
        var history = WritingHistoryRules.RecordFocusResult(
            WritingHistory.Empty,
            [WritingSkillTag.Tense, WritingSkillTag.Article],
            Feedback(Issue(WritingIssueType.Grammar, WritingSkillTag.Article)),
            Now);

        Assert.AreEqual(2, history.Reviews.Count);
        Assert.IsTrue(history.Reviews.Single(review => review.Tag == WritingSkillTag.Tense).Passed);
        Assert.IsFalse(history.Reviews.Single(review => review.Tag == WritingSkillTag.Article).Passed);
    }

    [TestMethod]
    public void WeakTags_OnlyTargetableTagsAtMostThree()
    {
        var history = WithMistakes(WritingSkillTag.Spelling, 9, Now);
        history = WithMistakes(WritingSkillTag.Tense, 5, Now, history);
        history = WithMistakes(WritingSkillTag.Article, 4, Now, history);
        history = WithMistakes(WritingSkillTag.Preposition, 3, Now, history);
        history = WithMistakes(WritingSkillTag.WordOrder, 2, Now, history);

        CollectionAssert.AreEqual(
            new[] { WritingSkillTag.Tense, WritingSkillTag.Article, WritingSkillTag.Preposition },
            WritingHistoryRules.WeakTags(history, Now).ToArray());
    }

    [TestMethod]
    public void CanPracticeWeakPoints_NeedsFiveRecentMistakesAndATargetableTag()
    {
        Assert.IsFalse(WritingHistoryRules.CanPracticeWeakPoints(WithMistakes(WritingSkillTag.Tense, 4, Now), Now));
        Assert.IsTrue(WritingHistoryRules.CanPracticeWeakPoints(WithMistakes(WritingSkillTag.Tense, 5, Now), Now));
        Assert.IsFalse(WritingHistoryRules.CanPracticeWeakPoints(WithMistakes(WritingSkillTag.Spelling, 8, Now), Now));
        Assert.IsFalse(WritingHistoryRules.CanPracticeWeakPoints(WithMistakes(WritingSkillTag.Tense, 8, Now.AddDays(-31)), Now));
    }

    [TestMethod]
    public void RecentMistakes_NewestFirst()
    {
        var history = WithMistakes(WritingSkillTag.Article, 1, Now.AddDays(-2));
        history = WithMistakes(WritingSkillTag.Tense, 1, Now, history);

        var recent = WritingHistoryRules.RecentMistakes(history, 5);
        Assert.AreEqual(WritingSkillTag.Tense, recent[0].Tag);
        Assert.AreEqual(2, recent.Count);
    }

    [TestMethod]
    public void Normalize_DropsBrokenEntriesAndClipsText()
    {
        var history = new WritingHistory
        {
            Mistakes =
            [
                null!,
                new WritingMistake(Now, WritingPracticeMode.Translate, WritingIssueType.Grammar, (WritingSkillTag)99, "a", "b", "c", "d", "e", "f"),
                new WritingMistake(Now, WritingPracticeMode.Dictation, WritingIssueType.Spelling, WritingSkillTag.Spelling, new string('x', 900), null!, "c", "d", "e", "f")
            ],
            Reviews = null!
        };

        var normalized = WritingHistoryRules.Normalize(history);

        Assert.AreEqual(1, normalized.Mistakes.Count);
        Assert.AreEqual(WritingHistoryRules.MaxTextLength, normalized.Mistakes[0].Original.Length);
        Assert.AreEqual("", normalized.Mistakes[0].Suggestion);
        Assert.AreEqual(0, normalized.Reviews.Count);
        Assert.AreSame(WritingHistory.Empty, WritingHistoryRules.Normalize(null));
    }

    [TestMethod]
    public void RecordFeedback_SameSentenceCheckedAgainIsNotRecordedTwice()
    {
        var feedback = Feedback(Issue(WritingIssueType.Grammar, WritingSkillTag.Tense, "live"));
        var history = WritingHistoryRules.RecordFeedback(WritingHistory.Empty, WritingPracticeMode.Translate, "Câu A", "I live", feedback, Now);
        history = WritingHistoryRules.RecordFeedback(history, WritingPracticeMode.Translate, "Câu A", "I live here", feedback, Now);
        Assert.AreEqual(1, history.Mistakes.Count);

        history = WritingHistoryRules.RecordFeedback(history, WritingPracticeMode.Translate, "Câu B", "I live", feedback, Now);
        Assert.AreEqual(2, history.Mistakes.Count, "the same mistake in another sentence still counts");
    }

    [TestMethod]
    public void RecordFeedback_SkipsCapitalizationEvenWhenLabelledGrammar()
    {
        var history = WritingHistoryRules.RecordFeedback(
            WritingHistory.Empty, WritingPracticeMode.Translate, "Câu", "i think so",
            Feedback(new WritingIssue(WritingIssueType.Grammar, "i", "I", "Capitalize."), Issue(WritingIssueType.Grammar, WritingSkillTag.Tense, "think")),
            Now);

        Assert.AreEqual(1, history.Mistakes.Count);
        Assert.AreEqual("think", history.Mistakes[0].Original);
    }

    [TestMethod]
    public void RecentMistakeGroups_CountsRepeatsIgnoringCase()
    {
        var history = WritingHistory.Empty;
        history = WritingHistoryRules.RecordFeedback(history, WritingPracticeMode.Translate, "A", "x",
            Feedback(new WritingIssue(WritingIssueType.Grammar, "Go", "went", "past") { Tag = WritingSkillTag.Tense }), Now.AddDays(-1));
        history = WritingHistoryRules.RecordFeedback(history, WritingPracticeMode.Translate, "B", "x",
            Feedback(new WritingIssue(WritingIssueType.Grammar, "go", "went", "past") { Tag = WritingSkillTag.Tense }), Now);
        history = WritingHistoryRules.RecordFeedback(history, WritingPracticeMode.Translate, "C", "x",
            Feedback(new WritingIssue(WritingIssueType.Grammar, "a", "the", "article") { Tag = WritingSkillTag.Article }), Now.AddHours(-1));

        var groups = WritingHistoryRules.RecentMistakeGroups(history, 8);

        Assert.AreEqual(2, groups.Count);
        Assert.AreEqual(2, groups[0].Count);
        Assert.AreEqual("go", groups[0].Latest.Original);
        Assert.AreEqual(1, groups[1].Count);
    }

    [TestMethod]
    public void Normalize_DropsSavedCapitalizationMistakes()
    {
        var history = new WritingHistory
        {
            Mistakes =
            [
                new WritingMistake(Now, WritingPracticeMode.Translate, WritingIssueType.Grammar, WritingSkillTag.Other, "i", "I", "Capitalize I.", "s", "a", "c"),
                new WritingMistake(Now, WritingPracticeMode.Translate, WritingIssueType.Grammar, WritingSkillTag.Tense, "go", "went", "Past.", "s", "a", "c")
            ]
        };

        var normalized = WritingHistoryRules.Normalize(history);

        Assert.AreEqual(1, normalized.Mistakes.Count);
        Assert.AreEqual("go", normalized.Mistakes[0].Original);
    }

    [TestMethod]
    public void FeedbackNormalize_TreatsCaseOnlyFixesAsPunctuation()
    {
        var feedback = WritingFeedbackRules.Normalize(new WritingFeedback(80, WritingVerdict.Almost, "I think so.", null, "", [
            new WritingIssue(WritingIssueType.Grammar, "i", "I", "Capitalize."),
            new WritingIssue(WritingIssueType.Grammar, "so", "so.", "Period.")
        ]));

        Assert.IsTrue(feedback.Issues.All(issue => issue.Type == WritingIssueType.Punctuation));
        Assert.AreEqual(100, feedback.Score, "formatting never lowers the score");
    }

    [TestMethod]
    public void CopyPreferences_CopiesEveryWritingField()
    {
        var source = AppSettings.Default with
        {
            WritingLevel = WritingLevel.Hard,
            WritingExplanationLanguage = WritingExplanationLanguage.Vietnamese,
            WritingPracticeMode = WritingPracticeMode.Dictation,
            WritingRetypeEnabled = false,
            WritingAutoPlayCorrected = true,
            WritingFocusWeakPoints = true,
            SelectedWritingTopicKeys = ["travel"],
            FontSize = 50
        };

        var copied = WritingTopicRules.CopyPreferences(AppSettings.Default, source);

        Assert.AreEqual(WritingLevel.Hard, copied.WritingLevel);
        Assert.AreEqual(WritingExplanationLanguage.Vietnamese, copied.WritingExplanationLanguage);
        Assert.AreEqual(WritingPracticeMode.Dictation, copied.WritingPracticeMode);
        Assert.IsFalse(copied.WritingRetypeEnabled);
        Assert.IsTrue(copied.WritingAutoPlayCorrected);
        Assert.IsTrue(copied.WritingFocusWeakPoints);
        CollectionAssert.AreEqual(new[] { "travel" }, copied.SelectedWritingTopicKeys.ToArray());
        Assert.AreEqual(AppSettings.Default.FontSize, copied.FontSize);
    }

    [TestMethod]
    public void Normalize_ResetsUnknownPracticeMode()
    {
        var settings = WritingTopicRules.Normalize(AppSettings.Default with { WritingPracticeMode = (WritingPracticeMode)7 });
        Assert.AreEqual(WritingPracticeMode.Translate, settings.WritingPracticeMode);
    }

    [TestMethod]
    public void MergeEditableFields_KeepsNewWritingPreferences()
    {
        var submitted = AppSettings.Default with { WritingPracticeMode = WritingPracticeMode.Dictation, WritingRetypeEnabled = false };
        var merged = SettingsUpdateMerger.MergeEditableFields(AppSettings.Default, submitted);

        Assert.AreEqual(WritingPracticeMode.Dictation, merged.WritingPracticeMode);
        Assert.IsFalse(merged.WritingRetypeEnabled);
    }
}
