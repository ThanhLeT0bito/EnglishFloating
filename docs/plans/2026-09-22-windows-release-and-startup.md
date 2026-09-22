# Windows Release and Startup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish a self-contained Windows executable and start exactly one EnglishFloating instance at Windows sign-in.

**Architecture:** Persist a startup preference in `AppSettings`; use a small Windows-only service to own one current-user Startup shortcut, and a mutex service to reject duplicate processes. Reuse the current Display settings/draft save flow. A PowerShell script publishes win-x64 self-contained single-file output.

**Tech Stack:** .NET 9, WPF, MSTest, Windows Startup folder, Windows Shell shortcut COM, `dotnet publish`.

## Global Constraints

- Publish to `C:\Dev\PteFloatingSentence\release\EnglishFloating.exe` as win-x64, self-contained, single-file.
- `LaunchAtWindowsSignIn` defaults to `true`, including when legacy JSON omits it.
- No admin rights, Run registry key, Task Scheduler, or machine-wide state.
- Do not create a startup shortcut from a test host or `dotnet run`.
- A duplicate launch must exit before creating a second overlay.

---

## File Structure

- Core: `AppSettings.cs`, `SettingsUpdateMerger.cs`.
- Windows infrastructure: new `WindowsStartupRegistration.cs`, `SingleInstanceGuard.cs`.
- Windows UI/lifecycle: `App.xaml.cs`, `DisplayPage.xaml`, `DisplayPage.xaml.cs`, `StudyListDraft.cs`, `SettingsWindow.xaml.cs`.
- Tests: JSON persistence, startup service, mutex, and Settings workflow tests.
- Release: new `scripts/publish-release.ps1`, README update.

### Task 1: Persist Windows sign-in setting

**Files:** modify `src/PteFloatingSentence.Core/AppSettings.cs`, `src/PteFloatingSentence.Core/SettingsUpdateMerger.cs`; test `tests/PteFloatingSentence.Windows.Tests/JsonSettingsStoreTests.cs` and `tests/PteFloatingSentence.Core.Tests/FlashcardDomainTests.cs`.

**Produces:** `bool AppSettings.LaunchAtWindowsSignIn { get; init; } = true`.

- [ ] **Step 1: Write failing tests**

```csharp
[TestMethod]
public async Task LoadAsync_LegacySettings_DefaultsLaunchAtWindowsSignInToTrue()
{
    await File.WriteAllTextAsync(_path, "{\"Version\":2,\"Sentence\":\"Legacy\"}");
    Assert.IsTrue((await new JsonSettingsStore(_path).LoadAsync()).LaunchAtWindowsSignIn);
}

[TestMethod]
public void SettingsUpdateMerger_UsesSubmittedStartupPreference()
{
    var merged = SettingsUpdateMerger.MergeEditableFields(
        AppSettings.Default with { LaunchAtWindowsSignIn = true },
        AppSettings.Default with { LaunchAtWindowsSignIn = false });
    Assert.IsFalse(merged.LaunchAtWindowsSignIn);
}
```

- [ ] **Step 2: Verify RED**

Run: `dotnet test PteFloatingSentence.sln -c Release --filter "LaunchAtWindowsSignIn|StartupPreference"`

Expected: compile failure because the property does not exist.

- [ ] **Step 3: Implement minimal setting support**

```csharp
// AppSettings.cs
public bool LaunchAtWindowsSignIn { get; init; } = true;

// SettingsUpdateMerger.cs, inside the with expression
LaunchAtWindowsSignIn = submitted.LaunchAtWindowsSignIn,
```

- [ ] **Step 4: Verify GREEN and commit**

Run: `dotnet test PteFloatingSentence.sln -c Release --filter "LaunchAtWindowsSignIn|StartupPreference"`

Expected: PASS.

Run: `git add src/PteFloatingSentence.Core/AppSettings.cs src/PteFloatingSentence.Core/SettingsUpdateMerger.cs tests/PteFloatingSentence.Windows.Tests/JsonSettingsStoreTests.cs tests/PteFloatingSentence.Core.Tests/FlashcardDomainTests.cs; git commit -m "feat: persist Windows sign-in preference"`

