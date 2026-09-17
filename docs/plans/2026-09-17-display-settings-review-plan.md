# Display Settings and Review Center Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the current Settings window into a scalable control center for sentence display, vocabulary cards, study-list setup, and a future review/flashcard workflow.

**Architecture:** Keep study-list editing in the existing `SettingsWindow`, add a persisted display-preferences model to `AppSettings`, and isolate overlay visibility behind a small display controller owned by `App`. When both visual layers are hidden, show a compact always-on-top `OverlayLauncherWindow` that reopens Settings and restores the overlay. Add a Review section as a read-only first slice, with quiz behavior explicitly deferred until its data contract is stable.

**Tech Stack:** .NET 9, WPF, C#, MSTest, existing immutable record-based Core models, JSON settings persistence, existing `FloatingWindow` and `SettingsWindow`.

## Global Constraints

- Preserve the existing always-on-top sentence overlay behavior.
- Do not change Gemini API, vocabulary persistence, or study-list semantics in this plan.
- Display preferences must survive app restart through the existing JSON settings store.
- The launcher must remain usable when the sentence window and vocabulary cards are hidden.
- All new behavior requires a failing test before production code.
- Keep the first Review slice read-only; do not implement answer checking or spaced repetition yet.

---

### Task 1: Add persisted display preferences

**Files:**
- Modify: `src/PteFloatingSentence.Core/AppSettings.cs`
- Modify: `src/PteFloatingSentence.Core/StudyListRules.cs`
- Modify: `tests/PteFloatingSentence.Core.Tests/StudyListRulesTests.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/JsonSettingsStoreTests.cs`

**Interfaces:**
- Produce `AppSettings.ShowSentenceOverlay: bool` defaulting to `true`.
- Produce `AppSettings.ShowVocabularyCards: bool` defaulting to `true`.
- Preserve backward compatibility: settings JSON without either property loads with both values set to `true`.

- [x] Write failing tests for default values, JSON round-trip, and old JSON compatibility.
- [x] Run `dotnet test tests/PteFloatingSentence.Core.Tests/PteFloatingSentence.Core.Tests.csproj --filter FullyQualifiedName~Display` and confirm failure because properties do not exist.
- [x] Add the two properties to the immutable `AppSettings` record with defaults; update any normalization/copy rules so they survive list navigation and `with` expressions.
- [x] Run Core and persistence tests and confirm all pass.
- [x] Commit with `feat: persist overlay display preferences`.

### Task 2: Add a display section to SettingsWindow

