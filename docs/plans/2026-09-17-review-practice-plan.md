# Review Practice Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a review-practice mode where a selected study list presents each sentence with selected words replaced by one `_` placeholder, accepts one hidden word at a time, automatically advances after a correct answer, and marks the sentence complete only when all hidden words are correct.

**Architecture:** Keep the original sentence text immutable and build a review projection from it. A Core review engine will tokenize the sentence, choose hidden word indexes, produce display segments, and validate answers. A WPF `ReviewPracticePage` will render visible words and one input control per hidden word; the page reports progress to the Settings Center without calling Gemini. Completion is persisted through the existing `StudySentence.IsCompleted` field and the current settings save path.

**Tech Stack:** .NET 9, WPF, C#, MSTest, immutable Core records, existing `AppSettings` JSON persistence.

## Global Constraints

- The original `StudySentence.Text` must never be replaced by the masked projection.
- Each hidden word is represented by exactly one `_` placeholder; the placeholder must not reveal word length.
- Visible words preserve their original spelling and punctuation.
- Hidden answers are compared case-insensitively after trimming surrounding whitespace and punctuation that belongs to the token boundary.
- A wrong answer never marks a sentence complete and never advances to the next hidden word.
- A correct answer advances automatically to the next hidden word.
- `ShowVocabularyCards` and Gemini are unrelated to Review Practice; Review must work with vocabulary disabled and without an API key.
- All behavior changes require a failing test before production code.
- The first version is local and deterministic; no audio, spaced repetition, hints beyond the visible words, or remote calls.

---

### Task 1: Define review token and session contracts

**Files:**
- Create: `src/PteFloatingSentence.Core/ReviewPractice.cs`
- Modify: `src/PteFloatingSentence.Core/StudyList.cs`
- Modify: `tests/PteFloatingSentence.Core.Tests/ReviewPracticeTests.cs`

**Interfaces:**
- `public sealed record ReviewToken(string SourceText, string DisplayText, bool IsHidden, int SourceIndex)`.
- `public sealed record ReviewSentence(Guid SentenceId, string OriginalText, IReadOnlyList<ReviewToken> Tokens, IReadOnlyList<int> HiddenTokenIndexes)`.
- `public sealed record ReviewAnswerResult(bool IsCorrect, bool IsComplete, int NextHiddenTokenPosition, string? Error)`.
- `public static class ReviewPracticeRules` exposes:
  - `ReviewSentence CreateProjection(StudySentence sentence, int? seed = null)`.
  - `ReviewAnswerResult CheckAnswer(ReviewSentence review, int hiddenPosition, string answer)`.

- [ ] Write failing tests for tokenizing punctuation, preserving original text, and representing hidden words with exactly one `_` display token.
- [ ] Write a failing test for the example sentence:

```csharp
var review = ReviewPracticeRules.CreateProjection(
    new StudySentence(id, "You must wear a hard hat on the construction site"),
    seed: 7);

Assert.AreEqual("_ _ wear _ _ on _ _ site", review.DisplayText);
```

- [ ] Run `dotnet test tests/PteFloatingSentence.Core.Tests/PteFloatingSentence.Core.Tests.csproj --filter FullyQualifiedName~ReviewPractice` and verify failure because the review types do not exist.
- [ ] Implement immutable contracts and a tokenizer that treats whitespace-separated words as tokens while retaining punctuation in `SourceText`.
- [ ] Add a deterministic `DisplayText` projection that joins token `DisplayText` values with the original whitespace boundaries.
- [ ] Run focused tests and confirm they pass.
- [ ] Commit with `feat: define review practice projection contracts`.

### Task 2: Implement deterministic hidden-word selection

**Files:**
- Modify: `src/PteFloatingSentence.Core/ReviewPractice.cs`
- Modify: `tests/PteFloatingSentence.Core.Tests/ReviewPracticeTests.cs`

