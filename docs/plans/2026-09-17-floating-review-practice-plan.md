# Floating Review Practice Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform the desktop floating overlay (`FloatingWindow`) into an interactive practice card when Practice Mode is started (from Settings or Context Menu), allowing the user to type answers directly into masked word inputs on screen rather than practicing inside the Settings window.

**Architecture:** `FloatingWindow` gains a `PracticeMode` state. When activated with a `StudyList`, it swaps the read-only `RichTextBox` with an inline `PracticeProjectionPanel` (WrapPanel of TextBlocks and compact TextBoxes for hidden words), hides vocabulary cards, and manages progression via `ReviewPracticeSession`. Sentence completions trigger persistence through the existing app callback. The Setup page in `SettingsWindow` and the `FloatingWindow` context menu provide the entry points to start/exit practice.

**Tech Stack:** .NET 9, WPF, C#, MSTest, existing Core `ReviewPracticeRules` and `ReviewPracticeSession`.

## Global Constraints

- The original `StudySentence.Text` is immutable and never modified by masking.
- In Practice Mode on `FloatingWindow`, vocabulary cards are collapsed to avoid spoilers.
- Exiting Practice Mode (via `✕` button or context menu) cleanly restores the normal read-only sentence view.
- All routed event handlers and delegates on `FloatingWindow` must have a deterministic teardown path to avoid memory leaks.
- All behavior changes require failing tests before implementation.

---

### Task 1: Add Practice Mode UI layout and state transitions to FloatingWindow

**Files:**
- Modify: `src/PteFloatingSentence.Windows/FloatingWindow.xaml`
- Modify: `src/PteFloatingSentence.Windows/FloatingWindow.xaml.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/WindowSurfaceTests.cs`

**Interfaces:**
- `FloatingWindow` exposes:
  - `bool IsPracticeMode { get; }`
  - `void StartPractice(Guid? listId = null)`
  - `void ExitPractice()`
  - `event EventHandler? PracticeModeChanged`
  - `event EventHandler<(Guid ListId, Guid SentenceId, bool Completed)>? SentenceCompleted`
- Context menu exposes:
  - `PracticeMenuItem` with text `"Start Practice"` (in normal mode) or `"Exit Practice"` (in practice mode).

- [x] Write failing surface tests verifying that calling `StartPractice()` switches `IsPracticeMode` to true, collapses `SentenceBox` and `VocabularyPanel`, shows `PracticeContainer`, and updates the context menu item.
- [x] Write failing surface test verifying that calling `ExitPractice()` restores `SentenceBox` visibility and sets `IsPracticeMode` to false.
- [x] Run `dotnet test tests/PteFloatingSentence.Windows.Tests/PteFloatingSentence.Windows.Tests.csproj --filter FullyQualifiedName~PracticeMode` and confirm failure.
- [x] Add practice UI containers inside `SentenceCard` in `FloatingWindow.xaml`:
  - `PracticeContainer` (StackPanel/Grid, collapsed by default).
  - `PracticeHeader` (Grid with `PracticeProgressLabel` and `PracticeExitButton` `✕`).
  - `PracticeProjectionPanel` (WrapPanel for token rendering).
  - `PracticeFeedbackLabel` (inline error / hint label).
  - `PracticeCompletionPanel` (completion message and restart button).
- [x] Implement `StartPractice(listId)` and `ExitPractice()` in `FloatingWindow.xaml.cs`.
- [x] Run focused tests and verify they pass.
- [x] Commit with `feat: add practice mode layout and state to floating window`.

### Task 2: Implement interactive inline masked-sentence input on FloatingWindow

**Files:**
- Modify: `src/PteFloatingSentence.Windows/FloatingWindow.xaml.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/WindowSurfaceTests.cs`

**Rules:**
- Visible tokens rendered as `TextBlock`s with font size matching settings.
- Hidden tokens rendered as compact `TextBox`es showing `_` when empty and unfocused.
- Focusing a hidden word `TextBox` clears `_`.
- Pressing `Enter` validates answer via `ReviewPracticeRules.CheckAnswer`.
- Correct answer:
  - Turns box emerald green (`#34D399`), displays source word, disables further editing.
  - Automatically moves keyboard focus to the next hidden `TextBox`.
- Wrong answer:
  - Highlights border red, shows inline "Try again.", keeps focus and selects all text.