**Files:**
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml.cs`
- Modify: `src/PteFloatingSentence.Windows/StudyListDraft.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/SettingsWorkflowTests.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/WindowSurfaceTests.cs`

**Interfaces:**
- Add two checkboxes named `ShowSentenceOverlayInput` and `ShowVocabularyCardsInput`.
- Add `StudyListDraft.SetDisplayPreferences(bool showSentence, bool showVocabulary)` or an equivalent single update method that preserves all existing list edits.
- Save must write the checkbox values into `AppSettings` only after current list validation succeeds.

- [x] Add tests proving unchecked values are saved and checked values remain the default.
- [x] Add a XAML surface test asserting the Display section and both controls exist.
- [x] Run the focused tests and verify failure before implementation.
- [x] Add a compact “Display” card near the top of the editor, with helper text explaining that hiding both layers leaves the launcher icon visible.
- [x] Load checkbox state in `RefreshUi()` and include it in `SaveButton_Click`.
- [x] Run settings tests and build.
- [x] Commit with `feat: add display controls to settings`.

### Task 3: Centralize overlay visibility and add launcher icon

**Files:**
- Create: `src/PteFloatingSentence.Windows/OverlayLauncherWindow.xaml`
- Create: `src/PteFloatingSentence.Windows/OverlayLauncherWindow.xaml.cs`
- Create: `src/PteFloatingSentence.Windows/DisplayController.cs`
- Modify: `src/PteFloatingSentence.Windows/App.xaml.cs`
- Modify: `src/PteFloatingSentence.Windows/FloatingWindow.xaml.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/DisplayControllerTests.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/WindowSurfaceTests.cs`

**Interfaces:**
- `DisplayController.Apply(AppSettings settings)` decides visibility for the sentence window and vocabulary panel.
- `DisplayController` owns launcher lifecycle and exposes `ShowSettingsRequested` and `RestoreOverlayRequested` events.
- `OverlayLauncherWindow` is borderless, always-on-top, draggable, keyboard-accessible, and contains a single settings/restore action.

- [x] Write tests for the visibility matrix:
  - sentence=true, cards=true → sentence visible, cards visible, launcher hidden;
  - sentence=true, cards=false → sentence visible, cards hidden, launcher hidden;
  - sentence=false, cards=true → sentence hidden, cards unavailable/hidden, launcher visible;
  - sentence=false, cards=false → sentence hidden, launcher visible.
- [x] Run focused tests and confirm failure because the controller does not exist.
- [x] Implement the controller without moving sentence rendering logic out of `FloatingWindow`.
- [x] Add a small launcher window with a neutral gear/list icon, a tooltip, and click handling to reopen Settings; avoid external image assets for now.
- [x] Ensure closing Settings does not accidentally hide the launcher; saving display preferences must immediately call `DisplayController.Apply`.
- [x] Run focused UI tests and build.
- [x] Commit with `feat: add overlay display controller and launcher`.

### Task 4: Create the Review section shell

**Files:**
- Create: `src/PteFloatingSentence.Windows/ReviewViewModel.cs`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/ReviewViewModelTests.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/WindowSurfaceTests.cs`

**Interfaces:**
- `ReviewViewModel` exposes list summaries and sentence/vocabulary counts only.
- It must not mutate `AppSettings` and must not call Gemini.
- The Settings UI exposes Review as a separate navigation section or card while retaining the current Setup controls.

- [x] Write tests that a list with completed sentences and vocabulary produces deterministic review summaries.
- [x] Run focused tests and verify failure.
- [x] Implement read-only summaries: list name, completed sentence count, total sentence count, vocabulary count, and pending/failed explanation count.
- [x] Add a Review section with an empty-state message when no study data exists and a clear “Quiz coming next” boundary; do not add fake quiz controls.
- [x] Run review and UI tests.
- [x] Commit with `feat: add read-only review section`.

### Task 5: Integration, migration, and release verification

**Files:**
- Modify: `src/PteFloatingSentence.Windows/App.xaml.cs`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml.cs`
- Modify: `docs/plans/2026-09-17-display-settings-review-plan.md`
- Optional modify: `README.md` if current setup instructions mention only the old Settings layout.

- [x] Test migration from settings JSON created before display preferences; verify both flags default to `true`.
- [x] Test saving each visibility combination and restarting the app state through `JsonSettingsStore`.
- [x] Test launcher recovery: hide both layers, open Settings from launcher, enable sentence display, save, and verify launcher hides while sentence returns.
- [x] Run sequential verification to avoid known WPF STA/resource races:
  - `dotnet test tests/PteFloatingSentence.Core.Tests/PteFloatingSentence.Core.Tests.csproj --configuration Debug --no-restore`
  - `dotnet test tests/PteFloatingSentence.Windows.Tests/PteFloatingSentence.Windows.Tests.csproj --configuration Debug --no-restore`
  - `dotnet build PteFloatingSentence.sln --configuration Debug --no-restore`
- [x] Review `git diff --check`, verify no API key or sentence data is logged, and confirm the working tree is clean.
- [x] Commit with `chore: verify display settings and review center`.

## Deliberately Deferred

- Actual flashcard quiz interaction and answer validation.
- Gemini-generated distractors or pronunciation audio.
- Spaced-repetition scheduling.
- Tray icon integration or Windows startup registration.
- Custom launcher artwork and theming beyond the existing dark UI.
