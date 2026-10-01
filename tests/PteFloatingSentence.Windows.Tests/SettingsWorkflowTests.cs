using PteFloatingSentence.Core;
using PteFloatingSentence.Windows;
using PteFloatingSentence.Windows.Infrastructure;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
[DoNotParallelize]
public class SettingsWorkflowTests
{
    [TestMethod]
    public void FloatingSentence_WideGapKeepsVocabularyHighlightAndSelectionCanonical()
    {
        RunOnSta(() =>
        {
            var vocabulary = VocabularyRules.CreatePending("gym after");
            var sentence = new StudySentence(Guid.NewGuid(), "Go to the gym after work.", Vocabulary: [vocabulary], PhraseBreakAfterWordIndices: [4]);
            var list = new StudyList(Guid.NewGuid(), "Groups", 10, 0, [sentence]);
            var window = new FloatingWindow();
            try
            {
                window.ApplySettings(new AppSettings { StudyLists = [list], ActiveListId = list.Id });
                var box = (RichTextBox)window.FindName("SentenceBox");
                var paragraph = (System.Windows.Documents.Paragraph)box.Document.Blocks.FirstBlock;
                var span = paragraph.Inlines.OfType<System.Windows.Documents.Span>().Single();
                Assert.AreEqual("gym\u2003\u2003after", new System.Windows.Documents.TextRange(span.ContentStart, span.ContentEnd).Text);
                string? selected = null;
                window.VocabularySelected += (_, text) => selected = text;
                box.Selection.Select(paragraph.ContentStart, paragraph.ContentEnd);
                typeof(FloatingWindow).GetMethod("HandleSelection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                Assert.AreEqual("Go to the gym after work", selected);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void RenderSignature_DifferentPhraseBoundariesRequireRedraw()
    {
        var first = RenderSignature.Create("One two three", 20, "white", 0.5, [], [1]);
        var second = RenderSignature.Create("One two three", 20, "white", 0.5, [], [2]);
        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void SentencePhrasingReview_LoadDoesNotConfirm_AndConfirmationUsesEditedGroups()
    {
        RunOnSta(() =>
        {
            var window = new SentencePhrasingReviewWindow("I go after werk.", new FakeSentencePhraser());
            try
            {
                window.LoadProposalAsync().GetAwaiter().GetResult();
                Assert.IsNull(window.ConfirmedGroups);
                var input = (TextBox)window.FindName("GroupsInput");
                input.Text = "I go\nafter work.";
                Assert.IsTrue(window.TryConfirm());
                Assert.AreEqual("I go\nafter work.", window.ConfirmedGroups);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void SentencePhrasingReview_CloseWithoutConfirm_DiscardsProposal()
    {
        RunOnSta(() =>
        {
            var window = new SentencePhrasingReviewWindow("I go after werk.", new FakeSentencePhraser());
            window.LoadProposalAsync().GetAwaiter().GetResult();
            window.Close();
            Assert.IsNull(window.ConfirmedGroups);
        });
    }

    private sealed class FakeSentencePhraser : ISentencePhraser
    {
        public Task<IReadOnlyList<string>> SuggestAsync(string sentence, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["I go", "after work."]);
    }

    [TestMethod]
    public void StudyListDraft_AddPhrasedSentence_StoresBreaksAndManualUpdateClearsThem()
    {
        var draft = new StudyListDraft(AppSettings.Default, _ => { });

        var add = draft.AddPhrasedSentence("I usually go to the gym\nafter work\nwith my friends.");

        Assert.IsTrue(add.IsValid);
        var sentence = draft.SelectedList.Sentences.Last();
        CollectionAssert.AreEqual(new[] { 6, 8 }, sentence.PhraseBreakAfterWordIndices.ToArray());
        var update = draft.UpdateSelectedSentence("I usually go to the gym after work with my friends.");
        Assert.IsTrue(update.IsValid);
        Assert.AreEqual(0, draft.SelectedList.Sentences.Last().PhraseBreakAfterWordIndices.Count);
    }

    [TestMethod]
    public void StudyListDraft_CreateList_UsesUniqueIdAndDefaultTarget()
    {
        var settings = AppSettings.Default;
        var draft = new StudyListDraft(settings, _ => { });

        var created = draft.CreateList();

        Assert.AreNotEqual(Guid.Empty, created.Id);
        Assert.AreNotEqual(settings.StudyLists.Single().Id, created.Id);
        Assert.AreEqual("New list", created.Name);
        Assert.AreEqual(10, created.TargetSentenceCount);
    }

    [TestMethod]
    public void StudyListDraft_DeleteLastList_ReturnsExpectedError()
    {
        var draft = new StudyListDraft(AppSettings.Default, _ => { });

        var result = draft.DeleteSelectedList();

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Cannot delete the last list.", result.Error);
    }

    [TestMethod]
    public void StudyListDraft_AddSentence_RejectsTwentyOneWords()
    {
        var draft = new StudyListDraft(AppSettings.Default, _ => { });
        var sentence = string.Join(' ', Enumerable.Repeat("word", 21));

        var result = draft.AddSentence(sentence);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Use 20 words or fewer.", result.Error);
    }

    [TestMethod]
    public void StudyListDraft_AddAndUpdateSentence_RejectPunctuationOnly()
    {
        var draft = new StudyListDraft(AppSettings.Default, _ => { });
        var originalCount = draft.SelectedList.Sentences.Count;
        var add = draft.AddSentence("!!!");
        Assert.IsFalse(add.IsValid);
        Assert.AreEqual("Enter a sentence.", add.Error);
        Assert.AreEqual(originalCount, draft.SelectedList.Sentences.Count);

        var original = draft.SelectedList.Sentences[0];
        draft.SelectSentence(original.Id);
        var update = draft.UpdateSelectedSentence("... ?!");
        Assert.IsFalse(update.IsValid);
        Assert.AreEqual("Enter a sentence.", update.Error);
        Assert.AreEqual(original.Text, draft.SelectedList.Sentences[0].Text);
    }

    [TestMethod]
    public void StudyListDraft_SaveUnrelatedChange_PreservesPreviouslyStoredPunctuationOnlySentence()
    {
        var sentence = new StudySentence(Guid.NewGuid(), "!!!");
        var list = new StudyList(Guid.NewGuid(), "Legacy", 10, 0, [sentence]);
        var initial = new AppSettings { Sentence = "!!!", ActiveListId = list.Id, StudyLists = [list] };
        AppSettings? saved = null;
        var draft = new StudyListDraft(initial, settings => saved = settings);
        draft.SetPracticeMode(PracticeMode.ListenAndWrite);

        var result = draft.Save();

        Assert.IsTrue(result.IsValid);
        Assert.IsNotNull(saved);
        Assert.AreEqual(PracticeMode.ListenAndWrite, saved.PracticeMode);
        Assert.AreEqual("!!!", saved.StudyLists.Single().Sentences.Single().Text);
    }

    [TestMethod]
    public void StudyListDraft_SelectSentence_OnlyChangesSelectedListsCurrentIndex()
    {
        var first = new StudyList(Guid.NewGuid(), "First", 10, 0,
            [new StudySentence(Guid.NewGuid(), "First sentence."), new StudySentence(Guid.NewGuid(), "Second sentence.")]);
        var second = new StudyList(Guid.NewGuid(), "Second", 10, 0,
            [new StudySentence(Guid.NewGuid(), "Third sentence."), new StudySentence(Guid.NewGuid(), "Fourth sentence.")]);
        var draft = new StudyListDraft(new AppSettings { ActiveListId = first.Id, StudyLists = [first, second] }, _ => { });

        draft.SelectSentence(first.Sentences[1].Id);

        Assert.AreEqual(1, draft.Settings.StudyLists.Single(list => list.Id == first.Id).CurrentSentenceIndex);
        Assert.AreEqual(0, draft.Settings.StudyLists.Single(list => list.Id == second.Id).CurrentSentenceIndex);
    }

    [TestMethod]
    public void StudyListDraft_Cancel_DoesNotInvokeSaveCallback()
    {
        var callbackInvoked = false;
        var draft = new StudyListDraft(AppSettings.Default, _ => callbackInvoked = true);

        draft.Cancel();

        Assert.IsFalse(callbackInvoked);
    }

    [TestMethod]
    public void StudyListDraft_Save_InvokesCallbackOnlyOnce()
    {
        var callbackCount = 0;
        var draft = new StudyListDraft(AppSettings.Default, _ => callbackCount++);

        Assert.IsTrue(draft.Save().IsValid);
        Assert.IsTrue(draft.Save().IsValid);

        Assert.AreEqual(1, callbackCount);
    }

    [TestMethod]
    public void SettingsWindow_UpdateSelectedSentence_SyncsUnsavedListFields()
    {
        RunOnSta(() =>
        {
            var window = CreateWindowWithTwoSentences();
            Named<ListBox>(window, "SentenceList").SelectedIndex = 0;
            Named<TextBox>(window, "ListNameInput").Text = "Edited list";
            Named<TextBox>(window, "TargetInput").Text = "12";
            Named<TextBox>(window, "SentenceInput").Text = "Updated sentence.";

            InvokeClick(window, "UpdateSentenceButton_Click");

            var list = DraftFor(window).SelectedList;
            Assert.AreEqual("Edited list", list.Name);
            Assert.AreEqual(12, list.TargetSentenceCount);
            Assert.AreEqual("Updated sentence.", list.Sentences[0].Text);
        });
    }

    [TestMethod]
    public void SettingsWindow_DeleteSelectedSentence_SyncsUnsavedListFields()
    {
        RunOnSta(() =>
        {
            var window = CreateWindowWithTwoSentences();
            Named<ListBox>(window, "SentenceList").SelectedIndex = 0;
            Named<TextBox>(window, "ListNameInput").Text = "Edited list";
            Named<TextBox>(window, "TargetInput").Text = "12";

            InvokeClick(window, "DeleteSentenceButton_Click");

            var list = DraftFor(window).SelectedList;
            Assert.AreEqual("Edited list", list.Name);
            Assert.AreEqual(12, list.TargetSentenceCount);
            Assert.AreEqual(1, list.Sentences.Count);
            Assert.AreEqual("Second sentence.", list.Sentences[0].Text);
        });
    }

    [TestMethod]
    public void SettingsWindow_UpdateSelectedSentence_InvalidListFields_PreservesEditorWithoutRefresh()
    {
        RunOnSta(() =>
        {
            var window = CreateWindowWithTwoSentences();
            Named<ListBox>(window, "SentenceList").SelectedIndex = 0;
            Named<TextBox>(window, "ListNameInput").Text = "Uncommitted list";
            Named<TextBox>(window, "TargetInput").Text = "not a number";
            Named<TextBox>(window, "SentenceInput").Text = "Updated sentence.";

            InvokeClick(window, "UpdateSentenceButton_Click");

            Assert.AreEqual("Uncommitted list", Named<TextBox>(window, "ListNameInput").Text);
            Assert.AreEqual("not a number", Named<TextBox>(window, "TargetInput").Text);
            Assert.AreEqual("Updated sentence.", Named<TextBox>(window, "SentenceInput").Text);
            Assert.AreEqual("First sentence.", DraftFor(window).SelectedList.Sentences[0].Text);
        });
    }

    [TestMethod]
    public void SettingsWindow_DeleteSelectedSentence_InvalidListFields_PreservesEditorWithoutRefresh()
    {
        RunOnSta(() =>
        {
            var window = CreateWindowWithTwoSentences();
            Named<ListBox>(window, "SentenceList").SelectedIndex = 0;
            Named<TextBox>(window, "ListNameInput").Text = "Uncommitted list";
            Named<TextBox>(window, "TargetInput").Text = "not a number";

            InvokeClick(window, "DeleteSentenceButton_Click");

            Assert.AreEqual("Uncommitted list", Named<TextBox>(window, "ListNameInput").Text);
            Assert.AreEqual("not a number", Named<TextBox>(window, "TargetInput").Text);
            Assert.AreEqual(2, DraftFor(window).SelectedList.Sentences.Count);
        });
    }

    [TestMethod]
    public void SettingsWindow_InvalidTargetDuringListSwitch_PreservesEditorAndSelection()
    {
        RunOnSta(() =>
        {
            var window = CreateWindowWithTwoLists();
            var originalListId = DraftFor(window).SelectedListId;
            var sidebar = Named<ListBox>(window, "StudyListList");
            Named<TextBox>(window, "ListNameInput").Text = "Uncommitted list";
            Named<TextBox>(window, "TargetInput").Text = "not a number";

            sidebar.SelectedIndex = 1;

            Assert.AreEqual("Uncommitted list", Named<TextBox>(window, "ListNameInput").Text);
            Assert.AreEqual("not a number", Named<TextBox>(window, "TargetInput").Text);
            Assert.AreEqual(originalListId, DraftFor(window).SelectedListId);
            Assert.AreEqual(originalListId, ((ListBoxItem)sidebar.SelectedItem).Tag);
        });
    }

    [TestMethod]
    public void SettingsWindow_FinalList_DisablesDeleteControl()
    {
        RunOnSta(() =>
        {
            var window = CreateWindowWithTwoSentences();

            Assert.IsFalse(Named<Button>(window, "DeleteListButton").IsEnabled);
        });
    }

    [TestMethod]
    public void SettingsWindow_SidebarMarksActiveListWhenAnotherListIsSelected()
    {
        RunOnSta(() =>
        {
            var window = CreateWindowWithTwoLists();
            var activeListId = DraftFor(window).Settings.ActiveListId;
            var sidebar = Named<ListBox>(window, "StudyListList");

            sidebar.SelectedIndex = 1;

            var activeItem = sidebar.Items.OfType<ListBoxItem>().Single(item => (Guid)item.Tag == activeListId);
            StringAssert.Contains(activeItem.Content.ToString(), "Active");
        });
    }

    [TestMethod]
    public async Task SettingsPersistenceQueue_AllowsALaterSaveAfterAFailedWrite()
    {
        var queueType = typeof(JsonSettingsStore).Assembly.GetType("PteFloatingSentence.Windows.Infrastructure.SettingsPersistenceQueue");

        Assert.IsNotNull(queueType);
        var queueMethod = queueType.GetMethod("Queue");
        var flushMethod = queueType.GetMethod("FlushAsync");
        Assert.IsNotNull(queueMethod);
        Assert.IsNotNull(flushMethod);

        var writes = new List<AppSettings>();
        var failFirstWrite = true;
        Func<AppSettings, Task> save = settings =>
        {
            writes.Add(settings);
            if (failFirstWrite)
            {
                failFirstWrite = false;
                throw new IOException("Settings are temporarily unavailable.");
            }

            return Task.CompletedTask;
        };
        var queue = Activator.CreateInstance(queueType, save)!;
        var first = AppSettings.Default with { Sentence = "First" };
        var retry = AppSettings.Default with { Sentence = "Retry" };

        queueMethod.Invoke(queue, [first]);
        await (Task)flushMethod.Invoke(queue, null)!;
        queueMethod.Invoke(queue, [retry]);
        await (Task)flushMethod.Invoke(queue, null)!;

        CollectionAssert.AreEqual(new[] { first, retry }, writes);
    }

    [TestMethod]
    public async Task SettingsPersistenceQueue_SerializesWritesAndCoalescesLatestSnapshot()
    {
        var queueType = typeof(JsonSettingsStore).Assembly.GetType("PteFloatingSentence.Windows.Infrastructure.SettingsPersistenceQueue");

        Assert.IsNotNull(queueType);
        var queueMethod = queueType.GetMethod("Queue");
        var flushMethod = queueType.GetMethod("FlushAsync");
        Assert.IsNotNull(queueMethod);
        Assert.IsNotNull(flushMethod);

        var writes = new List<AppSettings>();
        var firstWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<AppSettings, Task> save = async settings =>
        {
            writes.Add(settings);
            if (writes.Count == 1)
            {
                firstWriteStarted.SetResult();
                await releaseFirstWrite.Task;
            }
        };
        var queue = Activator.CreateInstance(queueType, save)!;
        var first = AppSettings.Default with { Sentence = "First" };
        var second = AppSettings.Default with { Sentence = "Second" };
        var latest = AppSettings.Default with { Sentence = "Latest" };

        queueMethod.Invoke(queue, [first]);
        await firstWriteStarted.Task;
        queueMethod.Invoke(queue, [second]);
        queueMethod.Invoke(queue, [latest]);
        releaseFirstWrite.SetResult();
        await (Task)flushMethod.Invoke(queue, null)!;

        CollectionAssert.AreEqual(new[] { first, latest }, writes);
    }

    [TestMethod]
    public void SettingsNumericValidator_RejectsNonFiniteInputs()
    {
        var validatorType = typeof(AppSettings).Assembly.GetType("PteFloatingSentence.Core.SettingsNumericValidator");

        Assert.IsNotNull(validatorType);
        var fontSizeMethod = validatorType.GetMethod("IsValidFontSize");
        var opacityMethod = validatorType.GetMethod("IsValidOpacity");
        Assert.IsNotNull(fontSizeMethod);
        Assert.IsNotNull(opacityMethod);

        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.IsFalse((bool)fontSizeMethod.Invoke(null, [value])!);
            Assert.IsFalse((bool)opacityMethod.Invoke(null, [value])!);
        }
    }

    [TestMethod]
    public void SettingsUpdateMerger_PreservesLatestPositionAndVersion()
    {
        var mergerType = typeof(AppSettings).Assembly.GetType("PteFloatingSentence.Core.SettingsUpdateMerger");

        Assert.IsNotNull(mergerType);
        var mergeMethod = mergerType.GetMethod("MergeEditableFields");
        Assert.IsNotNull(mergeMethod);

        var latest = AppSettings.Default with { Version = 4, Left = 345, Top = 678, Sentence = "Original" };
        var submitted = AppSettings.Default with
        {
            Version = 1,
            Left = 100,
            Top = 200,
            Sentence = "Updated sentence",
            FontSize = 44,
            TextColor = "#FF102030",
            BackgroundOpacity = 0.7
        };

        var merged = (AppSettings)mergeMethod.Invoke(null, [latest, submitted])!;

        Assert.AreEqual(4, merged.Version);
        Assert.AreEqual(345d, merged.Left);
        Assert.AreEqual(678d, merged.Top);
        Assert.AreEqual("Updated sentence", merged.Sentence);
        Assert.AreEqual(44d, merged.FontSize);
        Assert.AreEqual("#FF102030", merged.TextColor);
        Assert.AreEqual(0.7d, merged.BackgroundOpacity);
    }

    [TestMethod]
    public void SettingsWindow_SaveWithApiKey_SavesProtectedKeyAndUpdatesConfiguredFlag()
    {
        RunOnSta(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var store = new ProtectedApiKeyStore("PteFloatingSentence", tempDir);
                AppSettings? savedSettings = null;
                var window = new SettingsWindow(AppSettings.Default, s => savedSettings = s, store);

                Named<PasswordBox>(window, "ApiKeyInput").Password = "my-new-secret-gemini-key";
                InvokeClick(window, "SaveButton_Click");

                Assert.IsNotNull(savedSettings);
                Assert.IsTrue(savedSettings.GeminiApiKeyConfigured);

                var loadedKey = store.LoadAsync().GetAwaiter().GetResult();
                Assert.AreEqual("my-new-secret-gemini-key", loadedKey);
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        });
    }

    [TestMethod]
    public void SettingsWindow_ClearApiKey_RemovesKeyAndClearsConfiguredFlag()
    {
        RunOnSta(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var store = new ProtectedApiKeyStore("PteFloatingSentence", tempDir);
                store.SaveAsync("existing-key").GetAwaiter().GetResult();

                AppSettings? savedSettings = null;
                var window = new SettingsWindow(AppSettings.Default with { GeminiApiKeyConfigured = true }, s => savedSettings = s, store);

                InvokeClick(window, "ClearApiKeyButton_Click");
                InvokeClick(window, "SaveButton_Click");

                Assert.IsNotNull(savedSettings);
                Assert.IsFalse(savedSettings.GeminiApiKeyConfigured);

                var loadedKey = store.LoadAsync().GetAwaiter().GetResult();
                Assert.IsNull(loadedKey);
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        });
    }

    [TestMethod]
    public void StudyListDraft_SetDisplayPreferences_UpdatesSettingsWithoutAlteringLists()
    {
        var draft = new StudyListDraft(AppSettings.Default, _ => { });
        draft.SetDisplayPreferences(false, false);

        Assert.IsFalse(draft.Settings.ShowSentenceOverlay);
        Assert.IsFalse(draft.Settings.ShowVocabularyCards);
    }

    [TestMethod]
    public void StudyListDraft_SetPracticeMode_PersistsOnSaveAndRefreshesFromApp()
    {
        AppSettings? saved = null;
        var draft = new StudyListDraft(AppSettings.Default, settings => saved = settings);

        draft.SetPracticeMode(PracticeMode.ListenAndWrite);
        Assert.AreEqual(PracticeMode.ListenAndWrite, draft.Settings.PracticeMode);
        Assert.IsTrue(draft.Save().IsValid);
        Assert.AreEqual(PracticeMode.ListenAndWrite, saved?.PracticeMode);

        draft.UpdateSettingsFromApp(AppSettings.Default);
        Assert.AreEqual(PracticeMode.TextHints, draft.Settings.PracticeMode);
    }

    [TestMethod]
    public void SettingsWindow_SaveDisplayPreferences_SavesUncheckedValues()
    {
        RunOnSta(() =>
        {
            AppSettings? saved = null;
            var window = new SettingsWindow(AppSettings.Default, s => saved = s);

            var showSentenceBox = Named<CheckBox>(window, "ShowSentenceOverlayInput");
            var showVocabBox = Named<CheckBox>(window, "ShowVocabularyCardsInput");

            Assert.IsTrue(showSentenceBox.IsChecked);
            Assert.IsTrue(showVocabBox.IsChecked);

            showSentenceBox.IsChecked = false;
            showVocabBox.IsChecked = false;

            InvokeClick(window, "SaveButton_Click");

            Assert.IsNotNull(saved);
            Assert.IsFalse(saved.ShowSentenceOverlay);
            Assert.IsFalse(saved.ShowVocabularyCards);
        });
    }

    [TestMethod]
    public void SettingsWindow_LoadsDisplayPreferencesFromSettings()
    {
        RunOnSta(() =>
        {
            var initial = AppSettings.Default with
            {
                ShowSentenceOverlay = false,
                ShowVocabularyCards = true
            };
            var window = new SettingsWindow(initial, _ => { });

            var showSentenceBox = Named<CheckBox>(window, "ShowSentenceOverlayInput");
            var showVocabBox = Named<CheckBox>(window, "ShowVocabularyCardsInput");

            Assert.IsFalse(showSentenceBox.IsChecked);
            Assert.IsTrue(showVocabBox.IsChecked);
        });
    }

    [TestMethod]
    public void SettingsWindow_ReviewTab_TogglesVisibilityAndPopulatesReviewList()
    {
        RunOnSta(() =>
        {
            var window = CreateWindowWithTwoSentences();
            var setupSection = Named<Grid>(window, "SetupSection");
            var reviewSection = Named<Grid>(window, "ReviewSection");
            var reviewTab = Named<RadioButton>(window, "ReviewTabButton");
            var setupTab = Named<RadioButton>(window, "SetupTabButton");
            var reviewList = Named<ItemsControl>(window, "ReviewList");
            var emptyLabel = Named<TextBlock>(window, "ReviewEmptyStateLabel");

            Assert.AreEqual(Visibility.Visible, setupSection.Visibility);
            Assert.AreEqual(Visibility.Collapsed, reviewSection.Visibility);

            reviewTab.IsChecked = true;

            Assert.AreEqual(Visibility.Collapsed, setupSection.Visibility);
            Assert.AreEqual(Visibility.Visible, reviewSection.Visibility);
            Assert.AreEqual(Visibility.Collapsed, emptyLabel.Visibility);
            Assert.AreEqual(Visibility.Visible, reviewList.Visibility);
            Assert.IsNotNull(reviewList.ItemsSource);

            setupTab.IsChecked = true;

            Assert.AreEqual(Visibility.Visible, setupSection.Visibility);
            Assert.AreEqual(Visibility.Collapsed, reviewSection.Visibility);
        });
    }

    [TestMethod]
    public void SettingsNavigation_DefaultsToSetupPage()
    {
        RunOnSta(() =>
        {
            var window = CreateWindowWithTwoSentences();
            Assert.AreEqual(SettingsPageId.Setup, window.SelectedPage);
        });
    }

    [TestMethod]
    public void SettingsNavigation_NavigateTo_ChangesSelectedPage()
    {
        RunOnSta(() =>
        {
            var window = CreateWindowWithTwoSentences();

            window.NavigateTo(SettingsPageId.Display);
            Assert.AreEqual(SettingsPageId.Display, window.SelectedPage);

            window.NavigateTo(SettingsPageId.Review);
            Assert.AreEqual(SettingsPageId.Review, window.SelectedPage);

            window.NavigateTo(SettingsPageId.Gemini);
            Assert.AreEqual(SettingsPageId.Gemini, window.SelectedPage);

            window.NavigateTo(SettingsPageId.Setup);
            Assert.AreEqual(SettingsPageId.Setup, window.SelectedPage);
        });
    }

    [TestMethod]
    public void SettingsNavigation_PreservesDraftStateAcrossPageSwitches()
    {
        RunOnSta(() =>
        {
            var window = CreateWindowWithTwoSentences();
            var draft = DraftFor(window);
            var initialListId = draft.SelectedListId;

            window.NavigateTo(SettingsPageId.Display);
            window.NavigateTo(SettingsPageId.Review);
            window.NavigateTo(SettingsPageId.Gemini);
            window.NavigateTo(SettingsPageId.Setup);

            Assert.AreEqual(initialListId, DraftFor(window).SelectedListId);
            Assert.AreEqual(2, DraftFor(window).SelectedList.Sentences.Count);
        });
    }

    [TestMethod]
    public void SettingsNavigation_SwitchingPages_PreservesUnsavedUserInputsAcrossAllPages()
    {
        RunOnSta(() =>
        {
            var window = CreateWindowWithTwoSentences();
            var listNameInput = Named<TextBox>(window, "ListNameInput");
            var sentenceInput = Named<TextBox>(window, "SentenceInput");
            var apiKeyInput = Named<PasswordBox>(window, "ApiKeyInput");

            listNameInput.Text = "Unsaved List Name";
            sentenceInput.Text = "Unsaved Sentence Text";
            apiKeyInput.Password = "UnsavedApiKey123";

            window.NavigateTo(SettingsPageId.Display);
            window.NavigateTo(SettingsPageId.Review);
            window.NavigateTo(SettingsPageId.Gemini);

            Assert.AreEqual("UnsavedApiKey123", apiKeyInput.Password);

            window.NavigateTo(SettingsPageId.Setup);

            Assert.AreEqual("Unsaved List Name", listNameInput.Text);
            Assert.AreEqual("Unsaved Sentence Text", sentenceInput.Text);
        });
    }

    [TestMethod]
    public void DisplayPage_LoadsPreferences_AndEmitsDisplayPreferencesChangedOnToggle()
    {
        RunOnSta(() =>
        {
            var page = new DisplayPage();
            page.LoadPreferences(showSentenceOverlay: true, showVocabularyCards: false);

            var sentenceCheck = (CheckBox)page.FindName("ShowSentenceOverlayInput");
            var vocabCheck = (CheckBox)page.FindName("ShowVocabularyCardsInput");

            Assert.IsNotNull(sentenceCheck);
            Assert.IsNotNull(vocabCheck);
            Assert.IsTrue(sentenceCheck.IsChecked);
            Assert.IsFalse(vocabCheck.IsChecked);

            bool? emittedSentence = null;
            bool? emittedVocab = null;
            Action<bool, bool> handler = (s, v) =>
            {
                emittedSentence = s;
                emittedVocab = v;
            };
            page.DisplayPreferencesChanged += handler;
            try
            {
                vocabCheck.IsChecked = true;
                Assert.AreEqual(true, emittedSentence);
                Assert.AreEqual(true, emittedVocab);
            }
            finally
            {
                page.DisplayPreferencesChanged -= handler;
            }
        });
    }

    [TestMethod]
    public void DisplayPage_LoadsFullPreferences_AndEmitsFullDisplayPreferencesChangedOnToggle()
    {
        RunOnSta(() =>
        {
            var page = new DisplayPage();
            var summaries = new[]
            {
                new PteFloatingSentence.Core.FlashcardDeckSummary("deck-1", null, "Deck 1", false, 5, 2, 1, 2)
            };
            page.LoadPreferences(
                showSentenceOverlay: true,
                showVocabularyCards: false,
                showFloatingFlashcard: false,
                activeDeckKey: "deck-1",
                availableDecks: summaries);

            var flashcardCheck = (CheckBox)page.FindName("ShowFloatingFlashcardInput");
            var deckSelector = (ComboBox)page.FindName("FloatingDeckComboBox");

            Assert.IsNotNull(flashcardCheck);
            Assert.IsNotNull(deckSelector);
            Assert.IsFalse(flashcardCheck.IsChecked);
            Assert.AreEqual(1, deckSelector.Items.Count);

            bool? emittedSentence = null;
            bool? emittedVocab = null;
            bool? emittedFlashcard = null;
            string? emittedDeck = null;

            Action<bool, bool, bool, string?> handler = (s, v, f, d) =>
            {
                emittedSentence = s;
                emittedVocab = v;
                emittedFlashcard = f;
                emittedDeck = d;
            };

            page.FullDisplayPreferencesChanged += handler;
            try
            {
                flashcardCheck.IsChecked = true;
                Assert.AreEqual(true, emittedSentence);
                Assert.AreEqual(false, emittedVocab);
                Assert.AreEqual(true, emittedFlashcard);
                Assert.AreEqual("deck-1", emittedDeck);
            }
            finally
            {
                page.FullDisplayPreferencesChanged -= handler;
            }
        });
    }

    [TestMethod]
    public void SettingsWindow_ReviewPractice_UsesSelectedListAndCurrentCompletion()
    {
        RunOnSta(() =>
        {
            var window = CreateWindowWithTwoLists();
            window.NavigateTo(SettingsPageId.ReviewPractice);

            var practiceControl = Named<ReviewPracticePage>(window, "ReviewPracticePageControl");
            Assert.IsNotNull(practiceControl);

            var progressLabel = (TextBlock)((PracticePanel)practiceControl.FindName("SettingsPracticePanel")).FindName("PracticeProgressLabel");
            Assert.AreEqual("Sentence 1 of 1", progressLabel.Text);
        });
    }

    [TestMethod]
    public void SettingsWindow_ReviewPractice_CompletingSentence_PersistsOnSaveAndRefreshesReview()
    {
        RunOnSta(() =>
        {
            AppSettings? saved = null;
            var sentence = new StudySentence(Guid.NewGuid(), "Alpha beta gamma.");
            var list = new StudyList(Guid.NewGuid(), "Practice List", 10, 0, [sentence]);
            var settings = new AppSettings { ActiveListId = list.Id, StudyLists = [list] };

            var window = new SettingsWindow(settings, s => saved = s);
            window.NavigateTo(SettingsPageId.ReviewPractice);

            var practiceControl = Named<ReviewPracticePage>(window, "ReviewPracticePageControl");
            var session = (ReviewPracticeSession)typeof(ReviewPracticePage).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(practiceControl)!;

            for (var i = 0; i < session.CurrentReview.HiddenTokenIndexes.Count; i++)
            {
                var token = session.CurrentReview.Tokens[session.CurrentReview.HiddenTokenIndexes[i]];
                session.Submit(token.SourceText.Trim('.', ','));
            }

            Assert.IsTrue(session.IsComplete);

            var reviewControl = Named<ReviewPage>(window, "ReviewPageControl");
            var reviewList = (ItemsControl)reviewControl.FindName("ReviewList");
            var summaries = (IReadOnlyList<ListReviewSummary>)reviewList.ItemsSource;
            Assert.AreEqual(1, summaries.Single().CompletedSentenceCount);

            InvokeClick(window, "SaveButton_Click");

            Assert.IsNotNull(saved);
            Assert.IsTrue(saved.StudyLists.Single().Sentences.Single().IsCompleted);
        });
    }

    [TestMethod]
    public void SettingsWindow_ReviewPractice_Cancel_DoesNotPersistCompletion()
    {
        RunOnSta(() =>
        {
            var saveInvoked = false;
            var sentence = new StudySentence(Guid.NewGuid(), "Alpha beta gamma.");
            var list = new StudyList(Guid.NewGuid(), "Practice List", 10, 0, [sentence]);
            var settings = new AppSettings { ActiveListId = list.Id, StudyLists = [list] };

            var window = new SettingsWindow(settings, _ => saveInvoked = true);
            window.NavigateTo(SettingsPageId.ReviewPractice);

            var practiceControl = Named<ReviewPracticePage>(window, "ReviewPracticePageControl");
            var session = (ReviewPracticeSession)typeof(ReviewPracticePage).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(practiceControl)!;

            for (var i = 0; i < session.CurrentReview.HiddenTokenIndexes.Count; i++)
            {
                var token = session.CurrentReview.Tokens[session.CurrentReview.HiddenTokenIndexes[i]];
                session.Submit(token.SourceText.Trim('.', ','));
            }

            InvokeClick(window, "CancelButton_Click");

            Assert.IsFalse(saveInvoked);
        });
    }

    [TestMethod]
    public void SettingsWindow_UpdateSettingsFromApp_RefreshesFlashcardsPageWithNewVocabulary()
    {
        RunOnSta(() =>
        {
            var listId = Guid.NewGuid();
            var sentenceId = Guid.NewGuid();
            var initialSentence = new StudySentence(sentenceId, "The cat sleeps on the mat.");
            var initialList = new StudyList(listId, "Animals", 10, 0, [initialSentence]);
            var initialSettings = new AppSettings { ActiveListId = listId, StudyLists = [initialList] };

            var window = new SettingsWindow(initialSettings, _ => { });
            window.NavigateTo(SettingsPageId.Flashcards);

            var flashcardsControl = Named<FlashcardsPage>(window, "FlashcardsPageControl");
            var cardsList = (ItemsControl)flashcardsControl.FindName("CardsListControl");

            // Initially no vocabulary
            Assert.AreEqual(0, cardsList.Items.Count);

            // Live external addition (e.g. word highlighted on desktop sentence)
            var newVocab = new VocabularyItem(
                Guid.NewGuid(),
                "cat",
                "cat",
                Meaning: "A small domesticated carnivorous mammal.",
                Example: "The cat sleeps on the mat.",
                Status: VocabularyStatus.Ready,
                IsHidden: true); // Even if hidden from floating overlay!

            var updatedSentence = initialSentence with { Vocabulary = [newVocab] };
            var updatedList = initialList with { Sentences = [updatedSentence] };
            var updatedSettings = initialSettings with { StudyLists = [updatedList] };

            window.UpdateSettingsFromApp(updatedSettings);

            // Assert that Flashcards page immediately contains the new card!
            Assert.AreEqual(1, cardsList.Items.Count);
        });
    }

    [TestMethod]
    public void SetupPage_StartPracticeButton_RaisesStartPracticeRequestedAndPassesSelectedListId()
    {
        RunOnSta(() =>
        {
            var list = new StudyList(Guid.NewGuid(), "Target Practice List", 10, 0,
                [new StudySentence(Guid.NewGuid(), "Sentence for practice.")]);
            var settings = new AppSettings { ActiveListId = list.Id, StudyLists = [list] };

            Guid? requestedListId = null;
            var window = new SettingsWindow(settings, _ => { });
            window.StartPracticeRequested += (_, id) => requestedListId = id;

            var startButton = Named<System.Windows.Controls.Button>(window, "StartPracticeButton");
            startButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            Assert.AreEqual(list.Id, requestedListId);
        });
    }

    [TestMethod]
    public void FloatingWindow_ContextMenu_StartAndExitPractice_TogglesPracticeMode()
    {
        RunOnSta(() =>
        {
            var list = new StudyList(Guid.NewGuid(), "Practice List", 10, 0,
                [new StudySentence(Guid.NewGuid(), "Sentence to test context menu.")]);
            var settings = new AppSettings { ActiveListId = list.Id, StudyLists = [list] };

            var window = new FloatingWindow();
            window.ApplySettings(settings);

            var practiceItem = (System.Windows.Controls.MenuItem)window.FindName("PracticeMenuItem");
            Assert.IsFalse(window.IsPracticeMode);
            Assert.AreEqual("Start Practice", practiceItem.Header);

            // Click menu item -> Start Practice
            practiceItem.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Assert.IsTrue(window.IsPracticeMode);
            Assert.AreEqual("Exit Practice", practiceItem.Header);

            // Click menu item -> Exit Practice
            practiceItem.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Assert.IsFalse(window.IsPracticeMode);
            Assert.AreEqual("Start Practice", practiceItem.Header);
        });
    }

    [TestMethod]
    public void DisplayPage_UpdatesTtsVoiceAndSpeed_WhenChanged()
    {
        RunOnSta(() =>
        {
            var page = new DisplayPage();
            var initial = AppSettings.Default with
            {
                TtsVoice = "en-US-JennyNeural",
                TtsSpeed = 1.0
            };
            page.LoadSettings(initial);

            var voiceCombo = (ComboBox)page.FindName("VoiceAccentComboBox");
            var speedSlider = (Slider)page.FindName("SpeedSlider");
            var speedLabel = (TextBlock)page.FindName("SpeedValueLabel");

            Assert.IsNotNull(voiceCombo);
            Assert.IsNotNull(speedSlider);
            Assert.IsNotNull(speedLabel);

            voiceCombo.SelectedValue = "en-AU-NatashaNeural";
            speedSlider.Value = 1.15;

            Assert.AreEqual("1.15x", speedLabel.Text);
            Assert.AreEqual("en-AU-NatashaNeural", page.TtsVoice);
            Assert.AreEqual(1.15, page.TtsSpeed, 0.001);

            var updated = page.ApplySettings(initial);
            Assert.AreEqual("en-AU-NatashaNeural", updated.TtsVoice);
            Assert.AreEqual(1.15, updated.TtsSpeed, 0.001);
        });
    }

    [TestMethod]
    public void DisplayPage_ClearAudioCache_CallsClearCacheAndRefreshesLabel()
    {
        RunOnSta(() =>
        {
            var fakeCache = new FakeAudioCacheManager();
            var page = new DisplayPage(fakeCache);
            page.LoadSettings(AppSettings.Default);

            var cacheLabel = (TextBlock)page.FindName("CacheSizeLabel");
            var clearButton = (Button)page.FindName("ClearAudioCacheButton");

            Assert.IsNotNull(cacheLabel);
            Assert.IsNotNull(clearButton);
            Assert.AreEqual("Cache size: 1.0 MB / 20 MB", cacheLabel.Text);

            clearButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            Assert.IsTrue(fakeCache.ClearCacheCalled);
            Assert.AreEqual("Cache size: 0.0 MB / 20 MB", cacheLabel.Text);
        });
    }

    [TestMethod]
    public void SettingsWindow_SaveTtsPreferences_SavesVoiceAndSpeed()
    {
        RunOnSta(() =>
        {
            AppSettings? saved = null;
            var initial = AppSettings.Default with
            {
                TtsVoice = "en-US-JennyNeural",
                TtsSpeed = 1.0
            };
            var window = new SettingsWindow(initial, s => saved = s);
            var voiceCombo = Named<ComboBox>(window, "VoiceAccentComboBox");
            var speedSlider = Named<Slider>(window, "SpeedSlider");

            voiceCombo.SelectedValue = "en-AU-WilliamNeural";
            speedSlider.Value = 1.10;

            InvokeClick(window, "SaveButton_Click");

            Assert.IsNotNull(saved);
            Assert.AreEqual("en-AU-WilliamNeural", saved.TtsVoice);
            Assert.AreEqual(1.10, saved.TtsSpeed, 0.001);
        });
    }

    private sealed class FakeAudioCacheManager : IAudioCacheManager
    {
        public bool ClearCacheCalled { get; private set; }
        public long TotalCacheSizeBytes { get; set; } = 1024 * 1024;

        public void ClearCache()
        {
            ClearCacheCalled = true;
            TotalCacheSizeBytes = 0;
        }

        public long GetTotalCacheSizeBytes() => TotalCacheSizeBytes;
        public string GetCacheFilePath(string text, string voice, double speed) => string.Empty;
        public bool TryGetCachedAudio(string text, string voice, double speed, out string filePath)
        {
            filePath = string.Empty;
            return false;
        }
        public Task SaveAudioAsync(string text, string voice, double speed, System.IO.Stream audioStream, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void PruneToLimit(long maxSizeBytes) { }
    }


    private static SettingsWindow CreateWindowWithTwoSentences()
    {
        var list = new StudyList(Guid.NewGuid(), "Original list", 10, 0,
            [new StudySentence(Guid.NewGuid(), "First sentence."), new StudySentence(Guid.NewGuid(), "Second sentence.")]);
        return new SettingsWindow(new AppSettings { ActiveListId = list.Id, StudyLists = [list] }, _ => { });
    }

    private static SettingsWindow CreateWindowWithTwoLists()
    {
        var first = new StudyList(Guid.NewGuid(), "First list", 10, 0,
            [new StudySentence(Guid.NewGuid(), "First sentence.")]);
        var second = new StudyList(Guid.NewGuid(), "Second list", 10, 0,
            [new StudySentence(Guid.NewGuid(), "Second sentence.")]);
        return new SettingsWindow(new AppSettings { ActiveListId = first.Id, StudyLists = [first, second] }, _ => { });
    }

    private static StudyListDraft DraftFor(SettingsWindow window) =>
        (StudyListDraft)typeof(SettingsWindow).GetField("_draft", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static T Named<T>(SettingsWindow window, string name) where T : FrameworkElement
    {
        var found = window.FindName(name) as T;
        if (found is not null)
            return found;

        return FindDescendantByName<T>(window, name)
            ?? throw new InvalidOperationException($"Control '{name}' of type {typeof(T).Name} not found in SettingsWindow.");
    }

    private static T? FindDescendantByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        if (parent is FrameworkElement fe)
        {
            var found = fe.FindName(name) as T;
            if (found is not null)
                return found;
        }

        if (parent is ContentControl cc && cc.Content is DependencyObject contentChild)
        {
            var matchInContent = FindDescendantByName<T>(contentChild, name);
            if (matchInContent is not null)
                return matchInContent;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is DependencyObject dChild)
            {
                var result = FindDescendantByName<T>(dChild, name);
                if (result is not null)
                    return result;
            }
        }
        return null;
    }

    private static void InvokeClick(SettingsWindow window, string methodName)
    {
        var method = typeof(SettingsWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (method is not null)
        {
            method.Invoke(window, [window, new RoutedEventArgs()]);
            return;
        }

        var setupPage = typeof(SettingsWindow).GetField("_setupPage", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window);
        if (setupPage is not null)
        {
            var pageMethod = setupPage.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (pageMethod is not null)
            {
                pageMethod.Invoke(setupPage, [setupPage, new RoutedEventArgs()]);
                return;
            }
        }

        throw new InvalidOperationException($"Method '{methodName}' not found on SettingsWindow or SetupPage.");
    }

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
}
