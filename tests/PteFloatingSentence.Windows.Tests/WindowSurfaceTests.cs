using PteFloatingSentence.Core;
using System.Text.RegularExpressions;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
[DoNotParallelize]
public class WindowSurfaceTests
{
    [TestMethod]
    public void RenderSignature_IsStableForEquivalentSentenceState()
    {
        var first = RenderSignature.Create("Practice this sentence.", 30, "#FFFFFFFF", 0.35, []);
        var second = RenderSignature.Create("Practice this sentence.", 30, "#FFFFFFFF", 0.35, []);

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void RenderSignature_ChangesWhenVocabularyStateChanges()
    {
        var item = new VocabularyItem(Guid.NewGuid(), "Practice", "practice", Status: VocabularyStatus.Pending);
        var first = RenderSignature.Create("Practice this sentence.", 30, "#FFFFFFFF", 0.35, [item]);
        var second = RenderSignature.Create("Practice this sentence.", 30, "#FFFFFFFF", 0.35,
            [item with { Status = VocabularyStatus.Ready, Meaning = "To repeat." }]);

        Assert.AreNotEqual(first, second);
    }

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
        StringAssert.Contains(xaml, "x:Name=\"PracticeMenuItem\"");
        Assert.AreEqual(3, Regex.Matches(xaml, "<MenuItem\\s").Count);
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
        Assert.IsNotNull(floatingWindowType.GetEvent("DeleteVocabularyRequested"));
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
        StringAssert.Contains(xaml, "Delete");

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
    public void FloatingWindow_VocabularySpanQueryCursor_SetsHandCursorAndHandlesEvent()
    {
        var thread = new Thread(() =>
        {
            var window = new FloatingWindow();
            var item = new VocabularyItem(Guid.NewGuid(), "cursor-test", "cursor-test", Status: VocabularyStatus.Ready);

            var sentence = new StudySentence(
                Guid.NewGuid(),
                "Testing cursor-test phrase.",
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

            // Query cursor on Span
            var queryCursorArgs = new System.Windows.Input.QueryCursorEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0)
            {
                RoutedEvent = System.Windows.Input.Mouse.QueryCursorEvent
            };

            span.RaiseEvent(queryCursorArgs);

            Assert.IsTrue(queryCursorArgs.Handled, "QueryCursor must be marked Handled so RichTextBox cannot override it.");
            Assert.AreEqual(System.Windows.Input.Cursors.Hand, queryCursorArgs.Cursor, "Cursor over vocabulary phrase must remain Hand cursor.");

            // Also test Run inside Span
            var run = span.Inlines.OfType<System.Windows.Documents.Run>().Single();
            var runQueryArgs = new System.Windows.Input.QueryCursorEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0)
            {
                RoutedEvent = System.Windows.Input.Mouse.QueryCursorEvent
            };

            run.RaiseEvent(runQueryArgs);

            Assert.IsTrue(runQueryArgs.Handled, "QueryCursor on child Run must be marked Handled.");
            Assert.AreEqual(System.Windows.Input.Cursors.Hand, runQueryArgs.Cursor, "Cursor over vocabulary run must be Hand cursor.");

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
            Assert.IsTrue(box.Selection.IsEmpty, "Selection should be collapsed after clicking existing vocabulary.");

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

    [TestMethod]
    public void FloatingWindow_HandleSelection_PreservesMultiWordSelectionOverlappingVocabulary()
    {
        var thread = new Thread(() =>
        {
            var window = new FloatingWindow();
            var existingId = Guid.NewGuid();
            var item = new VocabularyItem(existingId, "toys", "toys", Status: VocabularyStatus.Ready);
            var sentence = new StudySentence(Guid.NewGuid(), "This is about small toys.", false, Vocabulary: [item]);
            var list = new StudyList(Guid.NewGuid(), "List", 10, 0, [sentence]);
            window.ApplySettings(AppSettings.Default with { StudyLists = [list], ActiveListId = list.Id });

            string? selectedPhrase = null;
            window.VocabularySelected += (_, phrase) => selectedPhrase = phrase;

            var box = (System.Windows.Controls.RichTextBox)window.FindName("SentenceBox");
            var run = box.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single()
                .Inlines.OfType<System.Windows.Documents.Run>().First(r => r.Text.Contains("small "));
            var start = run.ContentStart.GetPositionAtOffset(run.Text.IndexOf("small", StringComparison.Ordinal));
            var toysSpan = box.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single()
                .Inlines.OfType<System.Windows.Documents.Span>().Single();
            box.Selection.Select(start!, toysSpan.ContentEnd);

            typeof(FloatingWindow).GetMethod("HandleSelection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(window, null);

            Assert.AreEqual("small toys", selectedPhrase);
            window.Close();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [TestMethod]
    public void DisplayPageXaml_ContainsDisplaySectionAndControls()
    {
        var xaml = File.ReadAllText(FindWorkspaceFile("src", "PteFloatingSentence.Windows", "DisplayPage.xaml"));

        StringAssert.Contains(xaml, "x:Name=\"ShowSentenceOverlayInput\"");
        StringAssert.Contains(xaml, "x:Name=\"ShowVocabularyCardsInput\"");
        StringAssert.Contains(xaml, "Overlay visibility");
    }

    [TestMethod]
    public void OverlayLauncherWindowXaml_IsAlwaysOnTopAndHasActionControl()
    {
        var xaml = File.ReadAllText(FindWorkspaceFile("src", "PteFloatingSentence.Windows", "OverlayLauncherWindow.xaml"));

        StringAssert.Contains(xaml, "Topmost=\"True\"");
        StringAssert.Contains(xaml, "WindowStyle=\"None\"");
        StringAssert.Contains(xaml, "AllowsTransparency=\"True\"");
        StringAssert.Contains(xaml, "x:Name=\"LauncherActionButton\"");
        StringAssert.Contains(xaml, "FontFamily=\"Segoe MDL2 Assets\"");
    }

    [TestMethod]
    public void ReviewPageXaml_ContainsReviewSectionAndBoundary()
    {
        var xaml = File.ReadAllText(FindWorkspaceFile("src", "PteFloatingSentence.Windows", "ReviewPage.xaml"));

        StringAssert.Contains(xaml, "Review");
        StringAssert.Contains(xaml, "Quiz coming next");
    }

    [TestMethod]
    public void SettingsWindowXaml_ContainsSettingsCenterShellElements()
    {
        var xaml = File.ReadAllText(FindWorkspaceFile("src", "PteFloatingSentence.Windows", "SettingsWindow.xaml"));

        StringAssert.Contains(xaml, "x:Name=\"SettingsNavigation\"");
        StringAssert.Contains(xaml, "x:Name=\"PageTitle\"");
        StringAssert.Contains(xaml, "x:Name=\"PageSubtitle\"");
        StringAssert.Contains(xaml, "x:Name=\"PageContentHost\"");
        StringAssert.Contains(xaml, "x:Name=\"SaveButton\"");
        StringAssert.Contains(xaml, "x:Name=\"CancelButton\"");
        StringAssert.Contains(xaml, "Setup");
        StringAssert.Contains(xaml, "Display");
        StringAssert.Contains(xaml, "Review");
        StringAssert.Contains(xaml, "Gemini");
    }

    [TestMethod]
    public void SettingsWindow_LoadsAtMinimumSizeWithoutCrashing()
    {
        var thread = new Thread(() =>
        {
            var window = new SettingsWindow(AppSettings.Default, _ => { });
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            window.Measure(new System.Windows.Size(window.MinWidth, window.MinHeight));
            window.Arrange(new System.Windows.Rect(0, 0, window.MinWidth, window.MinHeight));
            Assert.AreEqual(SettingsPageId.Setup, window.SelectedPage);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [TestMethod]
    public void SetupPage_ExposesListEditorControlsAndUpdatesSentenceEditorOnSelection()
    {
        var thread = new Thread(() =>
        {
            var list = new StudyList(Guid.NewGuid(), "Sample List", 10, 0,
                [new StudySentence(Guid.NewGuid(), "First item."), new StudySentence(Guid.NewGuid(), "Second item.")]);
            var settings = new AppSettings { ActiveListId = list.Id, StudyLists = [list] };
            var draft = new StudyListDraft(settings, _ => { });

            var page = new SetupPage();
            page.Initialize(draft, _ => { });
            page.RefreshFromDraft(draft);

            var listNameInput = (System.Windows.Controls.TextBox)page.FindName("ListNameInput");
            var targetInput = (System.Windows.Controls.TextBox)page.FindName("TargetInput");
            var sentenceList = (System.Windows.Controls.ListBox)page.FindName("SentenceList");
            var sentenceInput = (System.Windows.Controls.TextBox)page.FindName("SentenceInput");

            Assert.IsNotNull(listNameInput);
            Assert.IsNotNull(targetInput);
            Assert.IsNotNull(sentenceList);
            Assert.IsNotNull(sentenceInput);
            Assert.AreEqual("Sample List", listNameInput.Text);

            sentenceList.SelectedIndex = 1;
            Assert.AreEqual("Second item.", sentenceInput.Text);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [TestMethod]
    public void ReviewPage_ShowsEmptyStateWhenNoData_AndPopulatesListWhenDataPresent()
    {
        var thread = new Thread(() =>
        {
            var page = new ReviewPage();
            page.LoadData(new ReviewViewModel(new AppSettings { StudyLists = [] }));

            var emptyLabel = (System.Windows.Controls.TextBlock)page.FindName("ReviewEmptyStateLabel");
            var reviewList = (System.Windows.Controls.ItemsControl)page.FindName("ReviewList");

            Assert.IsNotNull(emptyLabel);
            Assert.IsNotNull(reviewList);
            Assert.AreEqual(System.Windows.Visibility.Visible, emptyLabel.Visibility);
            Assert.AreEqual(System.Windows.Visibility.Collapsed, reviewList.Visibility);

            var list = new StudyList(Guid.NewGuid(), "List 1", 10, 0, [new StudySentence(Guid.NewGuid(), "Sentence.")]);
            page.LoadData(new ReviewViewModel(new AppSettings { StudyLists = [list] }));

            Assert.AreEqual(System.Windows.Visibility.Collapsed, emptyLabel.Visibility);
            Assert.AreEqual(System.Windows.Visibility.Visible, reviewList.Visibility);
            Assert.IsNotNull(reviewList.ItemsSource);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [TestMethod]
    public void GeminiPage_ExposesApiKeyControlsAndCallbacks()
    {
        var thread = new Thread(() =>
        {
            var page = new GeminiPage();
            var apiKeyInput = (System.Windows.Controls.PasswordBox)page.FindName("ApiKeyInput");
            var statusLabel = (System.Windows.Controls.TextBlock)page.FindName("ApiKeyStatusLabel");
            var clearButton = (System.Windows.Controls.Button)page.FindName("ClearApiKeyButton");

            Assert.IsNotNull(apiKeyInput);
            Assert.IsNotNull(statusLabel);
            Assert.IsNotNull(clearButton);

            page.LoadState(isKeyConfigured: true);
            Assert.AreEqual("Key configured", statusLabel.Text);
            Assert.IsTrue(clearButton.IsEnabled);

            var cleared = false;
            Action clearHandler = () => cleared = true;
            page.ClearKeyRequested += clearHandler;
            try
            {
                page.ClearApiKey();
                Assert.IsTrue(cleared);
                Assert.AreEqual("No key configured", statusLabel.Text);
                Assert.IsFalse(clearButton.IsEnabled);
            }
            finally
            {
                page.ClearKeyRequested -= clearHandler;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [TestMethod]
    public void SettingsWindow_SetupAndReviewPages_AreScrollableInsideScrollViewerAtMinimumSize()
    {
        var thread = new Thread(() =>
        {
            var window = new SettingsWindow(AppSettings.Default, _ => { });
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            window.Measure(new System.Windows.Size(window.MinWidth, window.MinHeight));
            window.Arrange(new System.Windows.Rect(0, 0, window.MinWidth, window.MinHeight));

            var scrollViewer = FindDescendant<System.Windows.Controls.ScrollViewer>(window);
            Assert.IsNotNull(scrollViewer);
            Assert.AreEqual(System.Windows.Controls.ScrollBarVisibility.Auto, scrollViewer.VerticalScrollBarVisibility);

            window.NavigateTo(SettingsPageId.Setup);
            window.Measure(new System.Windows.Size(window.MinWidth, window.MinHeight));

            window.NavigateTo(SettingsPageId.Review);
            window.Measure(new System.Windows.Size(window.MinWidth, window.MinHeight));

            window.NavigateTo(SettingsPageId.ReviewPractice);
            window.Measure(new System.Windows.Size(window.MinWidth, window.MinHeight));
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [TestMethod]
    public void ReviewPracticePage_SurfaceControlsAndPlaceholders_RenderCorrectly()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var page = new ReviewPracticePage();
                var sentence = new StudySentence(Guid.NewGuid(), "You must wear a hard hat on the construction site");
                var list = new StudyList(Guid.NewGuid(), "Practice List", 10, 0, [sentence]);
                var settings = AppSettings.Default with
                {
                    StudyLists = [list],
                    ActiveListId = list.Id
                };

                page.Initialize(settings, seed: 7);

                var selector = (System.Windows.Controls.ComboBox)page.FindName("StudyListSelector");
                var progressLabel = (System.Windows.Controls.TextBlock)page.FindName("ProgressLabel");
                var projectionPanel = (System.Windows.Controls.WrapPanel)page.FindName("SentenceProjectionPanel");
                var checkBtn = (System.Windows.Controls.Button)page.FindName("CheckAnswerButton");
                var showBtn = (System.Windows.Controls.Button)page.FindName("ShowAnswerButton");
                var prevBtn = (System.Windows.Controls.Button)page.FindName("PreviousSentenceButton");
                var nextBtn = (System.Windows.Controls.Button)page.FindName("NextSentenceButton");
                var completionPanel = (System.Windows.Controls.Border)page.FindName("CompletionPanel");

                Assert.IsNotNull(selector);
                Assert.IsNotNull(progressLabel);
                Assert.IsNotNull(projectionPanel);
                Assert.IsNotNull(checkBtn);
                Assert.IsNotNull(showBtn);
                Assert.IsNotNull(prevBtn);
                Assert.IsNotNull(nextBtn);
                Assert.IsNotNull(completionPanel);

                Assert.AreEqual("Sentence 1 of 1", progressLabel.Text);
                Assert.AreEqual(System.Windows.Visibility.Collapsed, completionPanel.Visibility);

                var review = ReviewPracticeRules.CreateProjection(sentence, seed: 7);
                Assert.AreEqual(review.Tokens.Count, projectionPanel.Children.Count);

                var textBoxes = projectionPanel.Children.OfType<System.Windows.Controls.TextBox>().ToList();
                Assert.AreEqual(review.HiddenTokenIndexes.Count, textBoxes.Count);

                page.Dispose();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(threadEx).Throw();
        }
    }

    [TestMethod]
    public void ReviewPracticePage_AllCompleteState_ShowsCompletionPanel()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var page = new ReviewPracticePage();
                var sentence = new StudySentence(Guid.NewGuid(), "You must wear a hard hat on the construction site", IsCompleted: true);
                var list = new StudyList(Guid.NewGuid(), "Completed List", 10, 0, [sentence]);
                var settings = AppSettings.Default with
                {
                    StudyLists = [list],
                    ActiveListId = list.Id
                };

                page.Initialize(settings, seed: 7);

                var completionPanel = (System.Windows.Controls.Border)page.FindName("CompletionPanel");
                var restartBtn = (System.Windows.Controls.Button)page.FindName("RestartListButton");

                Assert.IsNotNull(completionPanel);
                Assert.IsNotNull(restartBtn);
                Assert.AreEqual(System.Windows.Visibility.Visible, completionPanel.Visibility);

                page.Dispose();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(threadEx).Throw();
        }
    }

    [TestMethod]
    public void FloatingWindow_StartPractice_SwitchesToPracticeModeAndUpdatesUI()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new FloatingWindow();
                var sentence = new StudySentence(Guid.NewGuid(), "You must wear a hard hat on the construction site");
                var list = new StudyList(Guid.NewGuid(), "Practice List", 10, 0, [sentence]);
                var settings = AppSettings.Default with
                {
                    StudyLists = [list],
                    ActiveListId = list.Id,
                    ShowVocabularyCards = true
                };

                window.ApplySettings(settings);

                Assert.IsFalse(window.IsPracticeMode);
                var sentenceBox = (System.Windows.Controls.RichTextBox)window.FindName("SentenceBox");
                var practiceContainer = (System.Windows.FrameworkElement)window.FindName("PracticeContainer");
                var practiceItem = (System.Windows.Controls.MenuItem)window.FindName("PracticeMenuItem");

                Assert.AreEqual(System.Windows.Visibility.Visible, sentenceBox.Visibility);
                Assert.AreEqual(System.Windows.Visibility.Collapsed, practiceContainer.Visibility);
                Assert.AreEqual("Start Practice", practiceItem.Header);

                window.StartPractice();

                Assert.IsTrue(window.IsPracticeMode);
                Assert.AreEqual(System.Windows.Visibility.Collapsed, sentenceBox.Visibility);
                Assert.AreEqual(System.Windows.Visibility.Visible, practiceContainer.Visibility);
                Assert.AreEqual("Exit Practice", practiceItem.Header);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(threadEx).Throw();
        }
    }

    [TestMethod]
    public void FloatingWindow_ExitPractice_RestoresNormalMode()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new FloatingWindow();
                var sentence = new StudySentence(Guid.NewGuid(), "You must wear a hard hat on the construction site");
                var list = new StudyList(Guid.NewGuid(), "Practice List", 10, 0, [sentence]);
                var settings = AppSettings.Default with
                {
                    StudyLists = [list],
                    ActiveListId = list.Id
                };

                window.ApplySettings(settings);
                window.StartPractice();
                Assert.IsTrue(window.IsPracticeMode);

                window.ExitPractice();
                Assert.IsFalse(window.IsPracticeMode);

                var sentenceBox = (System.Windows.Controls.RichTextBox)window.FindName("SentenceBox");
                var practiceContainer = (System.Windows.FrameworkElement)window.FindName("PracticeContainer");
                var practiceItem = (System.Windows.Controls.MenuItem)window.FindName("PracticeMenuItem");

                Assert.AreEqual(System.Windows.Visibility.Visible, sentenceBox.Visibility);
                Assert.AreEqual(System.Windows.Visibility.Collapsed, practiceContainer.Visibility);
                Assert.AreEqual("Start Practice", practiceItem.Header);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(threadEx).Throw();
        }
    }

    [TestMethod]
    public void FloatingWindow_StartPractice_RendersTokensAsTextBlocksAndTextBoxes()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new FloatingWindow();
                var sentence = new StudySentence(Guid.NewGuid(), "You must wear a hard hat on the construction site");
                var list = new StudyList(Guid.NewGuid(), "Practice List", 10, 0, [sentence]);
                var settings = AppSettings.Default with
                {
                    StudyLists = [list],
                    ActiveListId = list.Id
                };

                window.ApplySettings(settings);
                window.StartPractice();

                var projectionPanel = (System.Windows.Controls.WrapPanel)window.FindName("PracticeProjectionPanel");
                var projection = ReviewPracticeRules.CreateProjection(sentence);

                Assert.AreEqual(projection.Tokens.Count, projectionPanel.Children.Count);

                var textBlockCount = projectionPanel.Children.OfType<System.Windows.Controls.TextBlock>().Count();
                var textBoxCount = projectionPanel.Children.OfType<System.Windows.Controls.TextBox>().Count();

                var expectedHiddenCount = projection.HiddenTokenIndexes.Count;
                var expectedVisibleCount = projection.Tokens.Count - expectedHiddenCount;

                Assert.AreEqual(expectedVisibleCount, textBlockCount);
                Assert.AreEqual(expectedHiddenCount, textBoxCount);

                // First box received focus automatically, so its placeholder is cleared
                var boxes = projectionPanel.Children.OfType<System.Windows.Controls.TextBox>().ToList();
                Assert.AreEqual(string.Empty, boxes[0].Text);
                for (var b = 1; b < boxes.Count; b++)
                {
                    Assert.AreEqual("_", boxes[b].Text);
                }
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(threadEx).Throw();
        }
    }

    [TestMethod]
    public void FloatingWindow_PracticeInput_ValidatesAndAdvancesFocus()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new FloatingWindow();
                var sentence = new StudySentence(Guid.NewGuid(), "You must wear a hard hat on the construction site");
                var list = new StudyList(Guid.NewGuid(), "Practice List", 10, 0, [sentence]);
                var settings = AppSettings.Default with
                {
                    StudyLists = [list],
                    ActiveListId = list.Id
                };

                window.ApplySettings(settings);
                window.StartPractice();

                var projectionPanel = (System.Windows.Controls.WrapPanel)window.FindName("PracticeProjectionPanel");
                var feedbackLabel = (System.Windows.Controls.TextBlock)window.FindName("PracticeFeedbackLabel");
                var projection = ReviewPracticeRules.CreateProjection(sentence);

                var firstTextBox = projectionPanel.Children.OfType<System.Windows.Controls.TextBox>().First();
                var firstHiddenIndex = projection.HiddenTokenIndexes[0];
                var expectedWord = projection.Tokens[firstHiddenIndex].SourceText.Trim('.', ',', '!', '?');

                // Test wrong answer
                firstTextBox.Text = "incorrectword";
                var wrongResult = window.SubmitPracticeAnswer(firstTextBox, firstTextBox.Text);

                Assert.IsNotNull(wrongResult);
                Assert.IsFalse(wrongResult.IsCorrect);
                Assert.AreEqual(System.Windows.Visibility.Visible, feedbackLabel.Visibility);
                Assert.IsTrue(feedbackLabel.Text.Contains("Try again"));

                // Test correct answer
                firstTextBox.Text = expectedWord;
                var correctResult = window.SubmitPracticeAnswer(firstTextBox, firstTextBox.Text);

                Assert.IsNotNull(correctResult);
                Assert.IsTrue(correctResult.IsCorrect);
                Assert.AreEqual(System.Windows.Visibility.Collapsed, feedbackLabel.Visibility);
                Assert.IsTrue(firstTextBox.IsReadOnly);
                Assert.AreEqual(expectedWord, firstTextBox.Text);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(threadEx).Throw();
        }
    }

    [TestMethod]
    public void FloatingWindow_CompletingAllHiddenTokens_FiresSentenceCompletedAndTransitions()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new FloatingWindow();
                var s1 = new StudySentence(Guid.NewGuid(), "You must wear a hard hat");
                var s2 = new StudySentence(Guid.NewGuid(), "The project requires careful safety inspection");
                var list = new StudyList(Guid.NewGuid(), "Practice List", 10, 0, [s1, s2]);
                var settings = AppSettings.Default with
                {
                    StudyLists = [list],
                    ActiveListId = list.Id
                };

                window.ApplySettings(settings);

                Guid? reportedListId = null;
                Guid? reportedSentenceId = null;
                bool? reportedCompleted = null;

                window.SentenceCompleted += (_, args) =>
                {
                    reportedListId = args.ListId;
                    reportedSentenceId = args.SentenceId;
                    reportedCompleted = args.Completed;
                };

                window.StartPractice();

                var projection = ReviewPracticeRules.CreateProjection(s1);
                var projectionPanel = (System.Windows.Controls.WrapPanel)window.FindName("PracticeProjectionPanel");
                var progressLabel = (System.Windows.Controls.TextBlock)window.FindName("PracticeProgressLabel");

                Assert.IsTrue(progressLabel.Text.Contains("Sentence 1 of 2"));

                // Answer all hidden tokens for sentence 1
                for (var h = 0; h < projection.HiddenTokenIndexes.Count; h++)
                {
                    var tokenIndex = projection.HiddenTokenIndexes[h];
                    var expectedWord = projection.Tokens[tokenIndex].SourceText.Trim('.', ',', '!', '?');

                    var boxes = projectionPanel.Children.OfType<System.Windows.Controls.TextBox>().ToList();
                    var activeBox = boxes.First(b => b.Tag is int p && p == h);
                    activeBox.Text = expectedWord;
                    window.SubmitPracticeAnswer(activeBox, expectedWord);
                }

                Assert.AreEqual(list.Id, reportedListId);
                Assert.AreEqual(s1.Id, reportedSentenceId);
                Assert.AreEqual(true, reportedCompleted);

                // Should now have transitioned to sentence 2
                Assert.IsTrue(progressLabel.Text.Contains("Sentence 2 of 2"));
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(threadEx).Throw();
        }
    }

    [TestMethod]
    public void FloatingWindow_PracticeNavigationButtons_NavigateSentences()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new FloatingWindow();
                var s1 = new StudySentence(Guid.NewGuid(), "Sentence one is here");
                var s2 = new StudySentence(Guid.NewGuid(), "Sentence two is here");
                var list = new StudyList(Guid.NewGuid(), "Practice List", 10, 0, [s1, s2]);
                var settings = AppSettings.Default with
                {
                    StudyLists = [list],
                    ActiveListId = list.Id
                };

                window.ApplySettings(settings);
                window.StartPractice();

                var progressLabel = (System.Windows.Controls.TextBlock)window.FindName("PracticeProgressLabel");
                var nextButton = (System.Windows.Controls.Button)window.FindName("NextButton");
                var prevButton = (System.Windows.Controls.Button)window.FindName("PreviousButton");

                Assert.IsTrue(progressLabel.Text.Contains("Sentence 1 of 2"));

                // Navigate next
                nextButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.IsTrue(progressLabel.Text.Contains("Sentence 2 of 2"));

                // Navigate previous
                prevButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.IsTrue(progressLabel.Text.Contains("Sentence 1 of 2"));
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(threadEx).Throw();
        }
    }

    [TestMethod]
    public void FloatingWindow_AllSentencesCompleted_ShowsCompletionBannerAndSupportsRestart()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new FloatingWindow();
                var s1 = new StudySentence(Guid.NewGuid(), "Single sentence test");
                var list = new StudyList(Guid.NewGuid(), "Practice List", 10, 0, [s1]);
                var settings = AppSettings.Default with
                {
                    StudyLists = [list],
                    ActiveListId = list.Id
                };

                window.ApplySettings(settings);
                window.StartPractice();

                var projection = ReviewPracticeRules.CreateProjection(s1);
                var projectionPanel = (System.Windows.Controls.WrapPanel)window.FindName("PracticeProjectionPanel");
                var completionPanel = (System.Windows.FrameworkElement)window.FindName("PracticeCompletionPanel");
                var restartButton = (System.Windows.Controls.Button)window.FindName("PracticeRestartButton");

                Assert.AreEqual(System.Windows.Visibility.Collapsed, completionPanel.Visibility);

                // Complete all hidden tokens
                for (var h = 0; h < projection.HiddenTokenIndexes.Count; h++)
                {
                    var tokenIndex = projection.HiddenTokenIndexes[h];
                    var expectedWord = projection.Tokens[tokenIndex].SourceText.Trim('.', ',', '!', '?');

                    var boxes = projectionPanel.Children.OfType<System.Windows.Controls.TextBox>().ToList();
                    var activeBox = boxes.First(b => b.Tag is int p && p == h);
                    activeBox.Text = expectedWord;
                    window.SubmitPracticeAnswer(activeBox, expectedWord);
                }

                // Completion banner should now be visible
                Assert.AreEqual(System.Windows.Visibility.Visible, completionPanel.Visibility);

                // Click restart
                restartButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.AreEqual(System.Windows.Visibility.Collapsed, completionPanel.Visibility);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(threadEx).Throw();
        }
    }




    private static T? FindDescendant<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(parent))
        {
            if (child is T match)
                return match;
            if (child is System.Windows.DependencyObject dep)
            {
                var result = FindDescendant<T>(dep);
                if (result is not null)
                    return result;
            }
        }
        return null;
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

