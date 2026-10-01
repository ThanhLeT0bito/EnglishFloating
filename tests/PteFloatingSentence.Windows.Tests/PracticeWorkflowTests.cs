using System.Windows;
using System.Windows.Controls;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
[DoNotParallelize]
public class PracticeWorkflowTests
{
    [TestMethod]
    public void Settings_ReviewPracticePage_PlaysAudio_StopsOnListChange_AndDisposes() => Sta(() =>
    {
        var first = new StudyList(Guid.NewGuid(), "First", 10, 0, [new(Guid.NewGuid(), "First secret sentence.")]);
        var second = new StudyList(Guid.NewGuid(), "Second", 10, 0, [new(Guid.NewGuid(), "Second secret sentence.")]);
        var audio = new FakeAudio();
        var page = new ReviewPracticePage(audio);
        page.Initialize(new AppSettings { ActiveListId = first.Id, StudyLists = [first, second] });

        Click(page, "AudioButton");
        Assert.AreEqual(first.Sentences[0].Text, audio.Text);

        var initialStops = audio.Stops;
        Named<ComboBox>(page, "StudyListSelector").SelectedIndex = 1;
        Assert.IsTrue(audio.Stops > initialStops);

        Click(page, "AudioButton");
        Assert.AreEqual(second.Sentences[0].Text, audio.Text);

        page.Dispose();
        Assert.AreEqual(0, audio.Subscribers);
        Assert.IsTrue(audio.Disposed);
    });

    [TestMethod]
    public void Settings_ReviewPracticePage_NavigationStopsAudio() => Sta(() =>
    {
        var list = new StudyList(Guid.NewGuid(), "Target", 10, 0, [
            new(Guid.NewGuid(), "Alpha sentence."),
            new(Guid.NewGuid(), "Beta sentence.")
        ]);
        var audio = new FakeAudio();
        var page = new ReviewPracticePage(audio);
        page.Initialize(new AppSettings { ActiveListId = list.Id, StudyLists = [list] });

        Click(page, "AudioButton");
        Assert.AreEqual(list.Sentences[0].Text, audio.Text);

        var stops = audio.Stops;
        Click(page, "NextSentenceButton");
        Assert.IsTrue(audio.Stops > stops);

        Click(page, "AudioButton");
        Assert.AreEqual(list.Sentences[1].Text, audio.Text);

        stops = audio.Stops;
        Click(page, "PreviousSentenceButton");
        Assert.IsTrue(audio.Stops > stops);

        page.Dispose();
    });

    [TestMethod]
    public void ReviewPracticeSession_SkipCurrentWord_AdvancesAndCompletes()
    {
        var sentence = new StudySentence(Guid.NewGuid(), "Alpha beta gamma.");
        var list = new StudyList(Guid.NewGuid(), "Test", 10, 0, [sentence]);
        var session = new ReviewPracticeSession(list, seed: 10, mode: PracticeMode.ListenAndWrite);

        Assert.AreEqual(0, session.CurrentHiddenPosition);
        var skip1 = session.SkipCurrentWord();
        Assert.IsFalse(skip1.IsComplete);
        Assert.AreEqual(1, session.CurrentHiddenPosition);

        var skip2 = session.SkipCurrentWord();
        Assert.IsFalse(skip2.IsComplete);
        Assert.AreEqual(2, session.CurrentHiddenPosition);

        var skip3 = session.SkipCurrentWord();
        Assert.IsTrue(skip3.IsComplete);
        Assert.IsTrue(session.IsComplete);
    }

    [TestMethod]
    public void Settings_ReviewPracticePage_SpaceOrEnterSubmitsAnswer_AdvancesWord() => Sta(() =>
    {
        var list = new StudyList(Guid.NewGuid(), "Target", 10, 0, [
            new(Guid.NewGuid(), "Alpha beta.")
        ]);
        var audio = new FakeAudio();
        var page = new ReviewPracticePage(audio);
        page.Initialize(new AppSettings { ActiveListId = list.Id, StudyLists = [list] }, seed: 42);

        var panel = Named<Panel>(page, "SentenceProjectionPanel");
        var activeBox = panel.Children.OfType<TextBox>().First(b => (int)b.Tag == 0);

        var session = (ReviewPracticeSession)typeof(ReviewPracticePage).GetField("_session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(page)!;
        var tokenIdx = session.CurrentReview.HiddenTokenIndexes[0];
        var word = session.CurrentReview.Tokens[tokenIdx].SourceText;

        activeBox.Text = word;
        var spaceArgs = new System.Windows.Input.KeyEventArgs(
            System.Windows.Input.Keyboard.PrimaryDevice,
            new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero),
            0,
            System.Windows.Input.Key.Space)
        {
            RoutedEvent = System.Windows.UIElement.KeyDownEvent,
            Source = activeBox
        };
        activeBox.RaiseEvent(spaceArgs);

        Assert.IsTrue(activeBox.Text.Contains(word));
        Assert.IsFalse(activeBox.IsEnabled);
        page.Dispose();
    });

    private static T Named<T>(FrameworkElement owner, string name) where T : class
    {
        var value = owner.FindName(name) as T;
        Assert.IsNotNull(value, name);
        return value;
    }

    private static void Click(FrameworkElement owner, string name) =>
        Named<Button>(owner, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    private sealed class FakeAudio : ISentenceAudioPlayback
    {
        private Action<AudioPlaybackState>? _changed;
        public int Subscribers => _changed?.GetInvocationList().Length ?? 0;
        public int Stops { get; private set; }
        public string? Text { get; private set; }
        public bool Disposed { get; private set; }
        public AudioPlaybackState State => new(false, false, null);
        public event Action<AudioPlaybackState>? StateChanged
        {
            add => _changed += value;
            remove => _changed -= value;
        }
        public Task ToggleAsync(string text, string voice, double speed)
        {
            Text = text;
            return Task.CompletedTask;
        }
        public void Stop() => Stops++;
        public void Dispose() => Disposed = true;
    }
}
