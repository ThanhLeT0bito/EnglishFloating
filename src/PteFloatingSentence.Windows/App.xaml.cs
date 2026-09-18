using System.Configuration;
using System.Data;
using System.Windows;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private readonly JsonSettingsStore _settingsStore = new();
    private readonly SettingsPersistenceQueue _persistenceQueue;
    private readonly ProtectedApiKeyStore _apiKeyStore = new();
    private GeminiVocabularyExplainer? _explainer;
    private VocabularyWorkflow? _vocabularyWorkflow;
    private Core.AppSettings _settings = Core.AppSettings.Default;
    private FloatingWindow? _floatingWindow;
    private DisplayController? _displayController;
    private SettingsWindow? _settingsWindow;
    private bool _isShuttingDown;

    public App()
    {
        _persistenceQueue = new(_settingsStore.SaveAsync);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = await _settingsStore.LoadAsync();
        var displays = Core.DisplayProjection.PrimaryFirst(System.Windows.Forms.Screen.AllScreens
            .Select(screen => (
                new Core.DisplayBounds(
                    screen.WorkingArea.Left,
                    screen.WorkingArea.Top,
                    screen.WorkingArea.Right,
                    screen.WorkingArea.Bottom),
                screen.Primary)));
        var position = Core.WindowPlacementNormalizer.Normalize(_settings, displays);
        _settings = _settings with { Left = position.Left, Top = position.Top };

        _explainer = new GeminiVocabularyExplainer(() => _apiKeyStore.Load());
        _vocabularyWorkflow = new VocabularyWorkflow(_explainer, () => _settings, SaveSettings);

        _floatingWindow = new FloatingWindow { Left = position.Left, Top = position.Top };
        _displayController = new DisplayController(_floatingWindow);
        _displayController.ShowSettingsRequested += (_, _) => OpenSettings();
        _displayController.OpenFlashcardsRequested += (_, _) => OpenSettings(SettingsPageId.Flashcards);
        _displayController.FlashcardRated += (_, args) =>
        {
            _settings = Core.FlashcardRules.ApplyRating(_settings, args.CardKey, args.Rating, DateTimeOffset.UtcNow);
            PersistSettings();
        };
        _displayController.CardMarkedDone += (_, cardKey) =>
        {
            SaveSettings(Core.FlashcardRules.SetMarkedDone(_settings, cardKey, true, DateTimeOffset.UtcNow));
        };
        _displayController.HideFlashcardRequested += (_, _) =>
        {
            _settings = _settings with { ShowFloatingFlashcard = false };
            _displayController.Apply(_settings);
            PersistSettings();
        };
        _displayController.RestoreOverlayRequested += (_, _) =>
        {
            _settings = _settings with { ShowSentenceOverlay = true };
            _displayController.Apply(_settings);
            PersistSettings();
        };

        _floatingWindow.SettingsRequested += (_, _) => OpenSettings();
        _floatingWindow.ExitRequested += async (_, _) => await ShutdownAsync();
        _floatingWindow.PreviousRequested += (_, _) => NavigateCurrentSentence(-1);
        _floatingWindow.NextRequested += (_, _) => NavigateCurrentSentence(1);
        _floatingWindow.PositionChanged += FloatingWindow_PositionChanged;
        _floatingWindow.SentenceCompleted += (_, args) =>
        {
            _settings = Core.StudyListRules.MarkSentenceCompleted(_settings, args.ListId, args.SentenceId, args.Completed);
            PersistSettings();
        };
        _floatingWindow.VocabularySelected += async (_, selection) =>
        {
            var activeList = Core.StudyListRules.ActiveList(_settings);
            if (activeList.Sentences.Count > 0 && _vocabularyWorkflow is not null)
            {
                var currentSentence = activeList.Sentences[activeList.CurrentSentenceIndex];
                var existing = Core.VocabularyRules.FindEquivalent(currentSentence.Vocabulary, selection);
                if (existing is not null)
                {
                    if (existing.IsHidden)
                    {
                        _vocabularyWorkflow.SetHidden(currentSentence.Id, existing.Id, false);
                    }

                    _floatingWindow.FocusVocabularyItem(existing.Id);
                    return;
                }

                await _vocabularyWorkflow.AddAsync(currentSentence, selection);
            }
        };
        _floatingWindow.VocabularyClicked += (_, itemId) =>
        {
            var activeList = Core.StudyListRules.ActiveList(_settings);
            if (activeList.Sentences.Count > 0 && _vocabularyWorkflow is not null)
            {
                var currentSentence = activeList.Sentences[activeList.CurrentSentenceIndex];
                var item = currentSentence.Vocabulary.FirstOrDefault(v => v.Id == itemId);
                if (item is not null && item.IsHidden)
                {
                    _vocabularyWorkflow.SetHidden(currentSentence.Id, itemId, false);
                }

                _floatingWindow.FocusVocabularyItem(itemId);
            }
        };
        _floatingWindow.HideVocabularyRequested += (_, args) =>
        {
            _vocabularyWorkflow?.SetHidden(args.SentenceId, args.ItemId, true);
        };
        _floatingWindow.RetryVocabularyRequested += async (_, args) =>
        {
            if (_vocabularyWorkflow is not null)
                await _vocabularyWorkflow.RetryAsync(args.SentenceId, args.ItemId);
        };
        _floatingWindow.DeleteVocabularyRequested += (_, args) =>
        {
            _vocabularyWorkflow?.Delete(args.SentenceId, args.ItemId);
        };

        _displayController.Apply(_settings);
    }

    private void OpenSettings(SettingsPageId page = SettingsPageId.Setup)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.UpdateSettingsFromApp(_settings);
            _settingsWindow.NavigateTo(page);
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, SaveSettings, _apiKeyStore);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.StartPracticeRequested += (_, listId) =>
        {
            _floatingWindow?.StartPractice(listId);
        };
        _settingsWindow.NavigateTo(page);
        _settingsWindow.Show();
    }

    private void FloatingWindow_PositionChanged(object? sender, (double Left, double Top) position)
    {
        _settings = _settings with { Left = position.Left, Top = position.Top };
        PersistSettings();
    }

    private void NavigateCurrentSentence(int direction)
    {
        _settings = Core.StudyListRules.MoveCurrentSentence(_settings, direction);
        _floatingWindow?.ApplySettings(_settings);
        PersistSettings();
    }

    private void SaveSettings(Core.AppSettings settings)
    {
        _settings = settings with { Left = _settings.Left, Top = _settings.Top };
        _displayController?.Apply(_settings);
        _settingsWindow?.UpdateSettingsFromApp(_settings);
        PersistSettings();
    }

    private void PersistSettings()
    {
        if (!_isShuttingDown)
            _persistenceQueue.Queue(_settings);
    }

    private async Task ShutdownAsync()
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;
        _displayController?.Dispose();
        _vocabularyWorkflow?.Dispose();
        _explainer?.Dispose();
        await _persistenceQueue.FlushAsync();
        Shutdown();
    }
}
