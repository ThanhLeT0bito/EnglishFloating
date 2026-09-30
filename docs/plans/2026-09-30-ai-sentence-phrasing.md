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

- [ ] Write failing tests for groups `I usually go to the gym\nafter work\nwith my friends.` yielding breaks `[6,8]`, rejection of empty/over-20 input, legacy no-break loading, invalid-break normalization, and manual edit clearing breaks.
- [ ] Run focused `dotnet test` filters and confirm missing behavior fails.
- [ ] Implement optional `PhraseBreakAfterWordIndices` on `StudySentence`, parser, normalization, draft add/update.
- [ ] Run focused and full tests; commit `feat: store sentence phrasing boundaries`.

### Task 2: Gemini proposal and review-before-add

**Files:** Create `src/PteFloatingSentence.Windows/Infrastructure/GeminiSentencePhraser.cs` and `ISentencePhraser.cs`; create `src/PteFloatingSentence.Windows/SentencePhrasingReviewWindow.xaml(.cs)`; modify `SetupPage.xaml(.cs)`, `SettingsWindow.xaml.cs`, `App.xaml.cs`. Test `tests/PteFloatingSentence.Windows.Tests/GeminiSentencePhraserTests.cs` and `SettingsWorkflowTests.cs`.

**Interfaces:** `ISentencePhraser.SuggestAsync(string, CancellationToken) -> Task<IReadOnlyList<string>>`; `SentencePhrasingReviewWindow` receives original and proposed groups, exposes confirmed edited groups. `SetupPage` raises an async AI-add request and only calls `AddPhrasedSentence` after confirm.

- [ ] Write failing tests for missing key, valid JSON groups, invalid JSON/groups and for review cancel leaving draft unchanged; verify RED.
- [ ] Implement bounded Gemini HTTP request using the configured key and structured JSON response. Require spelling/punctuation correction and natural grouping with original word order; validate returned groups before display.
- [ ] Add Add with AI button, busy/error states, review window with original/proposed editor (one group per line), visual preview using wide gaps, Confirm & add/Cancel.
- [ ] Integrate app key provider and preserve original input on error/cancel. Run tests; commit `feat: review AI sentence phrasing before add`.

### Task 3: Overlay gaps and vocabulary interaction

**Files:** Modify `src/PteFloatingSentence.Windows/FloatingWindow.xaml.cs`, `RenderSignature.cs`; test `tests/PteFloatingSentence.Windows.Tests/WindowSurfaceTests.cs`, `DisplayControllerTests.cs` or focused new rendering tests.

**Interfaces:** `SentencePhrasing.GetDisplayText(string, IReadOnlyList<int>)` yields visual wide spaces; `RenderSignature.Create` gains optional boundaries. Vocabulary selection uses `VocabularyRules.NormalizePhrase` before lookup/add.

- [ ] Write failing tests for visual gaps, signature difference on different boundaries, and multiword vocabulary across a gap; verify RED.
- [ ] Render boundaries in FlowDocument without altering canonical text, preserve vocabulary span highlighting, and normalize selected whitespace.
- [ ] Run full `dotnet test PteFloatingSentence.sln -c Release`, `dotnet publish` Win-x64, and inspect manual overlay if available; commit `feat: display sentence reading groups`.

### Task 4: Final verification

- [ ] Compare implementation against every spec section, inspect git diff, run `git diff --check`.
- [ ] Run full Release tests fresh and publish a test executable.
- [ ] Report executable path, test counts, and macOS branch handoff status.
