using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using MessageBox = System.Windows.MessageBox;
using ValidationResult = PteFloatingSentence.Core.ValidationResult;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

public partial class SetupPage : UserControl
{
    private StudyListDraft? _draft;
    private Action<ValidationResult>? _showResult;
    private Action? _onDraftChanged;
    private bool _isRendering;

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
                    StudyListList.SelectedItem = item;
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
                    SentenceList.SelectedItem = item;
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

    internal void AddSentenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommitListEdits is not null && !CommitListEdits().IsValid)
            return;

        var result = AddSentence?.Invoke(SentenceInput.Text) ?? new ValidationResult(true, null);
        _showResult?.Invoke(result);
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
