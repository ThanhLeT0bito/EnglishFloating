# Vocabulary Explanations Design

## Goal

Allow a learner to select a word or phrase directly in the floating PTE sentence, save it as vocabulary attached to that sentence, and receive a short Gemini explanation with an English example and IPA pronunciation.

This release focuses only on capture, explanation, display, hide, and retry. Quiz, scoring, audio, and global vocabulary are deferred.

## User Interaction

- The sentence is rendered by a native WPF text-selection surface.
- Double-clicking a word selects and adds that word.
- Click-dragging selects a multi-word phrase and adds it when the selection ends.
- Selection is trimmed and internal whitespace is collapsed before persistence.
- Selecting the same normalized phrase again for the same sentence does not create a duplicate.
- A saved vocabulary phrase is rendered with an accent color. Hovering it changes its color/underline; clicking it opens its explanation panel.
- A new vocabulary item is persisted locally before Gemini is called.
- While Gemini is processing, the panel shows a loading state.
- `Hide vocabulary` hides explanation cards from the floating widget without deleting the saved items.
- API failure preserves the item and shows `Retry`; retry replaces only the explanation payload when successful.

## Vocabulary Model

Each `StudySentence` owns zero or more `VocabularyItem` records:

```text
VocabularyItem
  Id
  Phrase
  NormalizedPhrase
  Meaning
  Example
  PronunciationIpa
  Status (Pending, Ready, Failed)
  IsHidden
  LastError
```

The phrase is sentence-specific because its meaning can depend on context. `NormalizedPhrase` is used only for duplicate detection and is not displayed. `Meaning` is one short sentence in simple English. `Example` is one new English sentence. `PronunciationIpa` is IPA text such as `/əˈplaɪ/`.

## Gemini Integration

- Add a Gemini API key field to Settings.
- Protect the key with Windows DPAPI before writing it to the local settings file; never log or render the key.
- Keep the provider behind an interface such as `IVocabularyExplainer.ExplainAsync(string phrase, string sourceSentence, CancellationToken)`, allowing tests to use a fake and future providers to be added.
- Send the phrase and full source sentence as context and request strict JSON with exactly `meaning`, `example`, and `pronunciationIpa` string fields.
- Validate the response locally: all fields must be non-empty, meaning must be short, and no untrusted markdown/HTML may be inserted into the WPF visual tree.
- Network, key, timeout, malformed JSON, and quota errors become `Failed` with a concise user-facing retry message. They do not crash the overlay or remove the local item.

## Display

- The existing sentence card remains the primary content.
- A vocabulary panel appears beneath it only when vocabulary is visible and the selected sentence has items.
- Each card shows phrase, IPA, simple meaning, example, and a `Hide vocabulary` action.
- The panel must remain compact enough not to obscure the sentence; it may wrap and increase widget height.
- Navigation to another sentence refreshes the panel from that sentence's vocabulary only.

## Data and Migration

- Extend the version-2 study sentence model with a vocabulary collection without changing existing list IDs, sentence IDs, current indexes, or completion state.
- Existing settings load with empty vocabulary collections.
- The next successful save writes the extended version-2 schema; vocabulary is an optional field, so no version bump is needed.
- Malformed vocabulary records are ignored individually while the containing sentence and list remain usable.

## Testing and Acceptance

Automated tests cover phrase normalization and duplicate detection, word versus multi-word capture, vocabulary persistence, migration of sentences without vocabulary, DPAPI key round-trip, fake explainer success/failure/malformed responses, and hidden-state persistence. UI contract tests cover the selection surface, vocabulary panel, retry/hide actions, and sentence-specific refresh.

Manual Windows checks cover double-click, drag selection, accent hover, panel layout, retry after an API error, hidden-state behavior, navigation isolation, and ensuring the API key never appears in logs or visible UI text.

## Deferred

- Vocabulary quiz and answer scoring
- Global vocabulary across sentences/lists
- Spaced repetition
- Audio pronunciation or text-to-speech
- Translation, synonyms, tags, and import/export
