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

    [TestMethod]
    public void FloatingWindow_MultipleVocabularyItems_RenderWithDistinctColorsAndUnderline()
    {
        var thread = new Thread(() =>
        {
            var window = new FloatingWindow();
            var item1 = new VocabularyItem(Guid.NewGuid(), "first", "first", Status: VocabularyStatus.Ready);
            var item2 = new VocabularyItem(Guid.NewGuid(), "second", "second", Status: VocabularyStatus.Ready);

            var sentence = new StudySentence(
                Guid.NewGuid(),
                "The first and second items.",
                IsCompleted: false,
                Vocabulary: [item1, item2]);

            var list = new StudyList(Guid.NewGuid(), "List", 10, 0, [sentence]);
            window.ApplySettings(AppSettings.Default with
            {
                StudyLists = [list],
                ActiveListId = list.Id
            });

            var doc = (System.Windows.Documents.FlowDocument)window.FindName("SentenceDocument");
            var paragraph = (System.Windows.Documents.Paragraph)doc.Blocks.FirstBlock;
            var spans = paragraph.Inlines.OfType<System.Windows.Documents.Span>().ToList();

            Assert.AreEqual(2, spans.Count);
            Assert.IsNotNull(spans[0].TextDecorations);
            Assert.IsTrue(spans[0].TextDecorations.Count > 0);
            Assert.IsNotNull(spans[1].TextDecorations);
            Assert.IsTrue(spans[1].TextDecorations.Count > 0);

            // Verify the two items have distinct colors
            var brush1 = spans[0].Foreground as System.Windows.Media.SolidColorBrush;
            var brush2 = spans[1].Foreground as System.Windows.Media.SolidColorBrush;
            Assert.IsNotNull(brush1);
            Assert.IsNotNull(brush2);
            Assert.AreNotEqual(brush1.Color, brush2.Color);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [TestMethod]
    public void FloatingWindow_VocabularySpanHover_ChangesForegroundAndRestoresOnLeave()
    {
        var thread = new Thread(() =>
        {
            var window = new FloatingWindow();
            var item = new VocabularyItem(Guid.NewGuid(), "hover-target", "hover-target", Status: VocabularyStatus.Ready);

            var sentence = new StudySentence(
                Guid.NewGuid(),
                "This is a hover-target phrase.",
                IsCompleted: false,
                Vocabulary: [item]);

            var list = new StudyList(Guid.NewGuid(), "List", 10, 0, [sentence]);
            window.ApplySettings(AppSettings.Default with
            {
                StudyLists = [list],
                ActiveListId = list.Id
            });

            var doc = (System.Windows.Documents.FlowDocument)window.FindName("SentenceDocument");
            var paragraph = (System.Windows.Documents.Paragraph)doc.Blocks.FirstBlock;
            var span = paragraph.Inlines.OfType<System.Windows.Documents.Span>().Single();

            var originalBrush = span.Foreground as System.Windows.Media.SolidColorBrush;
            var hoverBrush = span.Resources["HoverBrush"] as System.Windows.Media.SolidColorBrush;
            Assert.IsNotNull(originalBrush);
            Assert.IsNotNull(hoverBrush);
            Assert.AreNotEqual(originalBrush.Color, hoverBrush.Color);

            // Trigger MouseEnter
            span.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
            {
                RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent
            });
            Assert.AreEqual(hoverBrush, span.Foreground);

            // Trigger MouseLeave
            span.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
            {
                RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent
            });
            Assert.AreEqual(originalBrush, span.Foreground);

            // Ensure window closes cleanly without leak
            window.Close();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [TestMethod]
    public void FloatingWindow_HideAndRetryButtons_FireRequestedEvents()
    {
        var thread = new Thread(() =>
        {
            var window = new FloatingWindow();
            var itemId = Guid.NewGuid();
            var sentenceId = Guid.NewGuid();
            var item = new VocabularyItem(itemId, "phrase", "phrase", Status: VocabularyStatus.Failed);

            var sentence = new StudySentence(
                sentenceId,
                "A phrase for testing.",
                IsCompleted: false,
                Vocabulary: [item]);

            var list = new StudyList(Guid.NewGuid(), "List", 10, 0, [sentence]);
            window.ApplySettings(AppSettings.Default with
            {
                StudyLists = [list],
                ActiveListId = list.Id
            });

            (Guid SentenceId, Guid ItemId)? hideEvent = null;
            (Guid SentenceId, Guid ItemId)? retryEvent = null;

            window.HideVocabularyRequested += (_, args) => hideEvent = args;
            window.RetryVocabularyRequested += (_, args) => retryEvent = args;

            // Trigger Hide
            var hideMethod = typeof(FloatingWindow).GetMethod("HideVocabularyButton_Click",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(hideMethod);
            var btn = new System.Windows.Controls.Button { Tag = itemId };
            hideMethod.Invoke(window, [btn, new System.Windows.RoutedEventArgs()]);

            Assert.IsNotNull(hideEvent);
            Assert.AreEqual(sentenceId, hideEvent.Value.SentenceId);
            Assert.AreEqual(itemId, hideEvent.Value.ItemId);

            // Trigger Retry
            var retryMethod = typeof(FloatingWindow).GetMethod("RetryVocabularyButton_Click",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(retryMethod);
            retryMethod.Invoke(window, [btn, new System.Windows.RoutedEventArgs()]);

            Assert.IsNotNull(retryEvent);
            Assert.AreEqual(sentenceId, retryEvent.Value.SentenceId);
            Assert.AreEqual(itemId, retryEvent.Value.ItemId);

            window.Close();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [TestMethod]
    public void FloatingWindow_HandleSelection_DistinguishesExistingAndNewVocabulary()
    {
        var thread = new Thread(() =>
        {
            var window = new FloatingWindow();
            var existingId = Guid.NewGuid();
            var item = new VocabularyItem(existingId, "existing", "existing", Status: VocabularyStatus.Ready);

            var sentence = new StudySentence(
                Guid.NewGuid(),
                "This has existing and new phrases.",
                IsCompleted: false,
                Vocabulary: [item]);

            var list = new StudyList(Guid.NewGuid(), "List", 10, 0, [sentence]);
            window.ApplySettings(AppSettings.Default with
            {
                StudyLists = [list],
                ActiveListId = list.Id
            });

            Guid? clickedId = null;
            string? selectedPhrase = null;

            window.VocabularyClicked += (_, id) => clickedId = id;
            window.VocabularySelected += (_, phrase) => selectedPhrase = phrase;

            var box = (System.Windows.Controls.RichTextBox)window.FindName("SentenceBox");
            var handleSelectionMethod = typeof(FloatingWindow).GetMethod("HandleSelection",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(handleSelectionMethod);

            // 1. Select the word "existing" (which is already a vocabulary item)
            var doc = box.Document;
            var para = (System.Windows.Documents.Paragraph)doc.Blocks.FirstBlock;
            var existingSpan = para.Inlines.OfType<System.Windows.Documents.Span>().Single();
            box.Selection.Select(existingSpan.ContentStart, existingSpan.ContentEnd);

            handleSelectionMethod.Invoke(window, null);

            Assert.AreEqual(existingId, clickedId, "Selecting existing phrase should trigger VocabularyClicked.");
            Assert.IsNull(selectedPhrase, "Selecting existing phrase must NOT trigger VocabularySelected.");

            // 2. Select the new word "new"
            clickedId = null;
            selectedPhrase = null;

            // Find Run containing "new"
            var run = para.Inlines.OfType<System.Windows.Documents.Run>().First(r => r.Text.Contains("new"));
            var text = run.Text;
            var newIndex = text.IndexOf("new", StringComparison.Ordinal);
            var start = run.ContentStart.GetPositionAtOffset(newIndex);
            var end = run.ContentStart.GetPositionAtOffset(newIndex + 3);
            Assert.IsNotNull(start);
            Assert.IsNotNull(end);
            box.Selection.Select(start, end);

            handleSelectionMethod.Invoke(window, null);

            Assert.IsNull(clickedId, "Selecting new phrase should not trigger VocabularyClicked.");
            Assert.AreEqual("new", selectedPhrase, "Selecting new phrase must trigger VocabularySelected.");

            window.Close();
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