- [x] Add failing tests for projection rendering: correct count of TextBlocks and TextBoxes for a masked sentence on `FloatingWindow`.
- [x] Add failing test for Enter key submitting answer and advancing focus to next hidden box.
- [x] Run tests and verify failure.
- [x] Implement projection rendering and container-level routed event handlers (`KeyDown`, `GotFocus`, `LostFocus`) on `PracticeProjectionPanel` with proper cleanup in `ExitPractice` and `OnClosed`.
- [x] Run tests and confirm they pass.
- [x] Commit with `feat: implement interactive masked input on floating window`.

### Task 3: Implement sentence completion, list navigation, and persistence

**Files:**
- Modify: `src/PteFloatingSentence.Windows/FloatingWindow.xaml.cs`
- Modify: `src/PteFloatingSentence.Windows/App.xaml.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/WindowSurfaceTests.cs`

**Rules:**
- When final hidden word is answered correctly:
  - Marks sentence `IsCompleted = true`.
  - Emits `SentenceCompleted` event.
  - `App.xaml.cs` updates `_settings` via `StudyListRules.MarkSentenceCompleted` and queues save.
  - Card shows completion feedback and automatically transitions to the next incomplete sentence.
- Hover navigation buttons (`PreviousButton` and `NextButton`) in practice mode navigate to previous/next sentences in the practice list.
- When all sentences in the list are complete:
  - Shows `PracticeCompletionPanel` with "All sentences completed!" and a "Restart" button.

- [ ] Add failing test verifying that completing the last hidden token fires `SentenceCompleted` and marks completion in settings.
- [ ] Add failing test verifying `PreviousButton` and `NextButton` change current practice sentence.
- [ ] Add failing test verifying all-complete state shows completion banner.
- [ ] Run tests and verify failure.
- [ ] Implement completion transition, navigation logic, and wire `SentenceCompleted` in `App.xaml.cs`.
- [ ] Run tests and confirm they pass.
- [ ] Commit with `feat: handle practice sentence completion and navigation`.

### Task 4: Add "Start Practice" entry points in SettingsWindow and Context Menu

**Files:**
- Modify: `src/PteFloatingSentence.Windows/SetupPage.xaml`
- Modify: `src/PteFloatingSentence.Windows/SetupPage.xaml.cs`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml.cs`
- Modify: `src/PteFloatingSentence.Windows/App.xaml.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/SettingsWorkflowTests.cs`

**UI contract:**
- In `SetupPage.xaml`, add a primary "Start Practice" button next to the study list selector or action buttons.
- Clicking "Start Practice" raises `StartPracticeRequested(listId)`.
- `SettingsWindow` commits any pending list edits, forwards the event, and closes.
- `App.xaml.cs` calls `_floatingWindow.StartPractice(listId)`.
- Floating window context menu: "Start Practice" starts practice for the active list; "Exit Practice" exits practice.

- [ ] Add failing workflow tests:
  - Clicking "Start Practice" in Setup page raises event with selected list ID and closes window.
  - Context menu "Start Practice" / "Exit Practice" triggers mode change.
- [ ] Run tests and verify failure.
- [ ] Implement the UI button and wire events through `SetupPage`, `SettingsWindow`, and `App.xaml.cs`.
- [ ] Run tests and verify all pass.
- [ ] Commit with `feat: wire start practice entry points in settings and context menu`.

### Task 5: Verification, memory leak audit, and final check

**Files:**
- Modify: `tests/PteFloatingSentence.Windows.Tests/WindowSurfaceTests.cs`
- Modify: `docs/plans/2026-09-17-floating-review-practice-plan.md`

- [ ] Run memory leak and event lifecycle checklist:
  - Verify all routed handlers on `PracticeProjectionPanel` are detached in `ExitPractice` and `OnClosed`.
  - Verify context menu handler cleanup.
- [ ] Run sequentially:
  - `dotnet test tests/PteFloatingSentence.Core.Tests/PteFloatingSentence.Core.Tests.csproj --configuration Debug --no-restore`
  - `dotnet test tests/PteFloatingSentence.Windows.Tests/PteFloatingSentence.Windows.Tests.csproj --configuration Debug --no-restore`
  - `dotnet build PteFloatingSentence.sln --configuration Release --no-restore`
- [ ] Run `git diff --check`.
- [ ] Update plan checkboxes and commit with `test: verify floating review practice mode`.
