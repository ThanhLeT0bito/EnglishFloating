# Floating Review Practice Mode Design Specification

## Goal & Background

Previously, Review Practice was integrated into a sub-page within the Settings Center window. The intended user experience, however, is that practicing sentences happens directly on the desktop overlay (`FloatingWindow`), replacing the static floating sentence with an interactive masked-sentence card. The Settings Center serves only to configure study lists and trigger practice mode.

## User Flow & Entry Points

1. **Starting Practice from Settings:**
   - On the **Setup** page in `SettingsWindow`, the selected study list has a **Start Practice** button (e.g. next to the list title or action buttons).
   - Clicking "Start Practice" commits any pending draft changes, activates Practice Mode for the selected list on `FloatingWindow`, and closes (or minimizes) the Settings window.

2. **Starting / Exiting Practice from Floating Window Context Menu:**
   - Right-clicking `FloatingWindow` displays:
     - In Normal Mode: `"Start Practice"`, `"Settings"`, `"Exit"`.
     - In Practice Mode: `"Exit Practice"`, `"Settings"`, `"Exit"`.

3. **Exiting Practice Mode:**
   - Clicking the compact `✕` button on the practice header or choosing `"Exit Practice"` from the context menu exits Practice Mode.
   - `FloatingWindow` restores the standard read-only sentence view with vocabulary overlay cards.

## UI & Interaction Design on FloatingWindow

### Layout in Practice Mode

Within `SentenceCard`:
1. **Top Bar / Header:**
   - Progress text: e.g. `Practice · Sentence 2 of 10` (or `Sentence 2 / 10 · 1 completed`).
   - Compact Exit button `✕` on the top-right of the card to quickly return to normal display.
2. **Interactive Sentence Projection:**
   - Replaces the read-only `RichTextBox` with a horizontal wrapping panel (`WrapPanel`).
   - **Visible Tokens:** Rendered as clean, readable `TextBlock` elements with font size matching the user's configured settings, preserving original punctuation and spacing.
   - **Hidden Tokens:** Rendered as inline `TextBox` controls:
     - Default/unfocused state with no input displays placeholder `_`.
     - Focused state: clears placeholder, highlights border with accent color.
     - Past-answered / correct state: displays correct word in emerald green (`#34D399`), disabled from editing.
3. **Navigation & Hover Controls:**
   - The existing circle `PreviousButton` and `NextButton` appear on card hover, allowing skipping forward or backward between sentences in the practice list without losing progress.
4. **Vocabulary Panel:**
   - Hidden during Practice Mode to prevent distractions and spoiler answers.

### Answer Checking & Progression

- **Trigger:** User presses `Enter` inside the active hidden-word `TextBox` (or clicks an inline check button).
- **Validation Rule:** Uses `ReviewPracticeRules.CheckAnswer`:
  - Trims surrounding whitespace and boundary punctuation (`.,!?;:"'()[]{}-—`).
  - Case-insensitive comparison.
- **On Correct Answer:**
  - The `TextBox` displays the correct word, turns emerald green, and becomes read-only.
  - If more hidden words remain: keyboard focus automatically moves to the next hidden word `TextBox` and selects its text.
  - If all hidden words in the sentence are answered correctly:
    - The sentence is marked `IsCompleted = true`.
    - Progress is saved to `settings.json`.
    - The card briefly displays completion feedback and automatically transitions to the next incomplete sentence.
- **On Incorrect Answer:**
  - The active `TextBox` border highlights red, with a subtle inline hint ("Try again.").
  - Focus remains on the same `TextBox`, and its text is highlighted for immediate correction.

### All Sentences Completed State

- If all sentences in the list are completed:
  - Displays a celebration badge: `All sentences completed!` with an option button: `Restart Practice` or `Exit Practice`.

## State & Architecture

1. **`DisplayController` / `FloatingWindow` State:**
   - `FloatingWindow` maintains an active `ReviewPracticeSession?`.
   - When active, `FloatingWindow` renders in practice mode.
   - Subscribes to sentence completion and dispatches settings save via the existing `_saveSettings` action.
2. **Settings Window Updates:**
   - The Setup page provides the "Start Practice" trigger button.
   - Settings window raises a request / event (or calls `DisplayController.StartPractice(listId)`) to launch practice.
   - The sub-page `ReviewPracticePage` inside `SettingsWindow` can be streamlined or refactored into this floating overlay flow.

## Memory Leak & Event Lifecycle Guarantees

- Dynamic TextBox creation inside the practice projection panel uses routed container-level events (`KeyDown`, `GotFocus`, `LostFocus`) on the WrapPanel rather than individual per-box subscriptions.
- Switching sentences or exiting practice cleans up any timers or active routed handlers.
- When `FloatingWindow` closes, any active `ReviewPracticeSession` is cleanly detached.

## Verification Plan

### Automated Tests
1. **Unit Tests (Core & Windows):**
   - Core contracts and rules (`ReviewPracticeRules`, `ReviewPracticeSession`) remain verified.
   - Verify `FloatingWindow` entering and exiting Practice Mode transitions state correctly.
   - Verify answering all words marks sentence complete and invokes settings save.
   - Verify `PreviousButton` / `NextButton` navigation in practice mode updates the displayed sentence.
2. **Surface & Workflow Tests:**
   - Surface tests proving `FloatingWindow` renders TextBoxes for hidden words in Practice Mode and restores `RichTextBox` upon exit.
   - Workflow test proving "Start Practice" from Settings / ContextMenu triggers practice mode on the floating card.
