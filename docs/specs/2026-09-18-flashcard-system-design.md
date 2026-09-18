# Flashcard System Design

## Goal

Add a flashcard system that studies vocabulary collected from sentence lists and vocabulary entered manually into custom decks. Flashcards can run inside Settings or in a separate always-on-top floating window. AI-generated images are explicitly excluded from this phase.

## Product Scope

The first release supports two deck types:

1. **Study-list decks** are derived automatically from vocabulary attached to sentences in an existing study list.
2. **Custom decks** are created and maintained manually by the user.

The release includes card browsing, front/back flipping, lightweight learning states, progress persistence, custom-deck CRUD, and an independently controlled floating flashcard window.

The release does not generate, download, display, or manage AI images. The data model may reserve nullable image metadata for backward-compatible expansion, but no image behavior is exposed in the UI.

## Core Concepts

### Study-list deck

A study-list deck is a read-only projection of sentence vocabulary. It is not persisted as a second copy of vocabulary data.

- Its stable deck ID is derived from the study-list ID.
- It includes vocabulary from every sentence in that list.
- Hidden vocabulary remains excluded from the deck.
- Pending or failed vocabulary explanations may appear as unavailable cards in deck management, but the study session includes only cards that have usable content.
- Duplicate vocabulary is merged using `VocabularyRules.CleanPhrase` / normalized phrase matching.
- A merged card preserves every distinct source sentence ID and source sentence text.
- The primary meaning, example, and pronunciation come from the first ready vocabulary item in sentence order.
- Removing a vocabulary item from every source sentence removes it from the derived deck. Existing progress can remain orphaned until cleanup, but must not produce a visible card.

### Custom deck

A custom deck is persisted independently of study lists.

- User-defined name and stable GUID.
- Contains manually managed vocabulary cards.
- Custom cards have phrase, normalized phrase, optional pronunciation, meaning, example, and optional future image metadata.
- Duplicate normalized phrases are rejected within the same custom deck.
- The same phrase may exist in different custom decks.
- Deleting a custom deck deletes its cards and deck-scoped progress after confirmation.

### Flashcard identity

Each visible flashcard must have a stable progress key.

- Study-list deck key: `study-list:{StudyListId}`.
- Custom deck key: `custom:{CustomDeckId}`.
- Study-list card key: `study-list:{StudyListId}:{NormalizedPhrase}`.
- Custom card key: `custom:{CustomDeckId}:{CardId}`.

Deck and card keys are persisted as strings so display/session state remains separate from vocabulary ownership and can support future deck types. Renaming a list or custom deck does not change its key. Changing a study-list phrase so that its normalized value changes intentionally creates a new card identity; the old progress becomes orphaned and is eligible for cleanup.

## Data Model

### New Core records

`CustomVocabularyCard`

- `Guid Id`
- `string Phrase`
- `string NormalizedPhrase`
- `string? PronunciationIpa`
- `string Meaning`
- `string Example`
- `string? ImagePath` reserved for a later release; always null in this phase

`CustomFlashcardDeck`

- `Guid Id`
- `string Name`
- `IReadOnlyList<CustomVocabularyCard> Cards`

`FlashcardProgress`

- `string CardKey`
- `FlashcardLearningState State`
- `int ReviewCount`
- `int AgainCount`
- `FlashcardRating? LastRating`
- `DateTimeOffset? LastReviewedAt`

`FlashcardLearningState`

- `New`
- `Learning`
- `Remembered`

`FlashcardRating`

- `Again`
- `Hard`
- `Remembered`

### AppSettings additions

- `IReadOnlyList<CustomFlashcardDeck> CustomFlashcardDecks`
- `IReadOnlyList<FlashcardProgress> FlashcardProgress`
- `bool ShowFloatingFlashcard`, default false
- `string? ActiveFlashcardDeckKey`

Old settings JSON must load with empty custom decks/progress, floating flashcards disabled, and no active deck.

`ShowFloatingFlashcard` is a display preference, not session progress. Turning it off hides and disposes the floating window but preserves the active deck and all ratings. The current card index and front/back state are session-only and reset when a new study session starts or the app restarts.

## Projection and Deduplication

`FlashcardDeckProjection` converts current `AppSettings` into read-only deck summaries and cards.

For each study list:

1. Iterate sentences in their stored order.
2. Ignore vocabulary with `IsHidden=true`.
3. Normalize phrases with the existing vocabulary normalization rules.
4. Group by normalized phrase using ordinal case-insensitive comparison.
5. Select the first ready item as card content.
6. Append distinct source sentences in sentence order.
7. Generate the stable card key.

Custom decks bypass sentence projection but use the same presentation contract.

Ready study-list cards require a non-empty phrase, meaning, and example. Pronunciation is optional. A duplicate group with no ready item contributes to the unavailable count but does not enter a study session.

## Settings Center UX

Add a `Flashcards` navigation page.

The page has three areas:

1. **Deck sidebar**
   - Automatically generated study-list decks.
   - User-created custom decks.
   - Card count and remembered count.
   - New custom deck action.

2. **Deck content**
   - Search/filter by phrase.
   - Card rows showing phrase, meaning status, source count, and learning state.
   - Custom decks expose add/edit/delete actions.
   - Study-list decks are read-only and link conceptually back to sentence vocabulary management.

3. **Study action**
   - `Start studying` opens an in-page study view or shows the floating flashcard depending on user intent.
   - Empty decks display a clear empty state.

