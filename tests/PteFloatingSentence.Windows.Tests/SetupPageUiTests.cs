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
            Assert.IsFalse(((Button)page.FindName("AddWithAiButton")).IsEnabled, "AI phrasing is for one sentence");

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

    private sealed class FakeSplitter : ISentenceSplitter
    {
        public Task<IReadOnlyList<string>> SplitAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["Stock predictions can be very capricious even for professionals.", "We are looking for new ways."]);
    }
}
