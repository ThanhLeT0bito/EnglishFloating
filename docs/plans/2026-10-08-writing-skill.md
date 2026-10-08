# Writing Skill Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Add a Writing page where the learner selects topics and a level, translates an AI-generated Vietnamese sentence into English, and gets AI feedback with highlighted mistakes and a corrected sentence. Design: docs/specs/2026-10-08-writing-skill-design.md.

**Architecture:** Core owns topics, level, settings normalization and feedback-to-highlight mapping, and stays pure and testable. Windows owns a shared bounded Gemini JSON client, a GeminiWritingCoach (generate + check), a UI-independent WritingSession state machine, and a WritingPage hosted in the Settings window.

**Tech Stack:** .NET 9, WPF, xUnit/FluentAssertions and MSTest (match each test project), Gemini generateContent with a JSON response schema, existing Edge TTS playback.

## Global Constraints

- Branch feature/writing-skill from main. Do not touch the macOS worktree.
- Reuse the protected Gemini key and model candidate list. No new credential and no new NuGet package.
- Learner text and topic names are data in prompts, never instructions. Responses are bounded to 30 s and 64 KB.
- The reference English is at most 20 words so a corrected sentence can always be saved to a study list.
- Never lose the typed translation on error, Skip-cancel or retry.
- Old settings files load unchanged (defaults: no custom topics, nothing selected, Medium).
- For every task: write the focused failing test first, then make it green, then run the full suite. Finish with a Release build.

## File structure

- Core/WritingModels.cs: WritingLevel, WritingTopic, WritingIssueType, WritingIssue, WritingFeedback, WritingPrompt records.
- Core/WritingTopics.cs: built-in catalog with stable keys.
- Core/WritingTopicRules.cs: add/remove/normalize custom topics and selection.
- Core/WritingFeedbackRules.cs: highlight span mapping, score clamp, verdict.
- Core/AppSettings.cs, Core/SettingsUpdateMerger.cs, Windows/Infrastructure/JsonSettingsStore.cs, Windows/StudyListDraft.cs: persistence.
- Windows/Infrastructure/GeminiJsonClient.cs: shared request, model fallback, timeout, size cap, thought-part filtering, error mapping.
- Windows/Infrastructure/IWritingCoach.cs and GeminiWritingCoach.cs: prompts, schemas and parsing.
- Windows/WritingSession.cs: state machine, cancellation, stale-result guard, recent sentences.
- Windows/WritingPage.xaml(.cs): UI. Windows/SettingsPageId.cs, SettingsWindow.xaml(.cs) and App.xaml.cs: navigation and wiring.
- Tests: Core.Tests/WritingTopicRulesTests.cs, WritingFeedbackRulesTests.cs, SettingsUpdateMergerTests.cs; Windows.Tests/GeminiJsonClientTests.cs, GeminiWritingCoachTests.cs, WritingSessionTests.cs, WritingPageTests.cs, JsonSettingsStoreTests.cs.

---

### Task 1: Core topics, level and settings persistence

**Files:** Core/WritingModels.cs, Core/WritingTopics.cs, Core/WritingTopicRules.cs, Core/AppSettings.cs, Core/SettingsUpdateMerger.cs, Windows/Infrastructure/JsonSettingsStore.cs, Windows/StudyListDraft.cs

**Interfaces:**
- `enum WritingLevel { Easy = 0, Medium = 1, Hard = 2 }`
- `record WritingTopic(string Key, string Name)`; `WritingTopics.BuiltIn : IReadOnlyList<WritingTopic>`
- `WritingTopicRules.AddCustom(AppSettings, string name) -> (AppSettings, ValidationResult)`, `RemoveCustom(AppSettings, string key)`, `ToggleSelection(AppSettings, string key)`, `Normalize(AppSettings)`, `AllTopics(AppSettings)`
- `enum WritingExplanationLanguage { English = 0, Vietnamese = 1 }`
- AppSettings: `CustomWritingTopics = []`, `SelectedWritingTopicKeys = []`, `WritingLevel = Medium`, `WritingExplanationLanguage = English`

