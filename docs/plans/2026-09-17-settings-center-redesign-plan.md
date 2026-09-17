# Settings Center Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the crowded Settings form with an extensible dark Settings Center shell containing Setup, Display, Review, and Gemini pages.

**Architecture:** Keep `SettingsWindow` as the host shell and move each functional area into focused WPF `UserControl` pages. A small page-navigation state in the shell swaps the active page without opening multiple windows. Existing `StudyListDraft`, `AppSettings`, `VocabularyWorkflow`, and persistence remain the source of truth; pages receive explicit state/callbacks instead of owning global application state.

**Tech Stack:** .NET 9, WPF, C#, MSTest, existing immutable Core records and JSON persistence.

## Global Constraints

- Preserve all existing study-list, sentence, vocabulary, Gemini key, and save/cancel behavior.
- Do not add third-party UI libraries or external icon assets.
- Keep the current dark theme, but use a consistent shell, spacing, border, and accent system.
- New UI behavior requires a failing test before production code.
- The redesign must remain usable at the existing minimum window size and support vertical scrolling inside pages.
- Do not implement flashcard quiz logic in this redesign; Review is read-only until a separate plan.

---

### Task 1: Define the Settings Center navigation contract

**Files:**
- Create: `src/PteFloatingSentence.Windows/SettingsPageId.cs`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/SettingsWorkflowTests.cs`

**Interfaces:**
- Define `SettingsPageId` values: `Setup`, `Display`, `Review`, `Gemini`.
- `SettingsWindow.SelectedPage` exposes the current page for UI tests.
- `SettingsWindow.NavigateTo(SettingsPageId page)` changes the selected navigation item without changing draft data.

- [ ] Add failing tests for the default page (`Setup`), navigation to each page, and preserving the draft while navigating.
- [ ] Run `dotnet test tests/PteFloatingSentence.Windows.Tests/PteFloatingSentence.Windows.Tests.csproj --filter FullyQualifiedName~SettingsNavigation` and verify failure because the contract does not exist.
- [ ] Add the enum and minimal shell navigation state.
- [ ] Run focused tests and verify they pass.
- [ ] Commit with `feat: define settings center navigation contract`.

### Task 2: Create the Settings Center shell and visual system

**Files:**
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/WindowSurfaceTests.cs`

**Interfaces:**
- The shell owns a fixed left navigation rail, a page header, a scrollable content host, and a fixed footer.
- Required named elements: `SettingsNavigation`, `PageTitle`, `PageSubtitle`, `PageContentHost`, `SaveButton`, and `CancelButton`.
- Navigation items use `SettingsPageId` in their `Tag` property.

- [ ] Add failing surface tests for the navigation rail, all four navigation labels, page content host, and footer buttons.
- [ ] Replace the current single-grid form layout with the shell structure while preserving the existing window class and event handlers.
- [ ] Add shared resources for panel backgrounds, borders, muted text, accent, primary, secondary, and danger buttons.
- [ ] Add selected navigation styling and keyboard focus visibility without relying on external icons.
- [ ] Run XAML/build tests and verify the shell loads at the current minimum size.
- [ ] Commit with `ui: add extensible settings center shell`.

### Task 3: Extract SetupPage