**Rules:**
- Do not hide every word.
- For 1–3 word sentences, hide one word when there is more than one word.
- For 4–8 words, hide approximately half, with at least one hidden word.
- For 9+ words, hide approximately 40–50%, with at least two hidden words.
- Always leave the final visible word when possible so the sentence has a recognizable ending.
- Use a seeded deterministic selection for tests and a stable sentence-ID-derived seed in production so reopening the same session does not reshuffle unexpectedly.
- Do not hide punctuation-only tokens.

- [ ] Add failing tests for 1-, 3-, 6-, and 10-word sentences and assert hidden index bounds.
- [ ] Add a test proving the same sentence ID/seed produces the same hidden indexes.
- [ ] Add a test proving a sentence with punctuation such as `"Really? Yes!"` keeps punctuation attached to its source token.
- [ ] Run focused tests and verify failure.
- [ ] Implement selection with a local `Random(seed)` and stable ordering; do not use global mutable random state.
- [ ] Run Core tests and verify no existing study-list behavior changes.
- [ ] Commit with `feat: select deterministic hidden review words`.

### Task 3: Implement answer checking and completion transitions

**Files:**
- Modify: `src/PteFloatingSentence.Core/ReviewPractice.cs`
- Modify: `src/PteFloatingSentence.Core/StudyListRules.cs`
- Modify: `tests/PteFloatingSentence.Core.Tests/ReviewPracticeTests.cs`
- Modify: `tests/PteFloatingSentence.Core.Tests/StudyListRulesTests.cs`

**Interfaces:**
- `CheckAnswer` validates only the requested hidden token.
- A correct answer returns the next hidden position, or `IsComplete=true` for the final token.
- A wrong answer returns `IsCorrect=false`, keeps the same hidden position, and returns a short user-facing error such as `Try again.`.
- Add `StudyListRules.MarkSentenceCompleted(AppSettings settings, Guid listId, Guid sentenceId, bool completed)` returning a new normalized `AppSettings`.

- [ ] Add failing tests for exact match, case-insensitive match, whitespace trimming, wrong answer, retry after wrong answer, and final-answer completion.
- [ ] Add a failing test that marking one sentence complete does not alter other sentences or the current list index.
- [ ] Run tests and confirm expected failures.
- [ ] Implement validation using normalized token text while preserving the original text for display.
- [ ] Implement immutable completion update through `with` expressions and list projections.
- [ ] Run Core tests and verify all pass.
- [ ] Commit with `feat: validate review answers and persist completion`.

### Task 4: Add Review Practice state and page view model

**Files:**
- Create: `src/PteFloatingSentence.Windows/ReviewPracticeSession.cs`
- Create: `src/PteFloatingSentence.Windows/ReviewPracticeViewModel.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/ReviewPracticeSessionTests.cs`

**Interfaces:**
- `ReviewPracticeSession` owns:
  - `StudyList List`.
  - `int SentenceIndex`.
  - `ReviewSentence CurrentReview`.
  - `int CurrentHiddenPosition`.
  - `bool IsComplete`.
  - `void Submit(string answer)` returning `ReviewAnswerResult`.
  - `void MoveNextSentence()` and `void MovePreviousSentence()`.
- Session construction selects the first incomplete sentence; if all are complete, it selects the first sentence and exposes an all-complete state.
- Session does not mutate settings directly; it emits a completion callback `(sentenceId, completed)` to the host.

- [ ] Add failing tests for selecting the first incomplete sentence, all-complete lists, answer progression, and sentence navigation resetting hidden position.
- [ ] Implement session state as a small testable class independent of WPF controls.
- [ ] Add a view model exposing display tokens, current input, progress text (`Sentence 2 of 10`), error text, and `CanSubmit`.
- [ ] Run Windows test project filtered to `ReviewPracticeSession` and verify pass.
- [ ] Commit with `feat: add review practice session state`.

### Task 5: Build the Review Practice WPF page

