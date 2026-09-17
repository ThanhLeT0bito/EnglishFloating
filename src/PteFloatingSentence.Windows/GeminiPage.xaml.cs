using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using PasswordBox = System.Windows.Controls.PasswordBox;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace PteFloatingSentence.Windows;

public partial class GeminiPage : UserControl
{
    private bool _isKeyConfigured;

    public event Action? ClearKeyRequested;

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
}
