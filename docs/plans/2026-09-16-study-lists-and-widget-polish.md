# Study Lists and Widget Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Add reusable study lists, manual sentence navigation, a modern list-based Settings workspace, and a softly luminous floating widget.

**Architecture:** Core owns version-2 settings, migration, normalization, and wrap navigation without WPF. WPF owns JSON persistence, app lifetime, draft Settings UI, and presentation. The widget raises navigation events; App applies Core rules and persists the updated state with its existing serialized queue.

**Tech Stack:** C# 13, .NET 9, WPF, System.Text.Json, MSTest.

## Global Constraints

- Work in C:\Dev\PteFloatingSentence on Windows 10 and Windows 11.
- Preserve the borderless, transparent, draggable, topmost, taskbar-free widget.
- Every sentence has 20 whitespace-separated words or fewer.
- Store only local versioned JSON. Core cannot reference WPF.
- Next/previous changes only the current sentence; it never changes completion.
- List target must be positive and is not a cap on stored sentences.
- Exclude masked testing, answer input/scoring, completion changes, automatic daily generation, tags, audio, recording, cloud sync, and drag-drop reordering.

---

## Planned Files

~~~
src/PteFloatingSentence.Core/
  AppSettings.cs             # version 2 root settings
  StudyList.cs               # sentence and list records
  StudyListRules.cs          # migration, normalization, validation, navigation
src/PteFloatingSentence.Windows/
  App.xaml.cs                # app state and navigation wiring
  FloatingWindow.xaml/.cs    # polished hover navigation surface
  SettingsWindow.xaml/.cs    # draft list-management UI
  Infrastructure/JsonSettingsStore.cs
tests/PteFloatingSentence.Core.Tests/StudyListRulesTests.cs
tests/PteFloatingSentence.Windows.Tests/
  JsonSettingsStoreTests.cs
  SettingsWorkflowTests.cs
  WindowSurfaceTests.cs
~~~

### Task 1: Version-2 study-list domain and rules

**Files:**
- Modify: C:\Dev\PteFloatingSentence\src\PteFloatingSentence.Core\AppSettings.cs
- Create: C:\Dev\PteFloatingSentence\src\PteFloatingSentence.Core\StudyList.cs
- Create: C:\Dev\PteFloatingSentence\src\PteFloatingSentence.Core\StudyListRules.cs
- Create: C:\Dev\PteFloatingSentence\tests\PteFloatingSentence.Core.Tests\StudyListRulesTests.cs

**Interfaces:**
- Produce StudySentence(Guid Id, string Text, bool IsCompleted) and StudyList(Guid Id, string Name, int TargetSentenceCount, int CurrentSentenceIndex, IReadOnlyList<StudySentence> Sentences).
- Produce StudyListRules.CreateDefault(string), Normalize(AppSettings), ActiveList(AppSettings), MoveCurrentSentence(AppSettings, int), and ValidateList(StudyList).

- [ ] **Step 1: Write the failing rules tests**

Create StudyListRulesTests.cs with checks for default migration, active-list fallback, index clamping, empty-list behavior, target validation, sentence validation, duplicate ID repair, and wrapping without completion mutation:

~~~csharp
[TestMethod]
public void CreateDefault_UsesLegacySentenceInMyFirstList()
{
    var settings = StudyListRules.CreateDefault("A useful practice sentence.");
    Assert.AreEqual(2, settings.Version);
    Assert.AreEqual("My first list", settings.StudyLists.Single().Name);
    Assert.AreEqual(10, settings.StudyLists.Single().TargetSentenceCount);
    Assert.AreEqual("A useful practice sentence.", settings.StudyLists.Single().Sentences.Single().Text);
}

[TestMethod]
public void MoveCurrentSentence_WrapsWithoutChangingCompletion()
{
    var moved = StudyListRules.MoveCurrentSentence(SettingsWith("one", "two"), -1);
    var list = StudyListRules.ActiveList(moved);
    Assert.AreEqual(1, list.CurrentSentenceIndex);
    Assert.IsFalse(list.Sentences[1].IsCompleted);
}
~~~

- [ ] **Step 2: Run the tests and verify failure**

Run:

~~~powershell
dotnet test tests\PteFloatingSentence.Core.Tests\PteFloatingSentence.Core.Tests.csproj --filter FullyQualifiedName~StudyListRulesTests
~~~

Expected: compile failure because study-list types do not exist.

- [ ] **Step 3: Implement the models and rules**

Create:

~~~csharp
public sealed record StudySentence(Guid Id, string Text, bool IsCompleted = false);
public sealed record StudyList(Guid Id, string Name, int TargetSentenceCount,
    int CurrentSentenceIndex, IReadOnlyList<StudySentence> Sentences);
~~~

