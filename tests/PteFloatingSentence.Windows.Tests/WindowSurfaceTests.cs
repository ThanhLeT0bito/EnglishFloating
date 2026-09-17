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

    [TestMethod]
    public void ProjectFiles_HaveNoWebView2Dependency()
    {
        var windowsCsproj = File.ReadAllText(FindWorkspaceFile("src", "PteFloatingSentence.Windows", "PteFloatingSentence.Windows.csproj"));
        StringAssert.DoesNotMatch(windowsCsproj, new Regex("WebView2", RegexOptions.IgnoreCase));
    }

    [TestMethod]
    public void FloatingWindow_ProvidesVocabularyEvents()
    {
        var floatingWindowType = typeof(App).Assembly.GetType("PteFloatingSentence.Windows.FloatingWindow");
        Assert.IsNotNull(floatingWindowType);
        Assert.IsNotNull(floatingWindowType.GetEvent("VocabularySelected"));
        Assert.IsNotNull(floatingWindowType.GetEvent("VocabularyClicked"));
        Assert.IsNotNull(floatingWindowType.GetEvent("HideVocabularyRequested"));
        Assert.IsNotNull(floatingWindowType.GetEvent("RetryVocabularyRequested"));
    }

    [TestMethod]
    public void FloatingWindowXaml_UsesSelectableFlowDocumentAndVocabularyPanel()
    {
        var xaml = File.ReadAllText(FindWorkspaceFile("src", "PteFloatingSentence.Windows", "FloatingWindow.xaml"));

        // Must use FlowDocument or RichTextBox, not a plain non-selectable TextBlock for sentence text
        Assert.IsTrue(xaml.Contains("RichTextBox") || xaml.Contains("FlowDocumentScrollViewer"));
        Assert.IsFalse(xaml.Contains("<TextBlock x:Name=\"SentenceText\""));

        // Must contain VocabularyPanel and buttons for Hide and Retry
        StringAssert.Contains(xaml, "x:Name=\"VocabularyPanel\"");
        StringAssert.Contains(xaml, "Hide");
        StringAssert.Contains(xaml, "Retry");

        // VocabularyPanel must be outside SentenceCard to avoid bloating sentence card
        var cardStart = xaml.IndexOf("x:Name=\"SentenceCard\"", StringComparison.Ordinal);
        var cardEnd = xaml.IndexOf("</Border>", xaml.IndexOf("</Border>", cardStart) + 1, StringComparison.Ordinal);
        var cardContent = xaml.Substring(cardStart, cardEnd - cardStart);
        Assert.IsFalse(cardContent.Contains("VocabularyPanel"), "VocabularyPanel must be outside SentenceCard to keep card compact.");
        StringAssert.Contains(xaml, "Grid.Row=\"1\"");
    }

    [TestMethod]
    public void FloatingWindow_ApplySettings_RendersVocabularyCardsAndHighlights()
    {
        var thread = new Thread(() =>
        {
            var window = new FloatingWindow();
            var vocabItem = new VocabularyItem(
                Id: Guid.NewGuid(),
                Phrase: "practice",
                NormalizedPhrase: "practice",
                Meaning: "To do something repeatedly.",
                Example: "Practice helps progress.",
                PronunciationIpa: "/ˈpræktɪs/",
                Status: VocabularyStatus.Ready,
                IsHidden: false);

            var hiddenItem = new VocabularyItem(
                Id: Guid.NewGuid(),
                Phrase: "hidden",
                NormalizedPhrase: "hidden",
                Status: VocabularyStatus.Ready,
                IsHidden: true);

            var sentence = new StudySentence(
                Guid.NewGuid(),
                "Practice makes progress and stays hidden.",
                IsCompleted: false,
                Vocabulary: [vocabItem, hiddenItem]);

            var list = new StudyList(Guid.NewGuid(), "List", 10, 0, [sentence]);
            window.ApplySettings(AppSettings.Default with
            {
                StudyLists = [list],
                ActiveListId = list.Id
            });

            var panel = (System.Windows.Controls.ItemsControl)window.FindName("VocabularyPanel");
            Assert.AreEqual(System.Windows.Visibility.Visible, panel.Visibility);
            var items = (System.Collections.IEnumerable)panel.ItemsSource;
            var count = 0;
            foreach (var item in items)
            {
                count++;
            }

            // Only 1 item visible (the non-hidden one)
            Assert.AreEqual(1, count);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
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

