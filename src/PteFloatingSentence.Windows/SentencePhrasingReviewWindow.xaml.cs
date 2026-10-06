using System.Windows;
using System.Windows.Controls;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows;

public partial class SentencePhrasingReviewWindow : Window
{
    private readonly string _original;
    private readonly ISentencePhraser _phraser;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _isLoading;
    private bool _loaded;
    private bool _closed;

    public string? ConfirmedGroups { get; private set; }

    public SentencePhrasingReviewWindow(string original, ISentencePhraser phraser)
    {
        _original = original;
        _phraser = phraser;
        InitializeComponent();
        OriginalText.Text = original;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await LoadProposalAsync();

    public async Task LoadProposalAsync()
    {
        if (_isLoading || _loaded || _closed) return;
        _isLoading = true;
        RetryButton.Visibility = Visibility.Collapsed;
        StatusText.Text = "Checking spelling and finding natural reading groups…";
        try
        {
            var groups = await _phraser.SuggestAsync(_original, _cancellation.Token);
            if (_closed) return;
            _loaded = true;
            RetryButton.Visibility = Visibility.Collapsed;
            GroupsInput.IsEnabled = true;
            GroupsInput.Text = string.Join("\n", groups);
            RefreshPreview();
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception error)
        {
            if (!_closed)
            {
                RetryButton.Visibility = Visibility.Visible;
                StatusText.Text = error is InvalidOperationException ? error.Message : "Could not analyze this sentence. Click Retry or Cancel.";
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async void Retry_Click(object sender, RoutedEventArgs e) => await LoadProposalAsync();

    private void GroupsInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loaded) RefreshPreview();
    }

    private void RefreshPreview()
    {
        var proposal = SentencePhrasing.ParseGroups(GroupsInput.Text);
        ConfirmButton.IsEnabled = proposal.Validation.IsValid;
        StatusText.Text = proposal.Validation.Error ?? "Nothing is added until you confirm.";
        PreviewText.Text = SentencePhrasing.GetDisplayText(proposal.Text, proposal.BreakAfterWordIndices);
        CorrectionNote.Text = proposal.Text == VocabularyRules.NormalizePhrase(_original)
            ? "Wording is unchanged. Review the reading groups."
            : "Wording or punctuation changed. Compare with your original before confirming.";
    }

    public bool TryConfirm()
    {
        if (!_loaded || _closed) return false;
        var proposal = SentencePhrasing.ParseGroups(GroupsInput.Text);
        if (!proposal.Validation.IsValid)
        {
            StatusText.Text = proposal.Validation.Error;
            return false;
        }
        ConfirmedGroups = GroupsInput.Text;
        return true;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (TryConfirm()) DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _cancellation.Cancel();
        _cancellation.Dispose();
        base.OnClosed(e);
    }
}
