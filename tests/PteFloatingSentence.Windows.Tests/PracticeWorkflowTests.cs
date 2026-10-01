using System.Windows;
using System.Windows.Controls;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
[DoNotParallelize]
public class PracticeWorkflowTests
{
    [TestMethod]
    public void Settings_ListAndModeChanges_StopAudio_ClearInput_AndDisposeDetaches() => Sta(() =>
    {
        var first = new StudyList(Guid.NewGuid(), "First", 10, 0, [new(Guid.NewGuid(), "Secret first sentence.")]);
        var second = new StudyList(Guid.NewGuid(), "Second", 10, 0, [new(Guid.NewGuid(), "Second private sentence.")]);
        var audio = new FakeAudio();
        var page = new ReviewPracticePage(audio);
        page.Initialize(new AppSettings { ActiveListId = first.Id, StudyLists = [first, second], PracticeMode = PracticeMode.ListenAndWrite });
        var panel = Named<PracticePanel>(page, "SettingsPracticePanel");
        Click(panel, "PracticeAudioButton");
        Assert.AreEqual(first.Sentences[0].Text, audio.Text);
        Named<TextBox>(panel, "DictationInput").Text = "partial";
        var stops = audio.Stops;
        Named<ComboBox>(page, "StudyListSelector").SelectedIndex = 1;
        Assert.IsTrue(audio.Stops > stops);
        Assert.AreEqual("", Named<TextBox>(panel, "DictationInput").Text);
        Assert.AreEqual(0, Named<WrapPanel>(panel, "PracticeProjectionPanel").Children.Count);
        Click(panel, "PracticeAudioButton");
        Assert.AreEqual(second.Sentences[0].Text, audio.Text);
        stops = audio.Stops;
        Named<ComboBox>(page, "PracticeModeSelector").SelectedIndex = 0;
        Assert.IsTrue(audio.Stops > stops);
        Assert.AreEqual(Visibility.Visible, Named<Button>(panel, "PracticeAudioButton").Visibility);
        page.Dispose();
        Assert.AreEqual(0, audio.Subscribers);
        Assert.IsTrue(audio.Disposed);
    });

    [TestMethod]
    public void BothHosts_UseSharedPanel_SelectedList_ModeAndAutoAdvance() => Sta(() =>
    {
        var active = new StudyList(Guid.NewGuid(), "Active", 10, 0, [new(Guid.NewGuid(), "Other text.")]);
        var target = new StudyList(Guid.NewGuid(), "Target", 10, 0, [new(Guid.NewGuid(), "Alpha beta gamma."), new(Guid.NewGuid(), "Delta epsilon zeta.")]);
        var settings = new AppSettings { ActiveListId = active.Id, StudyLists = [active, target], PracticeMode = PracticeMode.ListenAndWrite };
        var floating = new FloatingWindow();
        floating.ApplySettings(settings);
        floating.StartPractice(target.Id);
        using var page = new ReviewPracticePage();
        page.Initialize(settings);
        Named<ComboBox>(page, "StudyListSelector").SelectedIndex = 1;
        var panels = new[] { Named<PracticePanel>(floating, "FloatingPracticePanel"), Named<PracticePanel>(page, "SettingsPracticePanel") };
        foreach (var panel in panels)
        {
            Assert.AreEqual(0, Named<WrapPanel>(panel, "PracticeProjectionPanel").Children.Count);
            Assert.AreEqual(Visibility.Visible, Named<Button>(panel, "PracticeAudioButton").Visibility);
            Named<TextBox>(panel, "DictationInput").Text = "Alpha beta gamma.";
            Click(panel, "PracticeCheckButton");
            Assert.AreEqual("Sentence 2 of 2", Named<TextBlock>(panel, "PracticeProgressLabel").Text);
        }
        Assert.AreEqual(Visibility.Collapsed, Named<Button>(panels[0], "PracticePreviousButton").Visibility);
        Click(floating, "PreviousButton");
        Assert.AreEqual("Sentence 1 of 2", Named<TextBlock>(panels[0], "PracticeProgressLabel").Text);
        floating.ApplySettings(settings with { PracticeMode = PracticeMode.TextHints });
        Assert.IsTrue(Named<WrapPanel>(panels[0], "PracticeProjectionPanel").Children.Count > 0);
        Assert.AreEqual("Sentence 1 of 2", Named<TextBlock>(panels[0], "PracticeProgressLabel").Text);
        floating.Close();
    });

    [TestMethod]
    public void Settings_ModeDefaultsToHints_PreviewsAndSaves() => Sta(() =>
    {
        var list = new StudyList(Guid.NewGuid(), "Target", 10, 0, [new(Guid.NewGuid(), "Alpha beta gamma.")]);
        AppSettings? saved = null;
        var window = new SettingsWindow(new AppSettings { ActiveListId = list.Id, StudyLists = [list] }, s => saved = s);
        var page = Named<ReviewPracticePage>(window, "ReviewPracticePageControl");
        var selector = Named<ComboBox>(page, "PracticeModeSelector");
        Assert.AreEqual(0, selector.SelectedIndex);
        var panel = Named<PracticePanel>(page, "SettingsPracticePanel");
        Assert.IsTrue(Named<WrapPanel>(panel, "PracticeProjectionPanel").Children.Count > 0);
        selector.SelectedIndex = 1;
        Assert.AreEqual(0, Named<WrapPanel>(panel, "PracticeProjectionPanel").Children.Count);
        typeof(SettingsWindow).GetMethod("SaveButton_Click", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);
        Assert.AreEqual(PracticeMode.ListenAndWrite, saved!.PracticeMode);
        window.Close();
    });

    private static T Named<T>(FrameworkElement owner, string name) where T : class
    {
        var value = owner.FindName(name) as T;
        Assert.IsNotNull(value, name);
        return value;
    }
    private static void Click(FrameworkElement owner, string name) => Named<Button>(owner, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    private sealed class FakeAudio : PteFloatingSentence.Windows.Infrastructure.ISentenceAudioPlayback
    {
        private Action<PteFloatingSentence.Windows.Infrastructure.AudioPlaybackState>? _changed;
        public int Subscribers => _changed?.GetInvocationList().Length ?? 0;
        public int Stops { get; private set; }
        public string? Text { get; private set; }
        public bool Disposed { get; private set; }
        public PteFloatingSentence.Windows.Infrastructure.AudioPlaybackState State => new(false, false, null);
        public event Action<PteFloatingSentence.Windows.Infrastructure.AudioPlaybackState>? StateChanged { add => _changed += value; remove => _changed -= value; }
        public Task ToggleAsync(string text, string voice, double speed) { Text = text; return Task.CompletedTask; }
        public void Stop() => Stops++;
        public void Dispose() => Disposed = true;
    }
}
