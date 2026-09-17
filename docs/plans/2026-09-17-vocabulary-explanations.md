# Vocabulary Explanations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Add sentence-specific vocabulary capture, Gemini explanations, IPA pronunciation, hide/retry states, and native WPF selection behavior.

**Architecture:** Core owns vocabulary records, phrase normalization, duplicate rules, and optional persistence fields. Windows owns DPAPI key protection, the Gemini HTTP adapter, the explainer interface, and WPF rendering. The API is called only after a local vocabulary item is saved, and failures never remove that item.

**Tech Stack:** C# 13, .NET 9, WPF, System.Text.Json, HttpClient, Windows DPAPI ProtectedData, MSTest.

## Global Constraints

- Work in C:\Dev\PteFloatingSentence on Windows 10 and Windows 11.
- Vocabulary is attached to an individual StudySentence.
- Double-click adds one word; drag selection adds a multi-word phrase.
- Meaning and example are simple English; pronunciation is IPA text.
- Persist locally before network calls; never log or display the API key.
- Do not implement quiz, scoring, global vocabulary, spaced repetition, audio, translation, tags, import, or export.
- Core cannot reference WPF or network APIs.

## Planned Files

~~~text
src/PteFloatingSentence.Core/
  StudyList.cs                 # VocabularyItem and sentence vocabulary collection
  VocabularyRules.cs           # phrase normalization and duplicate behavior
src/PteFloatingSentence.Windows/
  Infrastructure/GeminiVocabularyExplainer.cs
  Infrastructure/ProtectedApiKeyStore.cs
  Infrastructure/IVocabularyExplainer.cs
  VocabularyWorkflow.cs
  FloatingWindow.xaml/.cs
  SettingsWindow.xaml/.cs
  App.xaml.cs
tests/PteFloatingSentence.Core.Tests/VocabularyRulesTests.cs
tests/PteFloatingSentence.Windows.Tests/
  GeminiVocabularyExplainerTests.cs
  ProtectedApiKeyStoreTests.cs
  VocabularyWorkflowTests.cs
  WindowSurfaceTests.cs
~~~

### Task 1: Core vocabulary model and phrase rules

**Files:** modify StudyList.cs; create VocabularyRules.cs; add Core tests.

**Interfaces:** VocabularyItem has Id, Phrase, NormalizedPhrase, Meaning, Example, PronunciationIpa, Status, IsHidden, LastError. VocabularyRules exposes NormalizePhrase, ContainsEquivalent, CreatePending, and ValidatePhrase.

- [x] Write failing tests for one-word and multi-word whitespace normalization, duplicate detection ignoring case, rejection of empty/over-20-word phrases, and preservation of different phrases.
- [x] Run the focused Core test and verify it fails because the types are absent.
- [x] Implement immutable records and rules. Add an empty IReadOnlyList<VocabularyItem> Vocabulary to StudySentence; do not alter completion.
- [x] Run dotnet test tests\PteFloatingSentence.Core.Tests\PteFloatingSentence.Core.Tests.csproj; expect all Core tests to pass.
- [x] Commit with message feat: add vocabulary domain rules.

### Task 2: Persist vocabulary and protect the Gemini key

**Files:** modify AppSettings and JsonSettingsStore; create ProtectedApiKeyStore; add persistence tests.

**Interfaces:** ProtectedApiKeyStore(applicationName) exposes SaveAsync(string), LoadAsync(), and ClearAsync(). Settings expose only GeminiApiKeyConfigured or equivalent non-secret state; plaintext keys never enter AppSettings JSON.

- [x] Write failing tests for old settings without vocabulary, vocabulary round trip including IsHidden/Status, DPAPI key round trip, clear, missing key, and JSON text without a plaintext key.
- [x] Run focused tests and verify failure.
- [x] Implement optional vocabulary compatibility and DPAPI ProtectedData.Protect/Unprotect scoped to CurrentUser. Never write plaintext bytes.
- [x] Run all persistence tests and inspect serialized JSON for the absence of the key.
- [x] Commit with message feat: persist vocabulary and protect api key.

### Task 3: Gemini explainer and local-first workflow

**Files:** create IVocabularyExplainer, GeminiVocabularyExplainer, VocabularyWorkflow; add fake-client tests.

**Interfaces:** ExplainAsync(phrase, sourceSentence, cancellationToken) returns VocabularyExplanation. VocabularyWorkflow exposes AddAsync(sentence, selection), RetryAsync, and SetHidden. Gemini requests strict JSON fields meaning, example, pronunciationIpa.

- [x] Write failing tests for fake success, malformed response, timeout/network failure, pending retention, retry success, and hide/unhide persistence.
- [x] Run focused tests and verify failure.
- [x] Implement HttpClient adapter with cancellation, bounded response size, strict JSON deserialization, and concise Failed state. Never include the API key in exceptions or logs.
- [x] Run workflow and adapter tests; expect all to pass.
- [x] Commit with message feat: add Gemini vocabulary workflow.

### Task 4: Native word/phrase selection and vocabulary panel

**Files:** modify FloatingWindow.xaml/.cs, App.xaml.cs, SettingsWindow.xaml/.cs; add WPF contract tests.

**Interfaces:** FloatingWindow emits VocabularySelected(string selection) and VocabularyClicked(Guid itemId). ApplySettings renders the active sentence with vocabulary spans and a compact panel below. Hide and Retry route to VocabularyWorkflow without changing list navigation.

- [x] Write failing UI contract tests for a selectable RichTextBox/FlowDocument, phrase hover accent, vocabulary panel, Hide/Retry buttons, and no WebView2 dependency.
- [x] Run focused tests and verify failure.
- [x] Replace the sentence TextBlock with a read-only FlowDocument selection surface. Map TextPointer selections to normalized source text; double-click and drag-release raise VocabularySelected. Keep navigation and drag guard functional. Render stored phrases with accent text/underline and show meaning, example, IPA, loading, failed, and hidden states.
- [x] Add Settings API-key field and Save behavior that writes only through ProtectedApiKeyStore.
- [x] Wire App to load the key store, inject explainer/workflow, and re-render sentence-specific vocabulary. Run full tests/build.
- [x] Commit with message feat: add vocabulary selection and panel.

### Task 5: Verify privacy and user-visible behavior

**Files:** only files implicated by a failed check.

- [x] Run dotnet test PteFloatingSentence.sln --configuration Debug and dotnet build PteFloatingSentence.sln --configuration Debug --no-restore; record exact counts and warnings/errors.
- [x] With a fake explainer verify selection saves before explanation, duplicate selection is ignored, retry works, Hide survives restart, and navigation isolates vocabulary by sentence.
- [x] With a test Gemini key verify DPAPI storage, no key in settings JSON/logs/UI, strict meaning/example/IPA rendering, API failure recovery, and bounded panel layout. Restore user settings afterward.
- [x] Report only manual WPF checks actually observed; do not claim native selection or hover checks without evidence.