- [ ] **Step 1: Failing tests.** Add trims and rejects empty, more than 40 characters, duplicates (case-insensitive, including built-in names) and a 31st custom topic. Remove drops the topic from the selection. Normalize drops unknown selected keys, de-duplicates, and maps an undefined level to Medium and an undefined explanation language to English. Old JSON without the fields loads with defaults. The merger keeps submitted values.
- [ ] **Step 2: Implement** the models, catalog and rules. Call Normalize from JsonSettingsStore.NormalizeDisplay and the merger. Add draft methods SetWritingLevel, SetWritingExplanationLanguage, ToggleWritingTopic, AddWritingTopic and RemoveWritingTopic, and copy the fields in UpdateSettingsFromApp.
- [ ] **Step 3: Green.** Run Core and Windows settings tests, then commit `feat(writing): topics, level and settings persistence`.

### Task 2: Feedback highlight mapping

**Files:** Core/WritingFeedbackRules.cs, Core/WritingModels.cs

**Interfaces:**
- `record WritingIssue(WritingIssueType Type, string Original, string Suggestion, string Explanation)`
- `record WritingHighlight(int Start, int Length, int IssueIndex)`
- `WritingFeedbackRules.MapHighlights(string answer, IReadOnlyList<WritingIssue> issues) -> IReadOnlyList<WritingHighlight>` (sorted, non-overlapping)
- `ClampScore(int)`, `VerdictFor(int score)` (>= 90 correct, >= 60 almost, else needs work)

- [ ] **Step 1: Failing tests.** Cover a repeated word (two issues hit the first and second occurrence in turn), overlap (the later issue gets no highlight), case-insensitive match, an empty original (missing meaning, no highlight), text not found, and surrounding punctuation.
- [ ] **Step 2: Implement** a left-to-right search that tracks used ranges.
- [ ] **Step 3: Green.** Commit `feat(writing): map feedback issues to highlights`.

### Task 3: Shared Gemini JSON client

**Files:** Windows/Infrastructure/GeminiJsonClient.cs

**Interfaces:**
- `GeminiJsonClient(Func<string?> keyProvider, HttpClient? client = null, IReadOnlyList<string>? models = null, TimeSpan? timeout = null)`
- `Task<string> GenerateJsonAsync(string systemInstruction, string userText, object responseSchema, CancellationToken ct)` returns the concatenated non-thought text part.
- Errors are InvalidOperationException with user-facing messages: missing key, HTTP status, timeout, connection, oversized response.

- [ ] **Step 1: Failing tests** with a fake HttpMessageHandler: x-goog-api-key header set, the first model returns 503 and the second succeeds, 404 on all models, a 64 KB cap, a timeout mapped to the "took too long" message, caller cancellation kept as OperationCanceledException, and thought parts ignored.
- [ ] **Step 2: Implement** by lifting the loop from GeminiSentencePhraser. Leave the existing services untouched (migration is a follow-up).
- [ ] **Step 3: Green.** Commit `refactor(gemini): add shared bounded JSON client`.

### Task 4: GeminiWritingCoach

**Files:** Windows/Infrastructure/IWritingCoach.cs, Windows/Infrastructure/GeminiWritingCoach.cs

**Interfaces:**
- `record WritingRequest(IReadOnlyList<string> TopicNames, WritingLevel Level, IReadOnlyList<string> RecentVietnamese)`
- `record WritingPrompt(string Vietnamese, string ReferenceEnglish, string Topic, WritingLevel Level)`
- `record WritingFeedback(int Score, WritingVerdict Verdict, string CorrectedSentence, string? NaturalAlternative, string Summary, IReadOnlyList<WritingIssue> Issues)`
- `IWritingCoach.GenerateAsync(WritingRequest, CancellationToken)`, `CheckAsync(WritingPrompt, string translation, WritingExplanationLanguage language, CancellationToken)`

