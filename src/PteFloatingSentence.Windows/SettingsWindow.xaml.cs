using System.Windows;
using System.Windows.Controls;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

public partial class SettingsWindow : Window
{
    private readonly StudyListDraft _draft;
    private bool _isRendering;

    public SettingsWindow(AppSettings initial, Action<AppSettings> save)
    {
        InitializeComponent();
        _draft = new StudyListDraft(initial, save);
        RefreshUi();
    }

    private void NewListButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryUpdateSelectedList())
            return;

        _draft.CreateList();
        RefreshUi();
    }

    private void StudyListList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRendering || StudyListList.SelectedItem is not ListBoxItem item || item.Tag is not Guid listId)
            return;

        if (!TryUpdateSelectedList())
        {
            RefreshUi();
            return;
        }

        _draft.SelectList(listId);
        RefreshUi();
    }

    private void MakeActiveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryUpdateSelectedList())
            return;

        _draft.MakeSelectedListActive();
        RefreshUi();
    }

    private void DeleteListButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryUpdateSelectedList())
            return;

        if (System.Windows.MessageBox.Show(this, $"Delete '{_draft.SelectedList.Name}'?", "Delete study list", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        ShowResult(_draft.DeleteSelectedList());
        RefreshUi();
    }

    private void SentenceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRendering || SentenceList.SelectedItem is not ListBoxItem item || item.Tag is not Guid sentenceId)
            return;

        _draft.SelectSentence(sentenceId);
        SentenceInput.Text = _draft.SelectedList.Sentences.Single(sentence => sentence.Id == sentenceId).Text;
        ValidationMessage.Text = string.Empty;
    }

    private void AddSentenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryUpdateSelectedList())
            return;

        var result = _draft.AddSentence(SentenceInput.Text);
        ShowResult(result);
        if (result.IsValid)
            RefreshUi();
    }

    private void UpdateSentenceButton_Click(object sender, RoutedEventArgs e)
    {
        var result = _draft.UpdateSelectedSentence(SentenceInput.Text);
        ShowResult(result);
        if (result.IsValid)
            RefreshUi();
    }

    private void DeleteSentenceButton_Click(object sender, RoutedEventArgs e)
    {
        _draft.DeleteSelectedSentence();
        ValidationMessage.Text = string.Empty;
        RefreshUi();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryUpdateSelectedList())
            return;

        var result = _draft.Save();
        ShowResult(result);
        if (result.IsValid)
            Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _draft.Cancel();
        Close();
    }

    private bool TryUpdateSelectedList()
    {
        if (!int.TryParse(TargetInput.Text, out var target))
        {
            ValidationMessage.Text = "Target must be a whole number.";
            return false;
        }

        _draft.UpdateSelectedList(ListNameInput.Text, target);
        ValidationMessage.Text = string.Empty;
        return true;
    }

    private void RefreshUi()
    {
        _isRendering = true;
        try
        {
            StudyListList.Items.Clear();
            foreach (var list in _draft.Settings.StudyLists)
            {
                var item = new ListBoxItem
                {
                    Tag = list.Id,
                    Content = FormatListLabel(list)
                };
                StudyListList.Items.Add(item);
                if (list.Id == _draft.SelectedListId)
                    StudyListList.SelectedItem = item;
            }

            var selected = _draft.SelectedList;
            ListNameInput.Text = selected.Name;
            TargetInput.Text = selected.TargetSentenceCount.ToString();
            ActiveListLabel.Text = _draft.Settings.ActiveListId == selected.Id
                ? "This list is active"
                : "Choose Make active to show this list in the widget";

            SentenceList.Items.Clear();
            for (var index = 0; index < selected.Sentences.Count; index++)
            {
                var sentence = selected.Sentences[index];
                var item = new ListBoxItem { Tag = sentence.Id, Content = $"{index + 1}. {sentence.Text}" };
                SentenceList.Items.Add(item);
                if (sentence.Id == _draft.SelectedSentenceId)
                    SentenceList.SelectedItem = item;
            }

            SentenceInput.Text = _draft.SelectedSentenceId is Guid selectedSentenceId
                ? selected.Sentences.Single(sentence => sentence.Id == selectedSentenceId).Text
                : string.Empty;
        }
        finally
        {
            _isRendering = false;
        }
    }

    private void ShowResult(PteFloatingSentence.Core.ValidationResult result) => ValidationMessage.Text = result.IsValid ? string.Empty : result.Error;

    private static string FormatListLabel(StudyList list) =>
        $"{list.Name} · {list.Sentences.Count} sentence{(list.Sentences.Count == 1 ? string.Empty : "s")}";
}
