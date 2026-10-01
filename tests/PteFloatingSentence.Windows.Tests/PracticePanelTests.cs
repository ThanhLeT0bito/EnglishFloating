using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
[DoNotParallelize]
public class PracticePanelTests
{
    [DataTestMethod]
    [DataRow("mode")]
    [DataRow("reload")]
    [DataRow("unload")]
    public void QueuedAudioNotification_AfterResetCannotRestoreOldFeedback(string action) => RunOnSta(() =>
    {
        var panel = new PracticePanel();
        var audio = new FakeAudio();
        panel.Load(Session(), new AppSettings(), audio);
        var worker = new Thread(() => audio.Publish(new(false, false, "old error")));
        worker.Start();
        worker.Join();
        if (action == "mode") panel.UpdateMode(PracticeMode.ListenAndWrite);
        else if (action == "reload") panel.Load(Session(), new AppSettings(), audio);
        else panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        // A queued callback reads the source's current state; poison that read without a new event.
        audio.SetStateWithoutNotification(new(false, false, "stale error"));
        var frame = new System.Windows.Threading.DispatcherFrame();
        panel.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
            (Action)(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        Assert.AreEqual(Visibility.Collapsed, Named<TextBlock>(panel, "PracticeFeedbackLabel").Visibility);
        Assert.AreEqual("Play audio", Named<Button>(panel, "PracticeAudioButton").ToolTip);
    });

    [TestMethod]
    public void HintSpaceAndDictationEnter_SubmitAndAdvance_InCompactLayout() => RunOnSta(() =>
    {
        using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Practice input test"));
        var panel = new PracticePanel { Compact = true };
        var session = Session();
        panel.Load(session, new AppSettings(), new FakeAudio());
        var box = Named<WrapPanel>(panel, "PracticeProjectionPanel").Children.OfType<TextBox>().Single();
        box.Text = session.CurrentReview.Tokens[session.CurrentReview.HiddenTokenIndexes[0]].SourceText;
        box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = UIElement.PreviewKeyDownEvent });
        Assert.AreEqual(1, session.SentenceIndex);
        panel.UpdateMode(PracticeMode.ListenAndWrite);
        var input = Named<TextBox>(panel, "DictationInput");
        input.Text = "Delta epsilon zeta.";
        input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = UIElement.PreviewKeyDownEvent });
        Assert.IsTrue(session.IsAllSentencesCompleted);
        Assert.AreEqual(14d, panel.FontSize);
        panel.Compact = false;
        Assert.AreEqual(16d, panel.FontSize);
    });

    [TestMethod]
    public void Modes_RenderHintsOrOneUnrestrictedInput_AndClearPartialEntry() => RunOnSta(() =>
    {
        var panel = new PracticePanel();
        var audio = new FakeAudio();
        var session = Session();
        panel.Load(session, new AppSettings(), audio);
        var projection = Named<WrapPanel>(panel, "PracticeProjectionPanel");
        Assert.IsTrue(projection.Children.OfType<TextBlock>().Any());
        Assert.IsTrue(projection.Children.OfType<TextBox>().Any());
        projection.Children.OfType<TextBox>().First().Text = "partial";
        panel.UpdateMode(PracticeMode.ListenAndWrite);
        Assert.AreEqual(0, projection.Children.Count);
        var input = Named<TextBox>(panel, "DictationInput");
        Assert.AreEqual(Visibility.Visible, input.Visibility);
        Assert.AreEqual(0, input.MaxLength);
        Assert.AreEqual("", input.Text);
        Assert.AreEqual(Visibility.Visible, Named<Button>(panel, "PracticeAudioButton").Visibility);
        input.Text = "partial sentence";
        panel.UpdateMode(PracticeMode.TextHints);
        Assert.AreEqual("", input.Text);
        Assert.AreEqual(0, session.SentenceIndex);
        Assert.AreEqual(3, audio.Stops);
    });

    [TestMethod]
    public void Dictation_WrongDoesNotComplete_CorrectAdvances_ThenCompletes() => RunOnSta(() =>
    {
        var completed = 0;
        var session = Session((_, _) => completed++);
        var panel = new PracticePanel();
        var audio = new FakeAudio();
        panel.Load(session, new AppSettings { PracticeMode = PracticeMode.ListenAndWrite }, audio);
        var changes = 0;
        panel.SentenceChanged += (_, _) => changes++;
        var input = Named<TextBox>(panel, "DictationInput");
        input.Text = "wrong";
        Click(panel, "PracticeCheckButton");
        Assert.AreEqual(0, completed);
        Assert.AreEqual("Try again.", Named<TextBlock>(panel, "PracticeFeedbackLabel").Text);
        input.Text = "Alpha beta gamma.";
        Click(panel, "PracticeCheckButton");
        Assert.AreEqual(1, completed);
        Assert.AreEqual(1, session.SentenceIndex);
        Assert.AreEqual(1, changes);
        Assert.AreEqual("", input.Text);
        input.Text = "Delta epsilon zeta.";
        Click(panel, "PracticeCheckButton");
        Assert.AreEqual(2, completed);
        Assert.AreEqual(Visibility.Visible, Named<Border>(panel, "PracticeCompletionPanel").Visibility);
        Assert.AreEqual(3, audio.Stops);
    });

    [TestMethod]
    public void NavigationRestartReloadAndUnload_StopAudioAndClearInputs() => RunOnSta(() =>
    {
        var panel = new PracticePanel();
        var audio = new FakeAudio();
        panel.Load(Session(), new AppSettings { PracticeMode = PracticeMode.ListenAndWrite }, audio);
        foreach (var name in new[] { "PracticeNextButton", "PracticePreviousButton", "PracticeRestartButton" })
        {
            Named<TextBox>(panel, "DictationInput").Text = "partial";
            Click(panel, name);
            Assert.AreEqual("", Named<TextBox>(panel, "DictationInput").Text);
        }
        panel.Load(Session(), new AppSettings(), audio);
        panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert.AreEqual(6, audio.Stops);
        Assert.AreEqual(0, audio.Subscribers);
    });

    [TestMethod]
    public void Speaker_UsesSavedPreferences_ShowsGenericError_AndEmptyListDisablesIt() => RunOnSta(() =>
    {
        var panel = new PracticePanel();
        var audio = new FakeAudio();
        panel.Load(Session(), new AppSettings { TtsVoice = "voice", TtsSpeed = 1.2 }, audio);
        Click(panel, "PracticeAudioButton");
        Assert.AreEqual(("Alpha beta gamma.", "voice", 1.2), audio.Request);
        audio.Publish(new(false, false, "private endpoint detail"));
        Assert.AreEqual("Audio unavailable. Try again.", Named<TextBlock>(panel, "PracticeFeedbackLabel").Text);
        panel.Load(new ReviewPracticeSession(new StudyList(Guid.NewGuid(), "Empty", 0, 0, [])), new AppSettings(), audio);
        Assert.IsFalse(Named<Button>(panel, "PracticeAudioButton").IsEnabled);
    });

    private static ReviewPracticeSession Session(Action<Guid, bool>? callback = null) => new(
        new StudyList(Guid.NewGuid(), "Practice", 2, 0,
        [new(Guid.NewGuid(), "Alpha beta gamma."), new(Guid.NewGuid(), "Delta epsilon zeta.")]), 42, callback);
    private static T Named<T>(PracticePanel panel, string name) => (T)panel.FindName(name);
    private static void Click(PracticePanel panel, string name) => Named<Button>(panel, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
    private sealed class FakeAudio : ISentenceAudioPlayback
    {
        public AudioPlaybackState State { get; private set; } = new(false, false, null);
        public event Action<AudioPlaybackState>? StateChanged;
        public int Subscribers => StateChanged?.GetInvocationList().Length ?? 0;
        public int Stops { get; private set; }
        public (string, string, double) Request { get; private set; }
        public Task ToggleAsync(string text, string voice, double speed) { Request = (text, voice, speed); return Task.CompletedTask; }
        public void Stop() { Stops++; Publish(new(false, false, null)); }
        public void Publish(AudioPlaybackState state) { State = state; StateChanged?.Invoke(state); }
        public void SetStateWithoutNotification(AudioPlaybackState state) => State = state;
        public void Dispose() { }
    }
}
