using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using PasswordBox = System.Windows.Controls.PasswordBox;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using RadioButton = System.Windows.Controls.RadioButton;
using PteFloatingSentence.Core;

namespace PteFloatingSentence.Windows;

public partial class GeminiPage : UserControl
{
    private bool _isKeyConfigured;

    public event Action? ClearKeyRequested;
    public event Action<AiProvider>? ProviderChanged;
    public event Action<AiProvider>? ClearProviderKeyRequested;

    private bool _loadingProvider;
    private bool _groqConfigured;
    private bool _claudeConfigured;

    public AiProvider SelectedProvider { get; private set; } = AiProvider.Gemini;
    public string? CurrentGroqKey => GroqKeyInput.Password?.Trim();
    public string? CurrentClaudeKey => ClaudeKeyInput.Password?.Trim();

    public string? CurrentKey => ApiKeyInput.Password?.Trim();
    public bool IsKeyConfigured => _isKeyConfigured;

    public GeminiPage()
    {
        InitializeComponent();
    }

    public void LoadState(bool isKeyConfigured)
    {
        _isKeyConfigured = isKeyConfigured;
        ApiKeyStatusLabel.Text = _isKeyConfigured ? "Key configured" : "No key configured";
        ClearApiKeyButton.IsEnabled = _isKeyConfigured;
    }

    public void ClearApiKey()
    {
        ApiKeyInput.Password = string.Empty;
        LoadState(false);
        ClearKeyRequested?.Invoke();
    }

    private void ClearApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        ClearApiKey();
    }

    /// <summary>Shows the selected provider and whether the Groq and Claude keys are saved.</summary>
    public void LoadProviderState(AiProvider provider, bool groqConfigured, bool claudeConfigured)
    {
        _loadingProvider = true;
        try
        {
            SelectedProvider = AiProviderRules.Normalize(provider);
            GeminiProviderButton.IsChecked = SelectedProvider == AiProvider.Gemini;
            GroqProviderButton.IsChecked = SelectedProvider == AiProvider.Groq;
            ClaudeProviderButton.IsChecked = SelectedProvider == AiProvider.Claude;
            _groqConfigured = groqConfigured;
            _claudeConfigured = claudeConfigured;
            GroqKeyStatusLabel.Text = groqConfigured ? "Key configured" : "No key configured";
            ClearGroqKeyButton.IsEnabled = groqConfigured;
            ClaudeKeyStatusLabel.Text = claudeConfigured ? "Key configured" : "No key configured";
            ClearClaudeKeyButton.IsEnabled = claudeConfigured;
            UpdateProviderWarning();
        }
        finally
        {
            _loadingProvider = false;
        }
    }

    private void ProviderButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingProvider)
            return;

        SelectedProvider = ReferenceEquals(sender, GroqProviderButton) ? AiProvider.Groq
            : ReferenceEquals(sender, ClaudeProviderButton) ? AiProvider.Claude
            : AiProvider.Gemini;
        UpdateProviderWarning();
        ProviderChanged?.Invoke(SelectedProvider);
    }

    private void UpdateProviderWarning()
    {
        var configured = SelectedProvider switch
        {
            AiProvider.Groq => _groqConfigured || !string.IsNullOrEmpty(CurrentGroqKey),
            AiProvider.Claude => _claudeConfigured || !string.IsNullOrEmpty(CurrentClaudeKey),
            _ => _isKeyConfigured || !string.IsNullOrEmpty(CurrentKey)
        };
        var name = SelectedProvider == AiProvider.Claude ? "Claude" : SelectedProvider.ToString();
        ProviderKeyWarningText.Text = configured ? string.Empty : $"Add a {name} API key below to use {name}.";
        ProviderKeyWarningText.Visibility = configured ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ProviderKey_PasswordChanged(object sender, RoutedEventArgs e) => UpdateProviderWarning();

    private void ClearGroqKeyButton_Click(object sender, RoutedEventArgs e)
    {
        GroqKeyInput.Password = string.Empty;
        LoadProviderState(SelectedProvider, groqConfigured: false, _claudeConfigured);
        ClearProviderKeyRequested?.Invoke(AiProvider.Groq);
    }

    private void ClearClaudeKeyButton_Click(object sender, RoutedEventArgs e)
    {
        ClaudeKeyInput.Password = string.Empty;
        LoadProviderState(SelectedProvider, _groqConfigured, claudeConfigured: false);
        ClearProviderKeyRequested?.Invoke(AiProvider.Claude);
    }
}
