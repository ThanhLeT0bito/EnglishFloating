using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AiProviderSettingsTests
{
    [TestMethod]
    public void SelectingGroqAndSaving_StoresKeyAndProvider()
    {
        WithStores((gemini, groq, claude) =>
        {
            AppSettings? saved = null;
            var window = new SettingsWindow(AppSettings.Default, s => saved = s, gemini, groqKeyStore: groq, claudeKeyStore: claude);
            var page = (GeminiPage)window.FindName("GeminiPageControl");

            ((RadioButton)page.FindName("GroqProviderButton")).IsChecked = true;
            ((PasswordBox)page.FindName("GroqKeyInput")).Password = "gsk-test-key";
            Save(window);

            Assert.IsNotNull(saved);
            Assert.AreEqual(AiProvider.Groq, saved!.AiProvider);
            Assert.IsTrue(saved.GroqApiKeyConfigured);
            Assert.IsFalse(saved.ClaudeApiKeyConfigured);
            Assert.AreEqual("gsk-test-key", groq.Load());
            Assert.IsNull(claude.Load());
        });
    }

    [TestMethod]
    public void ClearingClaudeKey_RemovesItOnSave()
    {
        WithStores((gemini, groq, claude) =>
        {
            claude.Save("sk-ant-old");
            AppSettings? saved = null;
            var initial = AppSettings.Default with { AiProvider = AiProvider.Claude, ClaudeApiKeyConfigured = true };
            var window = new SettingsWindow(initial, s => saved = s, gemini, groqKeyStore: groq, claudeKeyStore: claude);
            var page = (GeminiPage)window.FindName("GeminiPageControl");
            Assert.IsTrue(((RadioButton)page.FindName("ClaudeProviderButton")).IsChecked == true);
            Assert.AreEqual("Key configured", ((TextBlock)page.FindName("ClaudeKeyStatusLabel")).Text);

            ((Button)page.FindName("ClearClaudeKeyButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Save(window);

            Assert.IsFalse(saved!.ClaudeApiKeyConfigured);
            Assert.IsNull(claude.Load());
        });
    }

    [TestMethod]
    public void ProviderWithoutKey_ShowsWarning()
    {
        WithStores((gemini, groq, claude) =>
        {
            var window = new SettingsWindow(AppSettings.Default, _ => { }, gemini, groqKeyStore: groq, claudeKeyStore: claude);
            var page = (GeminiPage)window.FindName("GeminiPageControl");
            var warning = (TextBlock)page.FindName("ProviderKeyWarningText");

            ((RadioButton)page.FindName("ClaudeProviderButton")).IsChecked = true;
            Assert.AreEqual(Visibility.Visible, warning.Visibility);

            ((PasswordBox)page.FindName("ClaudeKeyInput")).Password = "sk-ant-new";
            Assert.AreEqual(Visibility.Collapsed, warning.Visibility);
        });
    }

    [TestMethod]
    public void KeyStores_UseSeparateFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var paths = new[]
        {
            new ProtectedApiKeyStore("PteFloatingSentence", dir).FilePath,
            new ProtectedApiKeyStore("PteFloatingSentence", dir, ProtectedApiKeyStore.GroqFileName).FilePath,
            new ProtectedApiKeyStore("PteFloatingSentence", dir, ProtectedApiKeyStore.ClaudeFileName).FilePath
        };

        Assert.AreEqual(3, paths.Distinct().Count());
        Assert.IsTrue(paths[0].EndsWith("gemini.dpapi"));
    }

    private static void Save(SettingsWindow window) =>
        typeof(SettingsWindow).GetMethod("SaveButton_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [window, new RoutedEventArgs()]);

    private static void WithStores(Action<ProtectedApiKeyStore, ProtectedApiKeyStore, ProtectedApiKeyStore> action)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            RunOnSta(() => action(
                new ProtectedApiKeyStore("PteFloatingSentence", dir),
                new ProtectedApiKeyStore("PteFloatingSentence", dir, ProtectedApiKeyStore.GroqFileName),
                new ProtectedApiKeyStore("PteFloatingSentence", dir, ProtectedApiKeyStore.ClaudeFileName)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
