using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using UserControl = System.Windows.Controls.UserControl;
using MessageBox = System.Windows.MessageBox;
using ValidationResult = PteFloatingSentence.Core.ValidationResult;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows;

public partial class SetupPage : UserControl
{
    private StudyListDraft? _draft;
    private Action<ValidationResult>? _showResult;
    private Action? _onDraftChanged;
    private bool _isRendering;
    private bool _settingInput;
    private CancellationTokenSource? _splitting;
    public ISentencePhraser? SentencePhraser { get; set; }

    /// <summary>AI that splits pasted text into clean sentences (the "AI split" button); null without an AI key.</summary>
    public ISentenceSplitter? AiSplitter { get; set; }

    public Func<ValidationResult>? CommitListEdits { get; set; }
    public Action<Guid>? SelectList { get; set; }
    public Action<Guid>? SelectSentence { get; set; }
    public Func<string, ValidationResult>? AddSentence { get; set; }
    public Func<string, ValidationResult>? UpdateSentence { get; set; }
    public Action? DeleteSentence { get; set; }
    public Action? CreateList { get; set; }
    public Action? MakeActive { get; set; }
    public Func<ValidationResult>? DeleteList { get; set; }
    public event EventHandler<Guid>? StartPracticeRequested;

    public SetupPage()
    {
        InitializeComponent();
    }

    public void Initialize(StudyListDraft draft, Action<ValidationResult> showResult, Action? onDraftChanged = null)
    {
        _draft = draft;
        _showResult = showResult;
        _onDraftChanged = onDraftChanged;

        CommitListEdits = TryCommitListEditsInternal;
        SelectList = id =>
        {
            _draft.SelectList(id);
            _onDraftChanged?.Invoke();
            RefreshFromDraft(_draft);
        };
        SelectSentence = id =>
        {
            _draft.SelectSentence(id);
            SentenceInput.Text = _draft.SelectedList.Sentences.Single(s => s.Id == id).Text;
            _showResult?.Invoke(new ValidationResult(true, null));
        };
        AddSentence = text =>
        {
            var res = _draft.AddSentence(text);
            if (res.IsValid)
            {
                _onDraftChanged?.Invoke();
                RefreshFromDraft(_draft);
            }
            return res;
        };
        UpdateSentence = text =>
        {
            var res = _draft.UpdateSelectedSentence(text);
            if (res.IsValid)
            {
                _onDraftChanged?.Invoke();
                RefreshFromDraft(_draft);
            }
            return res;
        };
        DeleteSentence = () =>
        {
            _draft.DeleteSelectedSentence();
            _showResult?.Invoke(new ValidationResult(true, null));
            _onDraftChanged?.Invoke();
            RefreshFromDraft(_draft);
        };
        CreateList = () =>
        {
            _draft.CreateList();
            _onDraftChanged?.Invoke();
            RefreshFromDraft(_draft);
        };
        MakeActive = () =>
        {
            _draft.MakeSelectedListActive();
            _onDraftChanged?.Invoke();
            RefreshFromDraft(_draft);
        };
        DeleteList = () =>
        {
            var res = _draft.DeleteSelectedList();
            if (res.IsValid)
            {
                _onDraftChanged?.Invoke();
                RefreshFromDraft(_draft);
            }
            return res;
        };
    }

    public void RefreshFromDraft(StudyListDraft draft)
    {
        _draft = draft;
        _isRendering = true;
        try
        {
            StudyListList.Items.Clear();
            foreach (var list in draft.Settings.StudyLists)
            {
                var item = new ListBoxItem
                {
                    Tag = list.Id,
                    Content = FormatListLabel(list, list.Id == draft.Settings.ActiveListId)
                };
                StudyListList.Items.Add(item);
                if (list.Id == draft.SelectedListId)
                {
                    StudyListList.SelectedItem = item;
                    StudyListList.ScrollIntoView(item);
                }
            }

            var selected = draft.SelectedList;
            DeleteListButton.IsEnabled = draft.Settings.StudyLists.Count > 1;
            ListNameInput.Text = selected.Name;
            TargetInput.Text = selected.TargetSentenceCount.ToString();
            ActiveListLabel.Text = draft.Settings.ActiveListId == selected.Id
                ? "This list is active"
                : "Choose Make active to show this list in the widget";

            SentenceList.Items.Clear();
            for (var index = 0; index < selected.Sentences.Count; index++)
            {
                var sentence = selected.Sentences[index];
                var item = new ListBoxItem { Tag = sentence.Id, Content = $"{index + 1}. {sentence.Text}" };
                SentenceList.Items.Add(item);
                if (sentence.Id == draft.SelectedSentenceId)
                {
                    SentenceList.SelectedItem = item;
                    SentenceList.ScrollIntoView(item);
                }
            }

            SentenceInput.Text = draft.SelectedSentenceId is Guid selectedSentenceId
                ? selected.Sentences.SingleOrDefault(sentence => sentence.Id == selectedSentenceId)?.Text ?? string.Empty
                : string.Empty;
        }
        finally
        {
            _isRendering = false;
        }
    }