**Files:**
- Create: `src/PteFloatingSentence.Windows/SetupPage.xaml`
- Create: `src/PteFloatingSentence.Windows/SetupPage.xaml.cs`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/WindowSurfaceTests.cs`

**Interfaces:**
- `SetupPage` owns list selection, list name, target, sentence list, sentence editor, and list/sentence actions.
- It consumes the existing `StudyListDraft` through callbacks:
  - `Func<ValidationResult> CommitListEdits`
  - `Action<Guid> SelectList`
  - `Action<Guid> SelectSentence`
  - `Func<string, ValidationResult> AddSentence`
  - `Func<string, ValidationResult> UpdateSentence`
  - `Action DeleteSentence`
- It raises `ListChanged`/`SentenceChanged` notifications to the shell only through existing draft updates.

- [ ] Add failing tests proving SetupPage exposes the list editor controls and that selecting a sentence updates the editor text.
- [ ] Move the current list/sentence XAML into SetupPage without changing labels or validation semantics.
- [ ] Move corresponding event handlers into SetupPage and have SettingsWindow refresh the page through one explicit `RefreshFromDraft()` method.
- [ ] Keep destructive actions visually separated and require the existing delete confirmation for lists.
- [ ] Run Setup-focused tests and verify all existing list/sentence workflow tests remain green.
- [ ] Commit with `refactor: extract setup settings page`.

### Task 4: Extract DisplayPage

**Files:**
- Create: `src/PteFloatingSentence.Windows/DisplayPage.xaml`
- Create: `src/PteFloatingSentence.Windows/DisplayPage.xaml.cs`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml.cs`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml`
- Modify: `tests/PteFloatingSentence.Windows.Tests/SettingsWorkflowTests.cs`

**Interfaces:**
- `DisplayPage` exposes `ShowSentenceOverlay`, `ShowVocabularyCards`, and a short launcher explanation.
- The page emits a single `DisplayPreferencesChanged` event carrying both boolean values.
- SettingsWindow persists the values through the existing draft/settings save path; the page does not write JSON directly.

- [ ] Add failing tests for loading current preference values and emitting updated values when checkboxes change.
- [ ] Implement a clean Display card with grouped toggles and future-ready helper text.
- [ ] Add a disabled/preview-only row for future pronunciation or navigation controls only if it is visually clear; do not create fake behavior.
- [ ] Run Display tests and confirm save/cancel semantics: Save persists, Cancel discards.
- [ ] Commit with `feat: add display settings page`.

### Task 5: Add ReviewPage and GeminiPage shells

**Files:**
- Create: `src/PteFloatingSentence.Windows/ReviewPage.xaml`
- Create: `src/PteFloatingSentence.Windows/ReviewPage.xaml.cs`
- Create: `src/PteFloatingSentence.Windows/GeminiPage.xaml`
- Create: `src/PteFloatingSentence.Windows/GeminiPage.xaml.cs`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml.cs`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml`
- Modify: `tests/PteFloatingSentence.Windows.Tests/WindowSurfaceTests.cs`

**Interfaces:**
- `ReviewPage` is read-only and displays list progress, completed sentence count, vocabulary count, and pending/failed explanation count.
- `GeminiPage` owns API key input, configured status, and clear/save callbacks using the existing `ProtectedApiKeyStore`.
- Neither page owns `AppSettings` persistence directly.

- [ ] Add failing tests for Review empty state, Review list summary rendering, and Gemini key/status controls.
- [ ] Implement Review as a stable placeholder for future quiz navigation, with no answer input yet.
- [ ] Move existing API key controls and handlers into GeminiPage while preserving DPAPI storage and clear behavior.
- [ ] Run page-focused tests and verify no API key is exposed in labels, logs, or ordinary settings JSON.
- [ ] Commit with `feat: add review and gemini settings pages`.

### Task 6: Integrate page lifecycle and verify responsive behavior

**Files:**
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml.cs`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml`
- Modify: `tests/PteFloatingSentence.Windows.Tests/WindowSurfaceTests.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/SettingsWorkflowTests.cs`

- [ ] Add a single `RenderSelectedPage()` path that clears/replaces the content host and refreshes only the active page.
- [ ] Ensure switching pages does not lose unsaved list, sentence, display, or API key edits.
- [ ] Ensure Save validates the active page and all draft data before closing; Cancel closes without persistence.
- [ ] Add minimum-size and scrollability checks for Setup and Review content.
- [ ] Run sequential verification:
  - `dotnet test tests/PteFloatingSentence.Core.Tests/PteFloatingSentence.Core.Tests.csproj --configuration Debug --no-restore`
  - `dotnet test tests/PteFloatingSentence.Windows.Tests/PteFloatingSentence.Windows.Tests.csproj --configuration Debug --no-restore`
  - `dotnet build PteFloatingSentence.sln --configuration Debug --no-restore`
- [ ] Run `git diff --check`, inspect the rendered Settings window manually, and commit with `ui: complete settings center page integration`.

## Deferred Follow-up

- Flashcard quiz answer checking.
- Vocabulary review scheduling and spaced repetition.
- Launcher icon artwork and tray integration.
- Pronunciation audio controls.
- Settings search and keyboard shortcut customization.