Extend AppSettings with Version = 2, Guid ActiveListId, and IReadOnlyList<StudyList> StudyLists, preserving style and placement fields. Default creates My first list, target 10, and the instructional sentence.

Normalize trims text/names, ensures at least one list, repairs missing/duplicate IDs, clamps indexes, assigns target 10 for invalid targets, and chooses the first list when the active ID is invalid. MoveCurrentSentence returns unchanged state for an empty active list; otherwise uses (index + direction + count) % count and changes only the index. ValidateList rejects blank names, targets below 1, and text rejected by SentenceValidator.

- [ ] **Step 4: Run all Core tests**

~~~powershell
dotnet test tests\PteFloatingSentence.Core.Tests\PteFloatingSentence.Core.Tests.csproj
~~~

Expected: all Core tests pass.

- [ ] **Step 5: Commit**

~~~powershell
git add src/PteFloatingSentence.Core tests/PteFloatingSentence.Core.Tests
git commit -m "feat: add study list domain rules"
~~~

### Task 2: Migrate and persist version-2 settings

**Files:**
- Modify: C:\Dev\PteFloatingSentence\src\PteFloatingSentence.Windows\Infrastructure\JsonSettingsStore.cs
- Modify: C:\Dev\PteFloatingSentence\tests\PteFloatingSentence.Windows.Tests\JsonSettingsStoreTests.cs

**Interfaces:**
- LoadAsync() migrates v1 settings, returns normalized v2 settings, and still falls back safely for invalid JSON.
- SaveAsync(AppSettings) writes indented normalized version-2 JSON.

- [ ] **Step 1: Write failing migration and round-trip tests**

Write a v1 JSON fixture with only Sentence, font, color, opacity, and position, then assert load returns version 2 with My first list, target 10, legacy sentence, and preserved style/position. Add a v2 round trip with two lists, different indexes, active ID, and IsCompleted: true.

~~~csharp
Assert.AreEqual(2, loaded.Version);
Assert.AreEqual("Legacy text.", loaded.StudyLists.Single().Sentences.Single().Text);
Assert.AreEqual(second.Id, roundTripped.ActiveListId);
Assert.IsTrue(roundTripped.StudyLists[1].Sentences[0].IsCompleted);
~~~

- [ ] **Step 2: Run and verify failure**

~~~powershell
dotnet test tests\PteFloatingSentence.Windows.Tests\PteFloatingSentence.Windows.Tests.csproj --filter FullyQualifiedName~JsonSettingsStoreTests
~~~

Expected: v1 migration or v2 data assertions fail.

- [ ] **Step 3: Implement migration**

Detect missing or lower JSON version. Construct StudyListRules.CreateDefault(legacySentence), copy valid legacy display/position values, and save only v2 data thereafter. For v2 input return StudyListRules.Normalize(deserialized). Keep safe default fallback for I/O, unauthorized, and JSON errors.

- [ ] **Step 4: Verify and commit**

~~~powershell
dotnet test tests/PteFloatingSentence.Windows.Tests/PteFloatingSentence.Windows.Tests.csproj
git add src/PteFloatingSentence.Windows/Infrastructure/JsonSettingsStore.cs tests/PteFloatingSentence.Windows.Tests/JsonSettingsStoreTests.cs
git commit -m "feat: migrate settings to study lists"
~~~

Expected: Windows persistence tests pass.

### Task 3: Polish floating widget and wire navigation

**Files:**
- Modify: C:\Dev\PteFloatingSentence\src\PteFloatingSentence.Windows\FloatingWindow.xaml
- Modify: C:\Dev\PteFloatingSentence\src\PteFloatingSentence.Windows\FloatingWindow.xaml.cs
- Modify: C:\Dev\PteFloatingSentence\src\PteFloatingSentence.Windows\App.xaml.cs
- Modify: C:\Dev\PteFloatingSentence\tests\PteFloatingSentence.Windows.Tests\WindowSurfaceTests.cs

**Interfaces:**
- FloatingWindow emits PreviousRequested and NextRequested.
- ApplySettings(AppSettings) resolves and renders the active list sentence/progress.
- App handlers call StudyListRules.MoveCurrentSentence and PersistSettings().

- [ ] **Step 1: Write failing surface tests**

Assert XAML includes PreviousButton and NextButton, initial Visibility="Collapsed", a LinearGradientBrush, and only Settings/Exit in the context menu. Add a workflow test proving navigation wraps and leaves IsCompleted unchanged.

- [ ] **Step 2: Run focused tests and verify failure**

~~~powershell
dotnet test tests/PteFloatingSentence.Windows.Tests/PteFloatingSentence.Windows.Tests.csproj --filter "FullyQualifiedName~WindowSurfaceTests|FullyQualifiedName~SettingsWorkflowTests"
~~~