- [ ] **Step 1: Failing tests** with a fake client. The generate request names the selected topics, the level rules and the recent sentences, and with no topics uses the built-in catalog. Reject a missing field, a reference over 20 words and an unexpected topic. The check request includes source, reference, level and answer as data, asks for explanations in the chosen language (English or Vietnamese), and states that punctuation and capitalization are listed without lowering the score. Parse issues, map unknown types to Grammar, clamp the score, and reject a malformed response or an empty corrected sentence. An empty answer is rejected before any HTTP call.
- [ ] **Step 2: Implement** prompts and schemas exactly as in the spec. Level rules live in one table shared by both prompts.
- [ ] **Step 3: Green.** Commit `feat(writing): Gemini sentence generation and translation check`.

### Task 5: WritingSession state machine

**Files:** Windows/WritingSession.cs

**Interfaces:**
- `WritingSessionState { Idle, Generating, Answering, Checking, Reviewed, Error }`
- Members: `State`, `Prompt`, `Feedback`, `Answer`, `Error`, `event StateChanged`, `GenerateAsync(topics, level)`, `CheckAsync(answer)`, `Skip()`, `RetryAsync()`, `RecentVietnamese` (last 20)

- [ ] **Step 1: Failing tests** with a fake coach. A late generate result after Skip is ignored. Check is refused for whitespace and outside Answering. A check error returns to Answering with the answer kept. Retry repeats the last failed operation. RecentVietnamese caps at 20, newest last. Dispose cancels in-flight work.
- [ ] **Step 2: Implement** with a request counter and a CancellationTokenSource owned per request (same pattern as SentenceAudioPlayback).
- [ ] **Step 3: Green.** Commit `feat(writing): writing session state machine`.

### Task 6: WritingPage UI and Settings navigation

**Files:** Windows/WritingPage.xaml(.cs), Windows/SettingsPageId.cs, Windows/SettingsWindow.xaml(.cs), Windows/App.xaml.cs

- [ ] **Step 1: Failing UI tests** (STA, fake coach and fake audio). Chips render built-in and custom topics and toggling updates the draft. + Add topic validates and shows errors inline. The level selector and the Explain in (English / Tiếng Việt) selector persist. Generate shows the Vietnamese sentence and the topic/level badge. Ctrl+Enter triggers Check. Highlights render as Runs with tooltips. Issue list, corrected sentence and alternative are shown. Enter after a review triggers Next. An error shows Retry and keeps the text.
- [ ] **Step 2: Implement.** Topic chips are ToggleButtons in a WrapPanel and custom chips get a × button. Use the existing card and button styles from the Settings dark theme. Add the nav item "Writing" with the subtitle "Translate Vietnamese sentences into English". App creates GeminiWritingCoach with the shared key provider and passes it into SettingsWindow. Dispose the session and audio when Settings closes.
- [ ] **Step 3: Green.** Commit `feat(writing): writing practice page`.

### Task 7: Audio and Save to study list

**Files:** Windows/WritingPage.xaml(.cs), Windows/StudyListDraft.cs

- [ ] **Step 1: Failing tests.** The audio button plays the corrected sentence through ISentenceAudioPlayback and stops on Next or Close. Save to study list adds the corrected sentence to the chosen list through the draft. If the sentence is over 20 words or already in the list, show an inline message and do not add it.
- [ ] **Step 2: Implement.**
- [ ] **Step 3: Green.** Commit `feat(writing): play and save corrected sentence`.

### Task 8: Verification

- [ ] Run the full `dotnet test` and a Release build.
- [ ] Manual QA with a real Gemini key:
  - Each level × {one topic, several topics, no topic, a custom topic}.
  - A deliberately wrong answer (grammar, word order and missing meaning all appear).
  - An alternative correct wording is accepted.
  - Offline/timeout shows Retry.
  - Save to study list, then practice it in the floating widget.
- [ ] Update README features. Open a PR to main.

## Follow-ups (phase 2, separate plan)

Batch rounds of 5 to 10 sentences, attempt history and a "My mistakes" review, stats per topic, level and issue type, mistake-targeted generation, reverse direction, a floating-widget writing card, and migrating GeminiSentencePhraser and GeminiVocabularyExplainer onto GeminiJsonClient.