**Files:**
- Create: `src/PteFloatingSentence.Windows/ReviewPracticePage.xaml`
- Create: `src/PteFloatingSentence.Windows/ReviewPracticePage.xaml.cs`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/WindowSurfaceTests.cs`

**UI contract:**
- A study-list selector at the top.
- Progress text below it.
- Sentence projection rendered as a horizontal wrapping panel:
  - visible tokens are non-editable `TextBlock`s;
  - hidden tokens are one `TextBox`/placeholder control showing `_` until focused;
  - each hidden control maps to exactly one hidden token index.
- `Enter` submits the focused answer and automatically focuses the next hidden control after success.
- Wrong answer shows an inline error and keeps focus on the same control.
- `Show answer` reveals the current token but does not mark it correct or complete.
- `Next sentence` and `Previous sentence` navigate without mutating completion.

- [ ] Add failing surface tests for the Review Practice page, placeholder count, progress label, Check/Show answer controls, and all-complete empty state.
- [ ] Implement the page with existing dark Settings Center resources and no external UI library.
- [ ] Add a small completion panel with `Completed` state and a button to restart the list from the first sentence.
- [ ] Ensure keyboard focus is visible and the page remains usable at the Settings window minimum size.
- [ ] Run WPF surface tests and manually inspect the page with the example sentence.
- [ ] Commit with `feat: add review practice page`.

### Task 6: Integrate Review Practice with Settings and persistence

**Files:**
- Modify: `src/PteFloatingSentence.Windows/SettingsPageId.cs`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml.cs`
- Modify: `src/PteFloatingSentence.Windows/ReviewPracticePage.xaml.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/SettingsWorkflowTests.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/JsonSettingsStoreTests.cs`

**Interfaces:**
- Add `SettingsPageId.ReviewPractice` separately from read-only `Review`.
- SettingsWindow passes the current `AppSettings` snapshot and a save callback to the page.
- Completion callback updates the draft/settings through `StudyListRules.MarkSentenceCompleted`, then queues persistence through the existing app callback.

- [ ] Add failing tests that opening Review Practice for a selected list uses the correct list and current completion state.
- [ ] Add a test that completing the final hidden token updates `IsCompleted` in settings JSON after Save.
- [ ] Add a test that Cancel does not persist completion changes made in the unsaved session, unless the product explicitly chooses immediate progress persistence.
- [ ] Implement page navigation without instantiating a Gemini request or vocabulary workflow.
- [ ] Refresh Review summaries after practice completion.
- [ ] Run Settings workflow and persistence tests.
- [ ] Commit with `feat: integrate review practice into settings center`.

### Task 7: Verification and performance pass

**Files:**
- Modify: `tests/PteFloatingSentence.Core.Tests/ReviewPracticeTests.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/ReviewPracticeSessionTests.cs`
- Modify: `tests/PteFloatingSentence.Windows.Tests/WindowSurfaceTests.cs`
- Optional modify: `docs/plans/2026-09-17-review-practice-plan.md`

- [ ] Verify sentences with punctuation, repeated words, apostrophes, hyphenated words, and multiple spaces.
- [ ] Verify empty lists, one-sentence lists, and fully completed lists.
- [ ] Verify wrong answers do not advance and cannot mark completion.
- [ ] Verify `ShowVocabularyCards=false` does not affect Review Practice rendering or answer checking.
- [ ] Verify reopening the page recreates the same hidden-word pattern for the same sentence/list session seed.
- [ ] Run sequentially:
  - `dotnet test tests/PteFloatingSentence.Core.Tests/PteFloatingSentence.Core.Tests.csproj --configuration Debug --no-restore`
  - `dotnet test tests/PteFloatingSentence.Windows.Tests/PteFloatingSentence.Windows.Tests.csproj --configuration Debug --no-restore`
  - `dotnet build PteFloatingSentence.sln --configuration Release --no-restore`
- [ ] Run `git diff --check`, inspect memory while switching review sentences, and confirm no retained event handlers or per-keystroke `FlowDocument` rebuilds.
- [ ] Commit with `test: verify review practice mode`.

## Explicitly Deferred

- Per-character masking; this feature masks whole words with one `_` placeholder.
- Audio pronunciation.
- Gemini hints or generated explanations during review.
- Spaced repetition scheduling.
- Typo tolerance or fuzzy matching.
- Timed review sessions and score streaks.
