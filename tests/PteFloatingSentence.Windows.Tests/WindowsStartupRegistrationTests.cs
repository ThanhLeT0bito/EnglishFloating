using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public sealed class WindowsStartupRegistrationTests
{
    [TestMethod]
    public void Reconcile_DevelopmentHost_DoesNotCreateShortcut()
    {
        var store = new FakeStartupShortcutStore();
        var registration = new WindowsStartupRegistration(store);

        var result = registration.Reconcile(true, "dotnet");

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(store.CreatedShortcut);
    }

    [TestMethod]
    public void Reconcile_Disabled_RemovesOwnedShortcut()
    {
        var store = new FakeStartupShortcutStore();
        var registration = new WindowsStartupRegistration(store);

        var result = registration.Reconcile(false, @"C:\release\EnglishFloating.exe");

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(store.RemovedShortcut);
    }

    [TestMethod]
    public void IsPublishedExecutable_RejectsNonExecutablePath()
    {
        Assert.IsFalse(WindowsStartupRegistration.IsPublishedExecutable("dotnet"));
    }

    private sealed class FakeStartupShortcutStore : IStartupShortcutStore
    {
        public bool CreatedShortcut { get; private set; }
        public bool RemovedShortcut { get; private set; }

        public StartupRegistrationResult CreateOrUpdate(string shortcutFileName, string executablePath)
        {
            CreatedShortcut = true;
            return StartupRegistrationResult.Success;
        }

        public StartupRegistrationResult Remove(string shortcutFileName)
        {
            RemovedShortcut = true;
            return StartupRegistrationResult.Success;
        }
    }
}