Expected: new visual or navigation assertions fail.

- [ ] **Step 3: Implement hover navigation**

Use a padded outer border with blue-to-mint LinearGradientBrush, a near-black inner border, centered sentence, and progress text ListName · index / count. Add left/right buttons hidden until pointer hover; show only for lists with more than one sentence. Button clicks raise events and mark handled. The drag handler calls DragMove() only when the original source is outside both button trees.

For an empty list, show exactly Add a sentence in Settings., show 0 / 0, and disable arrows.

- [ ] **Step 4: Wire App state**

Subscribe to both events in OnStartup. Replace settings with MoveCurrentSentence(_settings, -1) or MoveCurrentSentence(_settings, 1), reapply it to the widget, then queue persistence. Do not change any IsCompleted value.

- [ ] **Step 5: Verify and commit**

~~~powershell
dotnet test PteFloatingSentence.sln
dotnet build PteFloatingSentence.sln --configuration Debug --no-restore
git add src/PteFloatingSentence.Windows tests/PteFloatingSentence.Windows.Tests
git commit -m "feat: add polished widget navigation"
~~~

Expected: tests pass and build has zero errors.

### Task 4: Build the Settings study-list workspace

**Files:**
- Modify: C:\Dev\PteFloatingSentence\src\PteFloatingSentence.Windows\SettingsWindow.xaml
- Modify: C:\Dev\PteFloatingSentence\src\PteFloatingSentence.Windows\SettingsWindow.xaml.cs
- Modify: C:\Dev\PteFloatingSentence\src\PteFloatingSentence.Windows\App.xaml.cs
- Modify: C:\Dev\PteFloatingSentence\tests\PteFloatingSentence.Windows.Tests\SettingsWorkflowTests.cs

**Interfaces:**
- Settings receives full AppSettings and calls Action<AppSettings> only after valid Save.
- It produces create, rename, activate, delete list; add/edit/delete/select sentence actions over an isolated draft.

- [ ] **Step 1: Write failing draft tests**

Add a WPF-free StudyListDraft helper and tests proving: new list has a unique ID and target 10; last-list deletion returns Cannot delete the last list.; 21-word add returns Use 20 words or fewer.; selecting a sentence changes only that list index; and Cancel never invokes the save callback.

- [ ] **Step 2: Run focused tests and verify failure**

~~~powershell
dotnet test tests/PteFloatingSentence.Windows.Tests/PteFloatingSentence.Windows.Tests.csproj --filter FullyQualifiedName~SettingsWorkflowTests
~~~

Expected: draft-operation types are not found.

- [ ] **Step 3: Implement workspace and draft**

Use a two-column dark Settings window: sidebar list names/counts plus New list; editor list name, target, Make active, Delete list, ordered sentence rows, Add sentence, Save, and Cancel. Deep-copy settings for editing. New list is named New list with target 10. Delete confirms and refuses final-list deletion. Selecting a sentence updates its draft current index. Save normalizes and validates every draft list, shows the first error inline, updates active ID when requested, and calls callback once. Cancel closes without callback.

Update App to replace its state from the valid returned draft while retaining position and existing serialized persistence behavior.

- [ ] **Step 4: Verify and commit**

~~~powershell
dotnet test PteFloatingSentence.sln --configuration Debug
dotnet build PteFloatingSentence.sln --configuration Debug --no-restore
git add src/PteFloatingSentence.Windows tests/PteFloatingSentence.Windows.Tests
git commit -m "feat: manage study lists in settings"
~~~

Expected: all tests pass and Debug build has zero errors.

### Task 5: Verify user-visible flow and migration

**Files:**
- Modify only a file implicated by a failed verification check.

- [ ] **Step 1: Run fresh automated verification**

~~~powershell
dotnet test PteFloatingSentence.sln --configuration Debug
dotnet build PteFloatingSentence.sln --configuration Debug --no-restore
~~~

Expected: every test passes; build has zero warnings/errors.

- [ ] **Step 2: Run manual Windows checks**

Launch:

~~~powershell
dotnet run --project src/PteFloatingSentence.Windows
~~~

Verify luminous but subtle border, topmost widget, hover-only arrows, non-draggable arrow clicks, wrapping navigation, no completion mutation, list CRUD, final-list protection, target persistence, restart restoration, and empty-list message.

- [ ] **Step 3: Verify migration**

Back up %LOCALAPPDATA%\PteFloatingSentence\settings.json when present. Use a v1 fixture containing legacy sentence/style/position, launch and Save, then inspect v2 JSON for preserved style/position, My first list, target 10, and legacy text. Restore the backup after the check.

- [ ] **Step 4: Report evidence**

Record exact test count, build output, manual checks completed, and environmental limitations. Do not claim an unchecked acceptance criterion passed.