## Flashcard Study UX

### Front

- Phrase centered prominently.
- Pronunciation beneath the phrase when present.
- Deck name badge.
- Learning-state badge.
- No image placeholder in this phase; empty visual space must not be reserved.

### Back

- Phrase and pronunciation remain visible.
- Simple English meaning.
- Example sentence.
- For study-list cards, a collapsed source section shows one source sentence by default and can expand to all source sentences.

### Interaction

- Click card or press `Space` to flip.
- Left/Right arrows move previous/next.
- `Again`, `Hard`, and `Remembered` rate the card and move to the next card.
- Ratings are disabled until the back is visible, preventing accidental grading before recall.
- `Escape` closes the floating flashcard or exits the in-page study view.
- Session order is deterministic in phase one: stored/projection order. Randomization is deferred.
- Previous/next navigation wraps at the first and last cards so a small deck can be repeated continuously.
- Rating the final card wraps to the first card and increments the session-completed-cycle count shown only for the active session.
- Navigating to another card always resets that card to its front face.

### Rating transitions

- `Again`: state becomes `Learning`; increment review and again counts.
- `Hard`: state becomes `Learning`; increment review count.
- `Remembered`: state becomes `Remembered`; increment review count.
- Every rating updates `LastRating` and `LastReviewedAt`.
- Reviewing a remembered card with `Again` returns it to `Learning`.

## Floating Flashcard Window

The floating flashcard is a separate window from the floating sentence.

- Borderless, transparent-capable, always on top, and draggable.
- Rectangular card with a compact modern layout.
- Renders one card at a time; it must not build UI for the entire deck.
- Has previous/next controls, flip behavior, ratings, deck progress, and a Settings action.
- Can be enabled or disabled independently of the sentence overlay and vocabulary feature.
- Uses `ActiveFlashcardDeckKey` to determine its deck.
- Enabling the floating flashcard without a valid active deck opens Settings on the Flashcards page instead of showing an empty always-on-top window.
- If the selected deck disappears or becomes empty, the window hides and the launcher remains available.
- If sentence overlay is hidden but flashcard is visible, the launcher is not required because the flashcard exposes Settings.
- If every floating layer is hidden, the existing launcher is visible.

## Display Controller Changes

The current display controller must evolve from a single floating-window coordinator into a layer coordinator.

It controls:

- sentence overlay;
- vocabulary behavior within the sentence overlay;
- floating flashcard;
- fallback launcher.

Launcher visibility rule:

`launcherVisible = !sentenceVisible && !flashcardVisible`

Vocabulary enablement does not count as a separate window layer.

The controller must lazily create the floating flashcard window only when first enabled to avoid increasing idle memory.

## Performance and Memory

- Project only the selected deck for an active study session.
- Render only the current card in the floating window.
- Do not duplicate source vocabulary into persisted study-list deck records.
- Cache projection results using a signature derived from the selected list/card data; invalidate only when relevant vocabulary changes.
- Do not keep decoded image objects because images are not part of this phase.
- Unsubscribe window/page events on close or dispose.
- Closing Settings must release its Flashcards page visual tree when no window references remain.
- A deck with hundreds of cards uses virtualized list controls in management view.

## Persistence and Concurrency

- All updates continue through immutable `AppSettings` snapshots.
- Flashcard ratings use the latest settings snapshot and update progress by card key.
- Settings edits and floating ratings must use `SettingsUpdateMerger` rules so stale Settings windows cannot overwrite newer progress.
- Custom-deck changes persist through the existing settings queue.
- Corrupt or duplicate progress entries normalize to one entry per key, preferring the last valid entry.
- Orphaned progress is ignored during normal reads and removed during the next successful deck/vocabulary mutation; app startup must not rewrite settings solely to clean orphaned entries.

## Error and Empty States

- Study list has no vocabulary: `No vocabulary has been added to this list yet.`
- All vocabulary hidden: explain that hidden vocabulary is excluded.
- Vocabulary explanation pending/failed: exclude from study session and report unavailable count.
- Custom deck empty: show `Add your first card` action.
- Active floating deck deleted: hide flashcard and clear active deck key.
- Persistence failure: preserve current in-memory state and use the existing persistence behavior; do not crash the window.

## Testing Strategy

Core tests:

- Study-list projection and source aggregation.
- Duplicate merging.
- Hidden/pending/failed filtering.
- Stable card keys.
- Custom deck validation and normalization.
- Rating transitions and progress normalization.
- Legacy JSON compatibility.

Windows tests:

- Flashcards page navigation and deck selection.
- Custom deck/card CRUD flow.
- Front/back rendering.
- Keyboard and button interactions.
- Ratings disabled before flip.
- Floating visibility matrix with sentence, flashcard, and launcher.
- Missing/empty active deck behavior.
- Settings merge preserves concurrent rating updates.
- Window disposal/event cleanup.

Manual verification:

- Large study list scrolling.
- Floating card drag/topmost behavior.
- Enable/disable combinations.
- Open/close Settings repeatedly and observe working set stabilization.
- Restart and confirm selected deck and progress persistence.

## Deferred Features

- AI image generation and image caching.
- Manual image attachment.
- Audio pronunciation.
- Randomized study order.
- Spaced repetition scheduling.
- Cloud synchronization.
- Import/export custom decks.
- Multiple examples editable per custom card.
