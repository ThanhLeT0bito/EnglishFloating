using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PteFloatingSentence.Windows.Infrastructure;

public sealed class ProtectedApiKeyStore
{
    public ProtectedApiKeyStore(string applicationName = "PteFloatingSentence", string? storeDirectory = null)
    {
        var dir = storeDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            applicationName);
        FilePath = Path.Combine(dir, "gemini.dpapi");
    }

    public string FilePath { get; }

    public void Save(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Clear();
            return;
        }

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var bytes = Encoding.UTF8.GetBytes(apiKey.Trim());
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, encrypted);
    }

    public Task SaveAsync(string apiKey)
    {
        Save(apiKey);
        return Task.CompletedTask;
    }

    public string? Load()
    {
        if (!File.Exists(FilePath))
            return null;

        try
        {
            var encrypted = File.ReadAllBytes(FilePath);
            if (encrypted.Length == 0)
                return null;

            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public Task<string?> LoadAsync() => Task.FromResult(Load());

    public void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public Task ClearAsync()
    {
        Clear();
        return Task.CompletedTask;
    }
}