### Task 2: Implement shortcut registration and single-instance guard

**Files:** create `src/PteFloatingSentence.Windows/Infrastructure/WindowsStartupRegistration.cs`, `src/PteFloatingSentence.Windows/Infrastructure/SingleInstanceGuard.cs`; modify `src/PteFloatingSentence.Windows/App.xaml.cs`; test new `WindowsStartupRegistrationTests.cs` and `SingleInstanceGuardTests.cs`.

**Produces:**

```csharp
public sealed record StartupRegistrationResult(bool IsSuccess, string? Error);
public sealed class WindowsStartupRegistration
{
    public StartupRegistrationResult Reconcile(bool enabled, string executablePath);
    public static bool IsPublishedExecutable(string executablePath);
}
public sealed class SingleInstanceGuard : IDisposable
{
    public static bool TryAcquire(string name, out SingleInstanceGuard? guard);
}
```

- [ ] **Step 1: Write failing service tests**

```csharp
[TestMethod]
public void Reconcile_DevelopmentHost_DoesNotCreateShortcut()
{
    var store = new FakeStartupShortcutStore();
    var result = new WindowsStartupRegistration(store).Reconcile(true, "dotnet");
    Assert.IsTrue(result.IsSuccess);
    Assert.IsFalse(store.CreatedShortcut);
}

[TestMethod]
public void TryAcquire_SecondGuardWithSameName_ReturnsFalse()
{
    var name = $"EnglishFloating.Tests.{Guid.NewGuid():N}";
    Assert.IsTrue(SingleInstanceGuard.TryAcquire(name, out var first));
    using (first!) Assert.IsFalse(SingleInstanceGuard.TryAcquire(name, out _));
}
```

- [ ] **Step 2: Verify RED**

Run: `dotnet test tests/PteFloatingSentence.Windows.Tests/PteFloatingSentence.Windows.Tests.csproj -c Release --filter "WindowsStartupRegistration|SingleInstanceGuard"`

Expected: compile failure because the services do not exist.

- [ ] **Step 3: Implement the narrow services and lifecycle**

```csharp
public static bool IsPublishedExecutable(string path) =>
    Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);

public StartupRegistrationResult Reconcile(bool enabled, string executablePath)
{
    if (!enabled) return _shortcutStore.RemoveOwnedShortcut("EnglishFloating.lnk");
    if (!IsPublishedExecutable(executablePath)) return StartupRegistrationResult.Success;
    return _shortcutStore.CreateOrUpdateOwnedShortcut("EnglishFloating.lnk", executablePath);
}
```

Use `Environment.GetFolderPath(Environment.SpecialFolder.Startup)`. Create/remove only `EnglishFloating.lnk`. In `App.OnStartup`, acquire a per-user `Local\EnglishFloating.<SID>` mutex before UI creation; call `Shutdown()` and return on failure. Reconcile after loading settings and after `SaveSettings`; registration errors must not block the overlay. Dispose the guard in `ShutdownAsync`.

- [ ] **Step 4: Verify GREEN and commit**

Run: `dotnet test tests/PteFloatingSentence.Windows.Tests/PteFloatingSentence.Windows.Tests.csproj -c Release --filter "WindowsStartupRegistration|SingleInstanceGuard"`

Expected: PASS.

Run: `git add src/PteFloatingSentence.Windows/Infrastructure/WindowsStartupRegistration.cs src/PteFloatingSentence.Windows/Infrastructure/SingleInstanceGuard.cs src/PteFloatingSentence.Windows/App.xaml.cs tests/PteFloatingSentence.Windows.Tests/WindowsStartupRegistrationTests.cs tests/PteFloatingSentence.Windows.Tests/SingleInstanceGuardTests.cs; git commit -m "feat: start EnglishFloating at Windows sign-in"`

### Task 3: Add Display setting and publish executable

**Files:** modify `DisplayPage.xaml`, `DisplayPage.xaml.cs`, `StudyListDraft.cs`, `SettingsWindow.xaml.cs`; test `SettingsWorkflowTests.cs`; create `scripts/publish-release.ps1`; modify `README.md`.

