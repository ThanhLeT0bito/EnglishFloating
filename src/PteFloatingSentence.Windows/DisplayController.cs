using System.Windows;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

public sealed class DisplayController : IDisposable
{
    private readonly FloatingWindow _floatingWindow;
    private readonly OverlayLauncherWindow _launcherWindow;
    private FloatingFlashcardWindow? _flashcardWindow;
    private readonly Func<FloatingFlashcardWindow>? _flashcardWindowFactory;
    private bool _disposed;

    public event EventHandler? ShowSettingsRequested;
    public event EventHandler? OpenFlashcardsRequested;
    public event EventHandler? RestoreOverlayRequested;
    public event EventHandler<(string CardKey, FlashcardRating Rating)>? FlashcardRated;
    public event EventHandler<string>? CardMarkedDone;
    public event EventHandler? HideFlashcardRequested;

    public DisplayController(
        FloatingWindow floatingWindow,
        OverlayLauncherWindow? launcherWindow = null,
        FloatingFlashcardWindow? flashcardWindow = null,
        Func<FloatingFlashcardWindow>? flashcardWindowFactory = null)
    {
        _floatingWindow = floatingWindow;
        _launcherWindow = launcherWindow ?? new OverlayLauncherWindow();
        _flashcardWindow = flashcardWindow;
        _flashcardWindowFactory = flashcardWindowFactory;

        _launcherWindow.SettingsRequested += LauncherWindow_SettingsRequested;
        _launcherWindow.RestoreRequested += LauncherWindow_RestoreRequested;

        if (_flashcardWindow is not null)
        {
            AttachFlashcardWindowEvents(_flashcardWindow);
        }
    }

    public FloatingWindow FloatingWindow => _floatingWindow;
    public OverlayLauncherWindow LauncherWindow => _launcherWindow;
    public FloatingFlashcardWindow? FlashcardWindow => _flashcardWindow;

    public void Apply(AppSettings settings)
    {
        _floatingWindow.ApplySettings(settings, renderContent: settings.ShowSentenceOverlay);

        var sentenceVisible = settings.ShowSentenceOverlay;
        var flashcardVisible = false;

        if (settings.ShowFloatingFlashcard)
        {
            var deckKey = settings.ActiveFlashcardDeckKey;
            IReadOnlyList<FlashcardItem> deckCards = [];
            var deckName = string.Empty;
            var hasValidDeck = false;

            if (!string.IsNullOrWhiteSpace(deckKey))
            {
                var summaries = FlashcardDeckProjection.GetDeckSummaries(settings);
                var summary = summaries.FirstOrDefault(s => s.DeckKey == deckKey);
                if (summary is not null)
                {
                    hasValidDeck = true;
                    deckName = summary.Name;
                    deckCards = FlashcardDeckProjection.GetDeckCards(settings, deckKey, onlyActive: true);
                }
            }

            if (deckCards.Count > 0)
            {
                EnsureFlashcardWindow();
                var currentIndex = _flashcardWindow!.CurrentIndex;
                var targetIndex = Math.Clamp(currentIndex, 0, deckCards.Count - 1);
                _flashcardWindow!.SetDeck(deckName, deckCards, targetIndex);
                _flashcardWindow.Show();
                flashcardVisible = true;
            }
            else
            {
                ReleaseFlashcardWindow();
                flashcardVisible = false;
                if (!hasValidDeck)
                    OpenFlashcardsRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            ReleaseFlashcardWindow();
            flashcardVisible = false;
        }

        if (sentenceVisible)
        {
            _floatingWindow.Show();
        }
        else
        {
            _floatingWindow.Hide();
        }

        var launcherVisible = !sentenceVisible && !flashcardVisible;
        if (launcherVisible)
        {
            _launcherWindow.Show();
        }
        else
        {
            _launcherWindow.Hide();
        }
    }

    private void EnsureFlashcardWindow()
    {
        if (_flashcardWindow is not null)
            return;

        _flashcardWindow = _flashcardWindowFactory != null
            ? _flashcardWindowFactory()
            : new FloatingFlashcardWindow();

        AttachFlashcardWindowEvents(_flashcardWindow);
    }

    private void AttachFlashcardWindowEvents(FloatingFlashcardWindow window)
    {
        window.SettingsRequested += FlashcardWindow_SettingsRequested;
        window.CloseRequested += FlashcardWindow_CloseRequested;
        window.CardRated += FlashcardWindow_CardRated;
        window.CardMarkedDoneRequested += FlashcardWindow_CardMarkedDone;
    }

    private void DetachFlashcardWindowEvents(FloatingFlashcardWindow window)
    {
        window.SettingsRequested -= FlashcardWindow_SettingsRequested;
        window.CloseRequested -= FlashcardWindow_CloseRequested;
        window.CardRated -= FlashcardWindow_CardRated;
        window.CardMarkedDoneRequested -= FlashcardWindow_CardMarkedDone;
    }

    private void ReleaseFlashcardWindow()
    {
        if (_flashcardWindow is null)
            return;

        DetachFlashcardWindowEvents(_flashcardWindow);
        _flashcardWindow.Close();
        _flashcardWindow = null;
    }

    private void FlashcardWindow_SettingsRequested(object? sender, EventArgs e) =>
        ShowSettingsRequested?.Invoke(this, EventArgs.Empty);

    private void FlashcardWindow_CloseRequested(object? sender, EventArgs e) =>
        HideFlashcardRequested?.Invoke(this, EventArgs.Empty);

    private void FlashcardWindow_CardRated(object? sender, (string CardKey, FlashcardRating Rating) e) =>
        FlashcardRated?.Invoke(this, e);

    private void FlashcardWindow_CardMarkedDone(object? sender, string cardKey) =>
        CardMarkedDone?.Invoke(this, cardKey);

    private void LauncherWindow_SettingsRequested(object? sender, EventArgs e) =>
        ShowSettingsRequested?.Invoke(this, EventArgs.Empty);

    private void LauncherWindow_RestoreRequested(object? sender, EventArgs e) =>
        RestoreOverlayRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _launcherWindow.SettingsRequested -= LauncherWindow_SettingsRequested;
        _launcherWindow.RestoreRequested -= LauncherWindow_RestoreRequested;
        _launcherWindow.Close();

        if (_flashcardWindow is not null)
        {
            ReleaseFlashcardWindow();
        }
    }
}
