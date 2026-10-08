using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;
using UserControl = System.Windows.Controls.UserControl;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Slider = System.Windows.Controls.Slider;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace PteFloatingSentence.Windows;

public sealed record VoiceOption(string VoiceId, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public partial class DisplayPage : UserControl
{
    public static readonly IReadOnlyList<VoiceOption> AvailableVoices =
    [
        new("random", "🎲 Random (Shuffle voices & accents)"),
        new("en-US-JennyNeural", "US - Jenny - Female"),
        new("en-US-GuyNeural", "US - Guy - Male"),
        new("en-AU-NatashaNeural", "AU - Natasha - Female - PTE"),
        new("en-AU-WilliamNeural", "AU - William - Male - PTE"),
        new("en-GB-SoniaNeural", "UK - Sonia - Female")
    ];

    private readonly IAudioCacheManager _audioCacheManager;
    private bool _isRendering;
    private string? _activeDeckKey;
    private IReadOnlyList<FlashcardDeckSummary> _availableDecks = [];
    private string _activeVoice = "en-US-JennyNeural";

    public bool ShowSentenceOverlay => ShowSentenceOverlayInput.IsChecked == true;
    public bool ShowVocabularyCards => ShowVocabularyCardsInput.IsChecked == true;
    public bool ShowFloatingFlashcard => ShowFloatingFlashcardInput.IsChecked == true;
    public bool LaunchAtWindowsSignIn => LaunchAtWindowsSignInInput.IsChecked == true;
    public string? ActiveFlashcardDeckKey => _activeDeckKey;

    public string TtsVoice
    {
        get => GetSelectedVoice();
        set => SelectVoice(value);
    }

    public double TtsSpeed
    {
        get => SpeedSlider?.Value ?? 1.0;
        set
        {
            if (SpeedSlider is not null)
            {
                SpeedSlider.Value = value;
                if (SpeedValueLabel is not null)
                {
                    SpeedValueLabel.Text = $"{value.ToString("0.00", CultureInfo.InvariantCulture)}x";
                }
            }
        }
    }

    public int PracticeAudioDelaySeconds
    {
        get => AudioCountdownSlider is null
            ? PracticeAudioDelay.DefaultSeconds
            : PracticeAudioDelay.Normalize((int)Math.Round(AudioCountdownSlider.Value));
        set
        {
            var seconds = PracticeAudioDelay.Normalize(value);
            if (AudioCountdownSlider is not null)
                AudioCountdownSlider.Value = seconds;
            if (AudioCountdownValueLabel is not null)
                AudioCountdownValueLabel.Text = $"{seconds}s";
        }
    }

    public IAudioCacheManager AudioCacheManager => _audioCacheManager;

    public event Action<bool, bool>? DisplayPreferencesChanged;
    public event Action<bool, bool, bool, string?>? FullDisplayPreferencesChanged;
    public event Action<bool>? LaunchAtWindowsSignInChanged;
    public event Action<string, double>? TtsPreferencesChanged;
    public event Action<int>? PracticeAudioDelayChanged;

    public DisplayPage() : this(new AudioCacheManager())
    {
    }

    public DisplayPage(IAudioCacheManager audioCacheManager)
    {
        _audioCacheManager = audioCacheManager ?? throw new ArgumentNullException(nameof(audioCacheManager));
        InitializeComponent();
        InitializeVoiceComboBox();
        UpdateCacheSizeDisplay();
    }

    private void InitializeVoiceComboBox()
    {
        VoiceAccentComboBox.ItemsSource = AvailableVoices;
        SelectVoice(_activeVoice);
    }

    public void LoadSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        LoadPreferences(
            settings.ShowSentenceOverlay,
            settings.ShowVocabularyCards,
            settings.ShowFloatingFlashcard,
            settings.ActiveFlashcardDeckKey,
            FlashcardDeckProjection.GetDeckSummaries(settings),
            settings.LaunchAtWindowsSignIn,
            settings.TtsVoice,
            settings.TtsSpeed,
            settings.PracticeAudioDelaySeconds);
    }

    public AppSettings ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings with
        {
            ShowSentenceOverlay = ShowSentenceOverlay,
            ShowVocabularyCards = ShowVocabularyCards,
            ShowFloatingFlashcard = ShowFloatingFlashcard,
            ActiveFlashcardDeckKey = ActiveFlashcardDeckKey,
            LaunchAtWindowsSignIn = LaunchAtWindowsSignIn,
            TtsVoice = TtsVoice,
            TtsSpeed = TtsSpeed,
            PracticeAudioDelaySeconds = PracticeAudioDelaySeconds
        };
    }

    public void LoadPreferences(
        bool showSentenceOverlay,
        bool showVocabularyCards,
        bool showFloatingFlashcard = false,
        string? activeDeckKey = null,
        IReadOnlyList<FlashcardDeckSummary>? availableDecks = null,
        bool launchAtWindowsSignIn = true,
        string? ttsVoice = null,
        double? ttsSpeed = null,
        int? practiceAudioCountdownSeconds = null)
    {
        _isRendering = true;
        try
        {
            ShowSentenceOverlayInput.IsChecked = showSentenceOverlay;
            ShowVocabularyCardsInput.IsChecked = showVocabularyCards;
            ShowFloatingFlashcardInput.IsChecked = showFloatingFlashcard;
            LaunchAtWindowsSignInInput.IsChecked = launchAtWindowsSignIn;

            _availableDecks = availableDecks ?? [];
            _activeDeckKey = activeDeckKey;

            UpdateDeckComboBox();
            FloatingDeckSelectionPanel.IsEnabled = showFloatingFlashcard;

            _activeVoice = ttsVoice ?? _activeVoice;
            SelectVoice(_activeVoice);

            if (ttsSpeed.HasValue && SpeedSlider is not null)
            {
                SpeedSlider.Value = ttsSpeed.Value;
                if (SpeedValueLabel is not null)
                {
                    SpeedValueLabel.Text = $"{ttsSpeed.Value.ToString("0.00", CultureInfo.InvariantCulture)}x";
                }
            }

            if (practiceAudioCountdownSeconds.HasValue)
            {
                PracticeAudioDelaySeconds = practiceAudioCountdownSeconds.Value;
            }

            UpdateCacheSizeDisplay();
        }
        finally
        {
            _isRendering = false;
        }
    }

    private void UpdateDeckComboBox()
    {
        var items = _availableDecks.Select(d => new DisplayDeckItem(
            d.DeckKey,
            d.Name,
            d.IsCustom ? "(Custom)" : "(Study List)"
        )).ToList();

        FloatingDeckComboBox.ItemsSource = items;

        if (items.Count > 0)
        {
            var selectedIndex = items.FindIndex(i => i.DeckKey == _activeDeckKey);
            FloatingDeckComboBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
            if (selectedIndex < 0 && items.Count > 0)
            {
                _activeDeckKey = items[0].DeckKey;
            }
        }
        else
        {
            FloatingDeckComboBox.SelectedIndex = -1;
            _activeDeckKey = null;
        }
    }

    private string GetSelectedVoice()
    {
        if (VoiceAccentComboBox.SelectedValue is string val && !string.IsNullOrWhiteSpace(val))
            return val;
        if (VoiceAccentComboBox.SelectedItem is VoiceOption opt)
            return opt.VoiceId;
        if (VoiceAccentComboBox.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
            return tag;
        return _activeVoice;
    }

    private void SelectVoice(string? voice)
    {
        var target = string.IsNullOrWhiteSpace(voice) ? "en-US-JennyNeural" : voice;
        _activeVoice = target;

        if (VoiceAccentComboBox is null) return;

        VoiceAccentComboBox.SelectedValue = target;
        if (VoiceAccentComboBox.SelectedIndex < 0)
        {
            for (int i = 0; i < VoiceAccentComboBox.Items.Count; i++)
            {
                var item = VoiceAccentComboBox.Items[i];
                if (item is VoiceOption opt && string.Equals(opt.VoiceId, target, StringComparison.OrdinalIgnoreCase))
                {
                    VoiceAccentComboBox.SelectedIndex = i;
                    return;
                }
            }
            if (VoiceAccentComboBox.Items.Count > 0)
            {
                var defaultIdx = AvailableVoices.ToList().FindIndex(v => v.VoiceId == "en-US-JennyNeural");
                VoiceAccentComboBox.SelectedIndex = defaultIdx >= 0 ? defaultIdx : 0;
            }
        }
    }

    public void UpdateCacheSizeDisplay()
    {
        if (CacheSizeLabel is null) return;

        var totalBytes = _audioCacheManager.GetTotalCacheSizeBytes();
        var currentMb = totalBytes / (1024.0 * 1024.0);
        var maxMb = Infrastructure.AudioCacheManager.DefaultMaxCacheSizeBytes / (1024.0 * 1024.0);

        CacheSizeLabel.Text = $"Cache size: {currentMb.ToString("0.0", CultureInfo.InvariantCulture)} MB / {maxMb.ToString("0.#", CultureInfo.InvariantCulture)} MB";
    }

    private void ClearAudioCacheButton_Click(object sender, RoutedEventArgs e)
    {
        _audioCacheManager.ClearCache();
        UpdateCacheSizeDisplay();
    }

    private void OnPreferenceChanged(object sender, RoutedEventArgs e)
    {
        NotifyPreferencesChanged();
    }

    private void FloatingDeckComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRendering) return;

        if (FloatingDeckComboBox.SelectedItem is DisplayDeckItem item)
        {
            _activeDeckKey = item.DeckKey;
            NotifyPreferencesChanged();
        }
    }

    private void VoiceAccentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRendering) return;

        if (VoiceAccentComboBox.SelectedItem is VoiceOption voiceOption)
        {
            _activeVoice = voiceOption.VoiceId;
        }
        else if (VoiceAccentComboBox.SelectedValue is string voiceId && !string.IsNullOrWhiteSpace(voiceId))
        {
            _activeVoice = voiceId;
        }

        NotifyPreferencesChanged();
    }

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SpeedValueLabel is not null)
        {
            SpeedValueLabel.Text = $"{e.NewValue.ToString("0.00", CultureInfo.InvariantCulture)}x";
        }

        NotifyPreferencesChanged();
    }

    private void AudioCountdownSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AudioCountdownValueLabel is not null)
        {
            AudioCountdownValueLabel.Text = $"{PracticeAudioDelay.Normalize((int)Math.Round(e.NewValue))}s";
        }

        if (_isRendering) return;
        PracticeAudioDelayChanged?.Invoke(PracticeAudioDelaySeconds);
    }

    private void NotifyPreferencesChanged()
    {
        if (_isRendering) return;

        FloatingDeckSelectionPanel.IsEnabled = ShowFloatingFlashcard;

        DisplayPreferencesChanged?.Invoke(ShowSentenceOverlay, ShowVocabularyCards);
        FullDisplayPreferencesChanged?.Invoke(ShowSentenceOverlay, ShowVocabularyCards, ShowFloatingFlashcard, _activeDeckKey);
        LaunchAtWindowsSignInChanged?.Invoke(LaunchAtWindowsSignIn);
        TtsPreferencesChanged?.Invoke(TtsVoice, TtsSpeed);
    }

    private sealed record DisplayDeckItem(string DeckKey, string Name, string TypeBadge);
}