**Produces:** `DisplayPage.LoadPreferences(..., bool launchAtWindowsSignIn)` and `FullDisplayPreferencesChanged(bool, bool, bool, string?, bool)`.

- [ ] **Step 1: Write failing UI test**

```csharp
[TestMethod]
public void DisplayPage_LoadsAndEmitsLaunchAtWindowsSignIn()
{
    RunOnSta(() =>
    {
        var page = new DisplayPage();
        page.LoadPreferences(true, true, false, null, [], launchAtWindowsSignIn: true);
        var toggle = (CheckBox)page.FindName("LaunchAtWindowsSignInInput");
        Assert.IsTrue(toggle.IsChecked);
        bool? emitted = null;
        page.FullDisplayPreferencesChanged += (_, _, _, _, launch) => emitted = launch;
        toggle.IsChecked = false;
        Assert.AreEqual(false, emitted);
    });
}
```

- [ ] **Step 2: Verify RED**

Run: `dotnet test tests/PteFloatingSentence.Windows.Tests/PteFloatingSentence.Windows.Tests.csproj -c Release --filter "LaunchAtWindowsSignIn"`

Expected: compile failure because the control and event argument do not exist.

- [ ] **Step 3: Implement toggle and deterministic publish script**

```xml
<CheckBox x:Name="LaunchAtWindowsSignInInput"
          Content="Launch at Windows sign-in"
          ToolTip="Starts EnglishFloating after you sign in to Windows."
          Checked="OnPreferenceChanged"
          Unchecked="OnPreferenceChanged" />
```

Thread the bool through the existing Display draft flow. Add this exact publish command to `scripts/publish-release.ps1`:

```powershell
$project = Join-Path $PSScriptRoot '..\src\PteFloatingSentence.Windows\PteFloatingSentence.Windows.csproj'
$output = Join-Path $PSScriptRoot '..\release'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (-not (Test-Path -LiteralPath (Join-Path $output 'EnglishFloating.exe'))) { throw 'Expected release executable was not produced.' }
```

Update README with the command and stable release path; never recursively delete `release`.

- [ ] **Step 4: Verify GREEN, publish, and commit**

Run: `dotnet test PteFloatingSentence.sln -c Release --no-restore`

Expected: all Core and Windows tests PASS.

Run: `powershell -ExecutionPolicy Bypass -File scripts\publish-release.ps1`

Expected: `C:\Dev\PteFloatingSentence\release\EnglishFloating.exe` exists.

Run: `git add src/PteFloatingSentence.Windows/DisplayPage.xaml src/PteFloatingSentence.Windows/DisplayPage.xaml.cs src/PteFloatingSentence.Windows/StudyListDraft.cs src/PteFloatingSentence.Windows/SettingsWindow.xaml.cs tests/PteFloatingSentence.Windows.Tests/SettingsWorkflowTests.cs scripts/publish-release.ps1 README.md; git commit -m "build: publish self-contained EnglishFloating release"`

### Task 4: Manual release verification

- [ ] **Step 1: Launch once**

Run: `Start-Process -FilePath C:\Dev\PteFloatingSentence\release\EnglishFloating.exe`

Expected: one overlay appears.

- [ ] **Step 2: Verify shortcut and duplicate prevention**

Run: `Get-Item "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\EnglishFloating.lnk"`

Expected: one shortcut targets the release executable.

Run: `Start-Process -FilePath C:\Dev\PteFloatingSentence\release\EnglishFloating.exe`

Expected: no second overlay appears.

- [ ] **Step 3: Verify disabling startup**

Clear `Launch at Windows sign-in` in Settings → Display, save, then run `Test-Path "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\EnglishFloating.lnk"`.

Expected: `False`.

## Self-Review

- Task 1 covers default persistence and merge semantics.
- Task 2 covers current-user shortcut ownership, development safety, errors, and duplicate process prevention.
- Task 3 covers setting UI and self-contained release creation.
- Task 4 verifies the real Windows output and auto-start contract.
- No placeholders or undefined interfaces remain.