    public ValidationResult TryCommitListEditsInternal()
    {
        if (_draft is null)
            return new ValidationResult(true, null);

        if (!int.TryParse(TargetInput.Text, out var target))
        {
            var err = new ValidationResult(false, "Target must be a whole number.");
            _showResult?.Invoke(err);
            return err;
        }

        _draft.UpdateSelectedList(ListNameInput.Text, target);
        var success = new ValidationResult(true, null);
        _showResult?.Invoke(success);
        return success;
    }

    internal void NewListButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommitListEdits is not null && !CommitListEdits().IsValid)
            return;

        CreateList?.Invoke();
    }

    internal void StudyListList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRendering || StudyListList.SelectedItem is not ListBoxItem item || item.Tag is not Guid listId)
            return;

        if (CommitListEdits is not null && !CommitListEdits().IsValid)
        {
            RestoreSelectedListSelection();
            return;
        }

        SelectList?.Invoke(listId);
    }

    internal void MakeActiveButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommitListEdits is not null && !CommitListEdits().IsValid)
            return;

        MakeActive?.Invoke();
    }

    internal void DeleteListButton_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null) return;
        if (CommitListEdits is not null && !CommitListEdits().IsValid)
            return;

        var parentWindow = Window.GetWindow(this);
        var message = $"Delete '{_draft.SelectedList.Name}'?";
        var confirm = parentWindow is not null
            ? MessageBox.Show(parentWindow, message, "Delete study list", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            : MessageBox.Show(message, "Delete study list", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        var result = DeleteList?.Invoke() ?? new ValidationResult(true, null);
        _showResult?.Invoke(result);
    }

    internal void SentenceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRendering || SentenceList.SelectedItem is not ListBoxItem item || item.Tag is not Guid sentenceId)
            return;

        SelectSentence?.Invoke(sentenceId);
    }

    /// <summary>
    /// Adds what is in the box: one sentence as before, or — when several are pasted (one per line, a numbered list
    /// or a paragraph) — each of them. Sentences that cannot be added stay in the box to fix.
    /// </summary>
    internal void AddSentenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommitListEdits is not null && !CommitListEdits().IsValid)
            return;

        var sentences = PteFloatingSentence.Core.SentenceSplitter.Split(SentenceInput.Text);
        if (sentences.Count <= 1 || _draft is null)
        {
            var result = AddSentence?.Invoke(sentences.Count == 1 ? sentences[0] : SentenceInput.Text) ?? new ValidationResult(true, null);
            _showResult?.Invoke(result);
            if (result.IsValid)
                ShowAddStatus(null);
            return;
        }

        var outcome = _draft.AddSentences(sentences);
        if (outcome.Added > 0)
        {
            _draft.ClearSentenceSelection();
            _onDraftChanged?.Invoke();
            RefreshFromDraft(_draft);
            if (SentenceList.Items.Count > 0)
                SentenceList.ScrollIntoView(SentenceList.Items[^1]);
        }

        SetInput(string.Join(Environment.NewLine, outcome.Rejected));
        var message = outcome.Message;
        if (outcome.Added == 0 && outcome.Rejected.Count > 0)
        {
            _showResult?.Invoke(new ValidationResult(false, message));
            ShowAddStatus(null);
        }
        else
        {
            _showResult?.Invoke(new ValidationResult(true, null));
            ShowAddStatus(message, outcome.Rejected.Count > 0);
        }
    }

    private void SentenceInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        RenderAddButtons();
        // The result of the last add or AI split no longer describes what is in the box.
        if (!_settingInput && !_isRendering)
            ShowAddStatus(null);
    }

    /// <summary>"＋ Add 5 sentences" when several are in the box; AI phrasing works on one sentence only.</summary>
    private void RenderAddButtons()
    {
        if (AddSentenceButton is null)
            return;

        var count = PteFloatingSentence.Core.SentenceSplitter.Split(SentenceInput.Text).Count;
        AddSentenceButton.Content = count > 1 ? $"＋ Add {count} sentences" : "＋ Add sentence";
        AddWithAiButton.IsEnabled = count <= 1 && _splitting is null;
        SentencePlaceholder.Visibility = string.IsNullOrEmpty(SentenceInput.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetInput(string text)
    {
        _settingInput = true;
        try
        {
            SentenceInput.Text = text;
        }
        finally
        {
            _settingInput = false;
        }
    }

    private void ShowAddStatus(string? text, bool warning = false)
    {
        AddStatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        AddStatusText.Text = text ?? string.Empty;
        AddStatusText.Foreground = new SolidColorBrush(warning ? Color.FromRgb(0xFB, 0xBF, 0x24) : Color.FromRgb(0x34, 0xD3, 0x99));
    }

    /// <summary>Lets the AI split pasted text into clean sentences, one per line, to check before adding.</summary>
    private async void AiSplitButton_Click(object sender, RoutedEventArgs e) => await SplitWithAiAsync();

    public async Task SplitWithAiAsync()
    {
        if (AiSplitter is null)
        {
            _showResult?.Invoke(new ValidationResult(false, "AI is unavailable. Add an API key on the AI page."));
            return;
        }

        var text = SentenceInput.Text;
        if (!text.Any(char.IsLetter))
        {
            _showResult?.Invoke(new ValidationResult(false, AiSentenceSplitter.EmptyMessage));
            return;
        }

        _splitting?.Cancel();
        var splitting = new CancellationTokenSource();
        _splitting = splitting;
        AiSplitButton.IsEnabled = false;
        AiSplitButton.Content = "✨ Splitting…";
        RenderAddButtons();
        _showResult?.Invoke(new ValidationResult(true, null));
        try
        {
            var sentences = await AiSplitter.SplitAsync(text, splitting.Token);
            if (!ReferenceEquals(_splitting, splitting))
                return;

            SetInput(string.Join(Environment.NewLine, sentences));
            ShowAddStatus(sentences.Count == 1
                ? "AI found 1 sentence. Check it, then click Add."
                : $"AI found {sentences.Count} sentences, one per line. Check them, then click Add {sentences.Count} sentences.");
        }
        catch (OperationCanceledException) when (splitting.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (ReferenceEquals(_splitting, splitting))
                _showResult?.Invoke(new ValidationResult(false, error is InvalidOperationException && !string.IsNullOrWhiteSpace(error.Message)
                    ? error.Message
                    : "Could not split the text. Please try again."));
        }
        finally
        {
            if (ReferenceEquals(_splitting, splitting))
            {
                _splitting = null;
                AiSplitButton.IsEnabled = true;
                AiSplitButton.Content = "✨ AI split";
                RenderAddButtons();
            }

            splitting.Dispose();
        }
    }

    private void AddWithAiButton_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null || (CommitListEdits is not null && !CommitListEdits().IsValid)) return;
        var validation = SentenceValidator.Validate(SentenceInput.Text);
        if (!validation.IsValid)
        {
            _showResult?.Invoke(validation);
            return;
        }
        if (SentencePhraser is null)
        {
            _showResult?.Invoke(new(false, "Gemini is unavailable. Check the API key in Settings."));
            return;
        }
        var review = new SentencePhrasingReviewWindow(SentenceInput.Text, SentencePhraser) { Owner = Window.GetWindow(this) };
        if (review.ShowDialog() != true || review.ConfirmedGroups is null) return;
        var result = _draft.AddPhrasedSentence(review.ConfirmedGroups);
        _showResult?.Invoke(result);
        if (result.IsValid)
        {
            _onDraftChanged?.Invoke();
            RefreshFromDraft(_draft);
        }
    }

    internal void UpdateSentenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommitListEdits is not null && !CommitListEdits().IsValid)
            return;

        var result = UpdateSentence?.Invoke(SentenceInput.Text) ?? new ValidationResult(true, null);
        _showResult?.Invoke(result);
    }

    internal void DeleteSentenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommitListEdits is not null && !CommitListEdits().IsValid)
            return;

        DeleteSentence?.Invoke();
    }

    internal void StartPracticeButton_Click(object sender, RoutedEventArgs e)
    {
        var commitRes = CommitListEdits?.Invoke() ?? new ValidationResult(true, null);
        if (!commitRes.IsValid)
        {
            _showResult?.Invoke(commitRes);
            return;
        }

        if (_draft is null) return;
        StartPracticeRequested?.Invoke(this, _draft.SelectedListId);
    }

    private void RestoreSelectedListSelection()
    {
        if (_draft is null) return;
        _isRendering = true;
        try
        {
            StudyListList.SelectedItem = StudyListList.Items.OfType<ListBoxItem>()
                .SingleOrDefault(item => item.Tag is Guid listId && listId == _draft.SelectedListId);
        }
        finally
        {
            _isRendering = false;
        }
    }

    private static string FormatListLabel(StudyList list, bool isActive) =>
        $"{list.Name} · {list.Sentences.Count} sentence{(list.Sentences.Count == 1 ? string.Empty : "s")}{(isActive ? " · Active" : string.Empty)}";
}
