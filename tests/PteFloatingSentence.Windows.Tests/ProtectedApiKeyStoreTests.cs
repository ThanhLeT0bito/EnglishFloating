using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public sealed class ProtectedApiKeyStoreTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task KeyRoundTrip_SavesAndLoadsProtectedApiKey()
    {
        var dir = CreateTempDirectory();
        var store = new ProtectedApiKeyStore("PteFloatingSentence", dir);

        await store.SaveAsync("test-gemini-key-12345");
        var loaded = await store.LoadAsync();

        Assert.AreEqual("test-gemini-key-12345", loaded);

        // Verify the file does not contain plaintext key bytes
        var files = Directory.GetFiles(dir);
        Assert.IsTrue(files.Length > 0);
        foreach (var file in files)
        {
            var content = await File.ReadAllTextAsync(file);
            StringAssert.DoesNotMatch(content, new System.Text.RegularExpressions.Regex("test-gemini-key-12345"));
        }
    }

    [TestMethod]
    public async Task LoadAsync_ReturnsNull_WhenNoKeySaved()
    {
        var dir = CreateTempDirectory();
        var store = new ProtectedApiKeyStore("PteFloatingSentence", dir);

        var loaded = await store.LoadAsync();

        Assert.IsNull(loaded);
    }

    [TestMethod]
    public async Task ClearAsync_RemovesStoredKey()
    {
        var dir = CreateTempDirectory();
        var store = new ProtectedApiKeyStore("PteFloatingSentence", dir);

        await store.SaveAsync("key-to-clear");
        Assert.IsNotNull(await store.LoadAsync());

        await store.ClearAsync();
        Assert.IsNull(await store.LoadAsync());
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(TestContext.TestRunDirectory!, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
