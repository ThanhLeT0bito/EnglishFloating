using PteFloatingSentence.Core;
using PteFloatingSentence.Windows;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public class ReviewPracticeSessionTests
{
    [TestMethod]
    public void Session_SelectsFirstIncompleteSentence()
    {
        var s1 = new StudySentence(Guid.NewGuid(), "Sentence one.", IsCompleted: true);
        var s2 = new StudySentence(Guid.NewGuid(), "Sentence two.", IsCompleted: false);
        var s3 = new StudySentence(Guid.NewGuid(), "Sentence three.", IsCompleted: false);
        var list = new StudyList(Guid.NewGuid(), "Test List", 10, 0, [s1, s2, s3]);

        var session = new ReviewPracticeSession(list);

        Assert.AreEqual(1, session.SentenceIndex);
        Assert.AreEqual(s2.Id, session.CurrentReview.SentenceId);
        Assert.IsFalse(session.IsAllSentencesCompleted);
    }

    [TestMethod]
    public void Session_AllSentencesComplete_SelectsFirstSentenceAndFlagsAllCompleted()
    {
        var s1 = new StudySentence(Guid.NewGuid(), "Sentence one.", IsCompleted: true);
        var s2 = new StudySentence(Guid.NewGuid(), "Sentence two.", IsCompleted: true);
        var list = new StudyList(Guid.NewGuid(), "Test List", 10, 0, [s1, s2]);

        var session = new ReviewPracticeSession(list);

        Assert.AreEqual(0, session.SentenceIndex);
        Assert.AreEqual(s1.Id, session.CurrentReview.SentenceId);
        Assert.IsTrue(session.IsAllSentencesCompleted);
    }

    [TestMethod]
    public void Session_EmptyList_HandlesGracefully()
    {
        var list = new StudyList(Guid.NewGuid(), "Empty List", 10, 0, []);

        var session = new ReviewPracticeSession(list);

        Assert.AreEqual(0, session.SentenceIndex);
        Assert.IsTrue(session.IsAllSentencesCompleted);
        Assert.IsTrue(session.IsComplete);
    }

    [TestMethod]
    public void Session_SubmitCorrectAnswer_AdvancesHiddenPosition()
    {
        var s1 = new StudySentence(Guid.NewGuid(), "Alpha beta gamma delta epsilon.");
        var list = new StudyList(Guid.NewGuid(), "Test List", 10, 0, [s1]);

        var session = new ReviewPracticeSession(list, seed: 123);
        Assert.IsTrue(session.CurrentReview.HiddenTokenIndexes.Count >= 2);

        var firstToken = session.CurrentReview.Tokens[session.CurrentReview.HiddenTokenIndexes[0]];
        var rawWord = firstToken.SourceText.Trim('.', ',');

        var result = session.Submit(rawWord);

        Assert.IsTrue(result.IsCorrect);
        Assert.AreEqual(1, session.CurrentHiddenPosition);
    }

    [TestMethod]
    public void Session_SubmitFinalCorrectAnswer_InvokesCompletionCallback()
    {
        var s1 = new StudySentence(Guid.NewGuid(), "Alpha beta gamma.");
        var list = new StudyList(Guid.NewGuid(), "Test List", 10, 0, [s1]);

        Guid? completedSentenceId = null;
        bool? completedValue = null;

        var session = new ReviewPracticeSession(list, seed: 10, onSentenceCompleted: (id, val) =>
        {
            completedSentenceId = id;
            completedValue = val;
        });

        for (var i = 0; i < session.CurrentReview.HiddenTokenIndexes.Count; i++)
        {
            var token = session.CurrentReview.Tokens[session.CurrentReview.HiddenTokenIndexes[i]];
            var word = token.SourceText.Trim('.', ',');
            session.Submit(word);
        }

        Assert.IsTrue(session.IsComplete);
        Assert.AreEqual(s1.Id, completedSentenceId);
        Assert.AreEqual(true, completedValue);
    }

    [TestMethod]
    public void Session_Navigation_ResetsHiddenPosition()
    {
        var s1 = new StudySentence(Guid.NewGuid(), "Alpha beta gamma delta epsilon.");
        var s2 = new StudySentence(Guid.NewGuid(), "One two three four five.");
        var list = new StudyList(Guid.NewGuid(), "Test List", 10, 0, [s1, s2]);

        var session = new ReviewPracticeSession(list, seed: 1);
        session.Submit(session.CurrentReview.Tokens[session.CurrentReview.HiddenTokenIndexes[0]].SourceText.Trim('.', ','));
        Assert.AreEqual(1, session.CurrentHiddenPosition);

        session.MoveNextSentence();
        Assert.AreEqual(1, session.SentenceIndex);
        Assert.AreEqual(0, session.CurrentHiddenPosition);
        Assert.AreEqual(s2.Id, session.CurrentReview.SentenceId);

        session.MovePreviousSentence();
        Assert.AreEqual(0, session.SentenceIndex);
        Assert.AreEqual(0, session.CurrentHiddenPosition);
        Assert.AreEqual(s1.Id, session.CurrentReview.SentenceId);
    }

    [TestMethod]
    public void ViewModel_ProgressAndRevealAnswer_BehavesCorrectly()
    {
        var s1 = new StudySentence(Guid.NewGuid(), "Alpha beta gamma delta.");
        var s2 = new StudySentence(Guid.NewGuid(), "One two three four.");
        var list = new StudyList(Guid.NewGuid(), "Test List", 10, 0, [s1, s2]);

        var session = new ReviewPracticeSession(list, seed: 5);
        var vm = new ReviewPracticeViewModel(session);

        Assert.AreEqual("Sentence 1 of 2", vm.ProgressText);
        Assert.IsFalse(vm.CanSubmit);
        vm.CurrentInput = "Alpha";
        Assert.IsTrue(vm.CanSubmit);

        // Reveal answer fills or shows answer without completing
        var currentExpected = vm.CurrentExpectedWord;
        Assert.IsFalse(string.IsNullOrEmpty(currentExpected));

        vm.RevealCurrentAnswer();
        Assert.AreEqual(currentExpected, vm.RevealedAnswer);
        Assert.IsFalse(vm.IsComplete);
    }

    [TestMethod]
    public void Session_WrongAnswers_DoNotAdvanceAndDoNotMarkCompletion()
    {
        var sentence = new StudySentence(Guid.NewGuid(), "One two three four five.");
        var list = new StudyList(Guid.NewGuid(), "Test List", 10, 0, [sentence]);
        var completed = false;

        var session = new ReviewPracticeSession(list, seed: 10, onSentenceCompleted: (_, _) => completed = true);
        var res = session.Submit("wrong_answer");

        Assert.IsFalse(res.IsCorrect);
        Assert.IsFalse(res.IsComplete);
        Assert.IsFalse(session.IsComplete);
        Assert.AreEqual(0, session.CurrentHiddenPosition);
        Assert.IsFalse(completed);
    }

    [TestMethod]
    public void Session_Reopening_RecreatesSameHiddenWordPattern()
    {
        var sentenceId = Guid.NewGuid();
        var sentence = new StudySentence(sentenceId, "The architectural design requires careful planning and execution.");
        var list = new StudyList(Guid.NewGuid(), "Test List", 10, 0, [sentence]);

        var session1 = new ReviewPracticeSession(list);
        var session2 = new ReviewPracticeSession(list);

        CollectionAssert.AreEqual(
            session1.CurrentReview.HiddenTokenIndexes.ToArray(),
            session2.CurrentReview.HiddenTokenIndexes.ToArray());
    }

    [TestMethod]
    public void Session_ShowVocabularyCardsDisabled_HasNoImpactOnSessionOrAnswerChecking()
    {
        var vocab = new VocabularyItem(Guid.NewGuid(), "Alpha", "alpha", Status: VocabularyStatus.Ready, Meaning: "First");
        var sentence = new StudySentence(Guid.NewGuid(), "Alpha beta gamma.", Vocabulary: [vocab]);
        var list = new StudyList(Guid.NewGuid(), "Test List", 10, 0, [sentence]);

        var settings = AppSettings.Default with
        {
            StudyLists = [list],
            ShowVocabularyCards = false
        };

        var session = new ReviewPracticeSession(settings.StudyLists[0], seed: 42);
        Assert.IsNotNull(session.CurrentReview);
        Assert.IsTrue(session.CurrentReview.HiddenTokenIndexes.Count > 0);

        var firstWord = session.CurrentReview.Tokens[session.CurrentReview.HiddenTokenIndexes[0]].SourceText.Trim('.', ',');
        var result = session.Submit(firstWord);
        Assert.IsTrue(result.IsCorrect);
    }
}
