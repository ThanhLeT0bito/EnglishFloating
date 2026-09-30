# AI Sentence Phrasing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a reviewed Gemini spelling and phrase-group proposal, then show wider gaps between groups in the floating sentence.

**Architecture:** Keep `StudySentence.Text` canonical and add optional one-based word-boundary metadata. Parse editable newline-separated groups in Core. Keep Gemini HTTP and review UI separate from the draft mutation; the dialog commits only after validation. The overlay uses the stored boundaries while vocabulary operations normalize visual whitespace.

**Tech Stack:** .NET 9, WPF, MSTest, Gemini generateContent JSON schema.

## Global Constraints

- Ordinary Add sentence remains offline and unchanged.
- AI never saves without explicit Confirm & add.
- At most 20 words; no slash markers in the floating display.
- Legacy settings load without phrase-break metadata.
- The existing protected Gemini key is reused.

---

### Task 1: Canonical phrase-group model

**Files:** Modify `src/PteFloatingSentence.Core/StudyList.cs`, `src/PteFloatingSentence.Core/StudyListRules.cs`, `src/PteFloatingSentence.Windows/StudyListDraft.cs`. Create `src/PteFloatingSentence.Core/SentencePhrasing.cs`. Test `tests/PteFloatingSentence.Core.Tests/SentencePhrasingTests.cs`, `tests/PteFloatingSentence.Windows.Tests/SettingsWorkflowTests.cs`.

**Interfaces:** `SentencePhrasing.ParseGroups(string) -> SentencePhrasingResult` with `Text`, `BreakAfterWordIndices`, `ValidationResult`; `StudyListDraft.AddPhrasedSentence(string) -> ValidationResult`.

- [x] Write failing tests for group conversion, validation, legacy loading, normalization and manual edit clearing breaks.
- [x] Run focused `dotnet test` filters and confirm missing behavior fails.
- [x] Implement optional `PhraseBreakAfterWordIndices` on `StudySentence`, parser, normalization, draft add/update.
- [x] Run focused and full tests; commit `60c20ef`.

### Task 2: Gemini proposal and review-before-add

**Files:** Create `src/PteFloatingSentence.Windows/Infrastructure/GeminiSentencePhraser.cs` and `ISentencePhraser.cs`; create `src/PteFloatingSentence.Windows/SentencePhrasingReviewWindow.xaml(.cs)`; modify `SetupPage.xaml(.cs)`, `SettingsWindow.xaml.cs`, `App.xaml.cs`. Test `tests/PteFloatingSentence.Windows.Tests/GeminiSentencePhraserTests.cs` and `SettingsWorkflowTests.cs`.

**Interfaces:** `ISentencePhraser.SuggestAsync(string, CancellationToken) -> Task<IReadOnlyList<string>>`; `SentencePhrasingReviewWindow` receives original and proposed groups, exposes confirmed edited groups. `SetupPage` raises an async AI-add request and only calls `AddPhrasedSentence` after confirm.

- [x] Write failing tests for missing key, valid JSON groups, invalid JSON/groups and for review cancel leaving draft unchanged; verify RED.
- [x] Implement bounded Gemini HTTP request using the configured key and structured JSON response. Require spelling/punctuation correction and natural grouping with original word order; validate returned groups before display.
- [x] Add Add with AI button, busy/error states, review window with original/proposed editor (one group per line), visual preview using wide gaps, Confirm & add/Cancel.
- [x] Integrate app key provider and preserve original input on error/cancel. Run tests; commit `b9860a3` (combined with overlay changes).

### Task 3: Overlay gaps and vocabulary interaction

**Files:** Modify `src/PteFloatingSentence.Windows/FloatingWindow.xaml.cs`, `RenderSignature.cs`; test `tests/PteFloatingSentence.Windows.Tests/WindowSurfaceTests.cs`, `DisplayControllerTests.cs` or focused new rendering tests.

**Interfaces:** `SentencePhrasing.GetDisplayText(string, IReadOnlyList<int>)` yields visual wide spaces; `RenderSignature.Create` gains optional boundaries. Vocabulary selection uses `VocabularyRules.NormalizePhrase` before lookup/add.

- [x] Write failing tests for visual gaps, signature difference on different boundaries, and multiword vocabulary across a gap; verify RED.
- [x] Render boundaries in FlowDocument without altering canonical text, preserve vocabulary span highlighting, and normalize selected whitespace.
- [x] Run full Release tests and publish Win-x64. Inspect WPF-rendered screenshots of review and overlay; commit `b9860a3`.

### Task 4: Final verification

- [x] Compare implementation against every spec section, inspect git diff, run `git diff --check`.
- [x] Run full Release tests fresh and publish a test executable.
- [ ] Report executable path, test counts, and macOS branch handoff status.

## Verification evidence

- Release test suite: 67 Core and 173 Windows tests passed (240 total).
- Publish: `release/EnglishFloating.exe`, Windows x64 self-contained single file.
- Visual check: rendered the review dialog and floating overlay with the sample three-group sentence; spacing and layout inspected.
- Live Gemini check: `I usually go to the gym after work with my freinds.` produced the three expected groups and corrected `friends`; no key written to output.
- Independent review of `2e365f6..b9860a3`: no actionable findings, ready to merge.
- macOS handoff: shared schema `60c20ef` and portable service/render helpers `b9860a3` sent to the macOS agent. Real Mac runtime acceptance remains separate from Windows-side build verification.
