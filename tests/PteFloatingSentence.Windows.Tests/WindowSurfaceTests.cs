using PteFloatingSentence.Core;
using System.Text.RegularExpressions;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public class WindowSurfaceTests
{
    [TestMethod]
    public void FloatingWindow_ProvidesApplySettingsApi()
    {
        var floatingWindowType = typeof(App).Assembly.GetType("PteFloatingSentence.Windows.FloatingWindow");

        Assert.IsNotNull(floatingWindowType);
        Assert.IsNotNull(floatingWindowType.GetMethod("ApplySettings", [typeof(AppSettings)]));
    }

    [TestMethod]
    public void FloatingWindow_ProvidesPreviousAndNextNavigationEvents()
    {
        var floatingWindowType = typeof(App).Assembly.GetType("PteFloatingSentence.Windows.FloatingWindow");

        Assert.IsNotNull(floatingWindowType);
        Assert.IsNotNull(floatingWindowType.GetEvent("PreviousRequested"));
        Assert.IsNotNull(floatingWindowType.GetEvent("NextRequested"));
    }

    [TestMethod]
    public void FloatingWindow_SizesNavigationButtonsToSentenceCardHeight()
    {
        double btnWidth = 0, btnHeight = 0, cardHeight = 0, cardWidth = 0;
        var thread = new Thread(() =>
        {
            var window = new FloatingWindow();
            window.ApplySettings(AppSettings.Default with
            {
                StudyLists = [new StudyList(Guid.NewGuid(), "List", 10, 0, [new StudySentence(Guid.NewGuid(), "First sentence for test."), new StudySentence(Guid.NewGuid(), "Second sentence.")])]
            });
            window.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            var btn = (System.Windows.Controls.Button)window.FindName("PreviousButton");
            var card = (System.Windows.FrameworkElement)window.FindName("SentenceCard");
            btnWidth = btn.Width;
            btnHeight = btn.Height;
            cardHeight = card.DesiredSize.Height;
            cardWidth = card.DesiredSize.Width;
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.AreEqual(Math.Round(cardHeight), btnWidth);
        Assert.AreEqual(Math.Round(cardHeight), btnHeight);
        Assert.IsTrue(btnWidth is >= 30 and <= 150);
    }

    [TestMethod]
    public void FloatingWindowXaml_UsesHiddenNavigationButtonsAndPolishedSurface()
    {
        var xaml = File.ReadAllText(FindWorkspaceFile("src", "PteFloatingSentence.Windows", "FloatingWindow.xaml"));

        StringAssert.Contains(xaml, "x:Name=\"PreviousButton\"");
        StringAssert.Contains(xaml, "x:Name=\"NextButton\"");
        Assert.AreEqual(2, Regex.Matches(xaml, "FontFamily=\"Segoe MDL2 Assets\"").Count);
        Assert.AreEqual(2, Regex.Matches(xaml, "Visibility=\"Hidden\"").Count);
        StringAssert.Contains(xaml, "Background=\"#01000000\"");
        StringAssert.Contains(xaml, "LinearGradientBrush");
        StringAssert.Contains(xaml, "Header=\"Settings\"");
        StringAssert.Contains(xaml, "Header=\"Exit\"");
        Assert.AreEqual(2, Regex.Matches(xaml, "<MenuItem\\s").Count);
    }

    [TestMethod]
    public void FloatingWindowXaml_PlacesNavigationButtonsOutsideContentWithCircleBorder()
    {
        var xaml = File.ReadAllText(FindWorkspaceFile("src", "PteFloatingSentence.Windows", "FloatingWindow.xaml"));

        var sentenceBgStart = xaml.IndexOf("x:Name=\"SentenceBackground\"", StringComparison.Ordinal);
        var sentenceBgEnd = xaml.IndexOf("</Border>", sentenceBgStart, StringComparison.Ordinal);
        var sentenceBgContent = xaml.Substring(sentenceBgStart, sentenceBgEnd - sentenceBgStart);

        Assert.IsFalse(sentenceBgContent.Contains("PreviousButton"));
        Assert.IsFalse(sentenceBgContent.Contains("NextButton"));
        StringAssert.Contains(xaml, "CircleNavButtonStyle");
        StringAssert.Contains(xaml, "x:Name=\"CircleBorder\"");
        StringAssert.Contains(xaml, "CornerRadius=\"999\"");
        StringAssert.Contains(xaml, "x:Name=\"SentenceCard\"");
        StringAssert.Contains(xaml, "SizeChanged=\"SentenceCard_SizeChanged\"");
    }

    [TestMethod]
    public void DragGuard_UsesVisualTreeAncestryBeforeDragMove()
    {
        var code = File.ReadAllText(FindWorkspaceFile("src", "PteFloatingSentence.Windows", "FloatingWindow.xaml.cs"));
        var handlerStart = code.IndexOf("private void Window_MouseLeftButtonDown", StringComparison.Ordinal);
        var guardIndex = code.IndexOf("IsWithinButtonTree", handlerStart, StringComparison.Ordinal);
        var dragMoveIndex = code.IndexOf("DragMove()", handlerStart, StringComparison.Ordinal);

        StringAssert.Contains(code, "VisualTreeHelper.GetParent");
        Assert.IsTrue(handlerStart >= 0 && guardIndex > handlerStart && guardIndex < dragMoveIndex);
    }

    [TestMethod]
    public void StudyListNavigation_WrapsForwardAndBackwardWithoutChangingCompletion()
    {
        var first = new StudySentence(Guid.NewGuid(), "First.", true);
        var second = new StudySentence(Guid.NewGuid(), "Second.");
        var list = new StudyList(Guid.NewGuid(), "Practice", 10, 0, [first, second]);
        var settings = new AppSettings { ActiveListId = list.Id, StudyLists = [list] };

        var movedBackward = StudyListRules.MoveCurrentSentence(settings, -1);
        var movedForward = StudyListRules.MoveCurrentSentence(movedBackward, 1);
        var backwardActive = StudyListRules.ActiveList(movedBackward);
        var forwardActive = StudyListRules.ActiveList(movedForward);

        Assert.AreEqual(1, backwardActive.CurrentSentenceIndex);
        Assert.AreEqual(0, forwardActive.CurrentSentenceIndex);
        CollectionAssert.AreEqual(new[] { true, false }, backwardActive.Sentences.Select(sentence => sentence.IsCompleted).ToArray());
        CollectionAssert.AreEqual(new[] { true, false }, forwardActive.Sentences.Select(sentence => sentence.IsCompleted).ToArray());
    }

    private static string FindWorkspaceFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
                return candidate;
        }

        Assert.Fail($"Could not locate workspace file: {Path.Combine(segments)}");
        return string.Empty;
    }
}
