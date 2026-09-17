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

    public async Task SaveAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            await ClearAsync();
            return;
        }

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var bytes = Encoding.UTF8.GetBytes(apiKey.Trim());
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(FilePath, encrypted);
    }

    public async Task<string?> LoadAsync()
    {
        if (!File.Exists(FilePath))
            return null;

        try
        {
            var encrypted = await File.ReadAllBytesAsync(FilePath);
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

    public Task ClearAsync()
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

        return Task.CompletedTask;
    }
}
