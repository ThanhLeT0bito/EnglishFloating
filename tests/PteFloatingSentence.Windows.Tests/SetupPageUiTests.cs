using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;
using ValidationResult = PteFloatingSentence.Core.ValidationResult;

namespace PteFloatingSentence.Windows.Tests;

/// <summary>The Sentences page: pasting many sentences, the AI split and the layout that fills the window.</summary>
[TestClass]
[DoNotParallelize]
public sealed class SetupPageUiTests
{
    [TestMethod]
    public void PastingANumberedList_AddsEverySentence()
    {
        RunOnSta(() =>
        {
            var draft = new StudyListDraft(AppSettings.Default, _ => { });
            var page = new SetupPage();
            ValidationResult? shown = null;
            page.Initialize(draft, result => shown = result);
            page.RefreshFromDraft(draft);
            var before = draft.SelectedList.Sentences.Count;
            var input = (TextBox)page.FindName("SentenceInput");

            input.Text = "1. There was a lot of traffic this morning.\n2. You need to pass the written exam.\n3. Please raise your hand.";
            Assert.AreEqual("＋ Add 3 sentences", ((Button)page.FindName("AddSentenceButton")).Content);
            Assert.AreEqual("✨ AI phrased (3)", ((Button)page.FindName("AddWithAiButton")).Content);

            Click(page, "AddSentenceButton");

            Assert.AreEqual(before + 3, draft.SelectedList.Sentences.Count);
            Assert.AreEqual("Please raise your hand.", draft.SelectedList.Sentences[^1].Text);
            Assert.AreEqual(string.Empty, input.Text);
            Assert.AreEqual("Added 3 sentences.", ((TextBlock)page.FindName("AddStatusText")).Text);
            Assert.IsTrue(shown!.Value.IsValid);
            Assert.AreEqual(before + 3, ((ListBox)page.FindName("SentenceList")).Items.Count);
        });
    }

    [TestMethod]
    public void ASentenceThatCannotBeAdded_StaysInTheBox()
    {
        RunOnSta(() =>
        {
            var draft = new StudyListDraft(AppSettings.Default, _ => { });
            var page = new SetupPage();
            page.Initialize(draft, _ => { });
            page.RefreshFromDraft(draft);
            var tooLong = string.Join(' ', Enumerable.Range(1, 22).Select(i => $"w{i}")) + ".";
            var input = (TextBox)page.FindName("SentenceInput");

            input.Text = "A short one. " + tooLong;
            Click(page, "AddSentenceButton");

            Assert.AreEqual(tooLong, input.Text);
            StringAssert.Contains(((TextBlock)page.FindName("AddStatusText")).Text, "20 words");
        });
    }

    [TestMethod]
    public void AiSplit_PutsOneSentencePerLineToCheckBeforeAdding()
    {
        RunOnSta(() =>
        {
            var draft = new StudyListDraft(AppSettings.Default, _ => { });
            var page = new SetupPage { AiSplitter = new FakeSplitter() };
            page.Initialize(draft, _ => { });
            page.RefreshFromDraft(draft);
            var input = (TextBox)page.FindName("SentenceInput");
            input.Text = "stock predictions can be very capri-\ncious even for professionals we are looking for new ways";

            page.SplitWithAiAsync().GetAwaiter().GetResult();

            Assert.AreEqual($"Stock predictions can be very capricious even for professionals.{Environment.NewLine}We are looking for new ways.", input.Text);
            StringAssert.Contains(((TextBlock)page.FindName("AddStatusText")).Text, "AI found 2 sentences");
            Assert.AreEqual("＋ Add 2 sentences", ((Button)page.FindName("AddSentenceButton")).Content);
        });
    }

    [TestMethod]
    public void AiPhrased_AfterAiSplit_PhrasesAndAddsEverySentence()
    {
        RunOnSta(() =>
        {
            var draft = new StudyListDraft(AppSettings.Default, _ => { });
            var page = new SetupPage { SentencePhraser = new FakePhraser() };
            page.Initialize(draft, _ => { });
            page.RefreshFromDraft(draft);
            var before = draft.SelectedList.Sentences.Count;
            var index = draft.SelectedList.CurrentSentenceIndex;
            var input = (TextBox)page.FindName("SentenceInput");
            input.Text = "We are looking for new ways to engage learners.\nPlease raise your hand.\nThis one fails.";

            page.PhraseAndAddAllAsync(PteFloatingSentence.Core.SentenceSplitter.Split(input.Text)).GetAwaiter().GetResult();

            var sentences = draft.SelectedList.Sentences;
            Assert.AreEqual(before + 2, sentences.Count);
            Assert.AreEqual("We are looking for new ways to engage learners.", sentences[before].Text);
            Assert.IsTrue(sentences[before].PhraseBreakAfterWordIndices.Count > 0, "added with its reading groups");
            Assert.AreEqual(index, draft.SelectedList.CurrentSentenceIndex, "the widget's sentence stays");
            Assert.AreEqual("This one fails.", input.Text, "a sentence the AI could not phrase stays in the box");
            StringAssert.StartsWith(((TextBlock)page.FindName("AddStatusText")).Text, "AI phrased and added 2 sentences");
        });
    }

    [TestMethod]
    public void SettingsWindow_SentencesPageFillsTheWindowInsteadOfScrolling()
    {
        RunOnSta(() =>
        {
            var window = new SettingsWindow(AppSettings.Default, _ => { });
            window.NavigateTo(SettingsPageId.Setup);
            Assert.AreEqual(ScrollBarVisibility.Disabled, ((ScrollViewer)window.FindName("PageScrollViewer")).VerticalScrollBarVisibility);

            window.NavigateTo(SettingsPageId.Writing);
            Assert.AreEqual(ScrollBarVisibility.Auto, ((ScrollViewer)window.FindName("PageScrollViewer")).VerticalScrollBarVisibility);
            window.Close();
        });
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

    private sealed class FakePhraser : ISentencePhraser
    {
        public Task<IReadOnlyList<string>> SuggestAsync(string sentence, CancellationToken cancellationToken = default)
        {
            if (sentence.Contains("fails", StringComparison.Ordinal))
                throw new InvalidOperationException("The AI returned an invalid sentence proposal. Please try again.");
            var words = sentence.Split(' ');
            var half = Math.Max(1, words.Length / 2);
            return Task.FromResult<IReadOnlyList<string>>([string.Join(' ', words.Take(half)), string.Join(' ', words.Skip(half))]);
        }
    }

    private sealed class FakeSplitter : ISentenceSplitter
    {
        public Task<IReadOnlyList<string>> SplitAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["Stock predictions can be very capricious even for professionals.", "We are looking for new ways."]);
    }
}
