using System.Windows;
using System.Windows.Controls;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;
using UserControl = System.Windows.Controls.UserControl;

namespace PteFloatingSentence.Windows;

public partial class ReviewPracticePage : UserControl, IDisposable
{
    private AppSettings? _settings;
    private Action<Guid, Guid, bool>? _onSentenceCompleted;
    private ReviewPracticeSession? _session;
    private readonly ISentenceAudioPlayback _audio;
    private int? _seed;
    private bool _initializing;
    private bool _isDisposed;

    public event Action<PracticeMode>? PracticeModeChanged;

    public ReviewPracticePage() : this(new SentenceAudioPlayback(new WpfAudioPlayer(), new EdgeNeuralTtsService(), new AudioCacheManager())) { }

    // The page owns the playback workflow, including an injected workflow.
    public ReviewPracticePage(ISentenceAudioPlayback audio)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        InitializeComponent();
        IsVisibleChanged += OnVisibilityChanged;
    }

    public void Initialize(AppSettings settings, Action<Guid, Guid, bool>? onSentenceCompleted = null, int? seed = null)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _onSentenceCompleted = onSentenceCompleted;
        _seed = seed;
        _initializing = true;
        PracticeModeSelector.SelectedIndex = settings.PracticeMode == PracticeMode.ListenAndWrite ? 1 : 0;
        StudyListSelector.Items.Clear();
        foreach (var list in settings.StudyLists)
            StudyListSelector.Items.Add(new ComboBoxItem { Content = list.Name, Tag = list.Id });
        StudyListSelector.SelectedIndex = Math.Max(0, settings.StudyLists.ToList().FindIndex(l => l.Id == settings.ActiveListId));
        _initializing = false;
        LoadList(settings.StudyLists.FirstOrDefault(l => l.Id == settings.ActiveListId)
            ?? settings.StudyLists.FirstOrDefault() ?? new StudyList(Guid.Empty, "", 10, 0, []));
    }

    public void LoadList(StudyList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _session = new ReviewPracticeSession(list, _seed, (id, completed) => _onSentenceCompleted?.Invoke(list.Id, id, completed));
        if (_session.IsAllSentencesCompleted && list.Sentences.Count > 0) _session.RestartList();
        SettingsPracticePanel.Load(_session, _settings ?? new AppSettings(), _audio);
    }

    private void StudyListSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _settings is null || StudyListSelector.SelectedItem is not ComboBoxItem { Tag: Guid id }) return;
        var list = _settings.StudyLists.FirstOrDefault(l => l.Id == id);
        if (list is not null) LoadList(list);
    }

    private void PracticeModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _settings is null) return;
        var mode = PracticeModeSelector.SelectedIndex == 1 ? PracticeMode.ListenAndWrite : PracticeMode.TextHints;
        _settings = _settings with { PracticeMode = mode };
        SettingsPracticePanel.UpdateMode(mode);
        PracticeModeChanged?.Invoke(mode);
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible) _audio.Stop();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        IsVisibleChanged -= OnVisibilityChanged;
        StudyListSelector.SelectionChanged -= StudyListSelector_SelectionChanged;
        PracticeModeSelector.SelectionChanged -= PracticeModeSelector_SelectionChanged;
        SettingsPracticePanel.Clear();
        _audio.Dispose();
    }
}
