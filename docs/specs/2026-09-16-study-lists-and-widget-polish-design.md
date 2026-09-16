# Study Lists and Widget Polish Design

## Goal

Evolve the single-sentence Windows widget into a lightweight PTE study-set tool. The user creates named lists such as `Daily 10`, assigns each list its own sentence target, navigates its sentences from the floating widget, and manages content in a modern Settings workspace.

This release does not test recall. A later test feature will reveal one or two cue words, accept typed full-sentence answers, and be the only feature allowed to mark a sentence done.

## Scope

### Floating widget

- Retain borderless, transparent, draggable, topmost, taskbar-free behavior.
- Replace the plain panel with a dark surface and a subtle blue-to-mint luminous border. The border must be readable without looking like a strong neon effect.
- Display the active list name and a position label, for example `Daily 10 · 3 / 10`.
- Keep navigation controls hidden until pointer hover. On hover, show left and right arrow buttons inside the widget.
- Left moves to the previous sentence; right moves to the next sentence. At the first or last sentence, the buttons wrap around to the last or first sentence respectively.
- Navigation changes only the active sentence index. It must not set any completion state.
- Dragging starts only from the non-button portion of the widget, so arrows remain clickable.
- Right-click remains the access point for `Settings` and `Exit`.

### Study lists

- A study list has a stable ID, a user-editable name, a positive sentence target, an ordered collection of sentences, and a current sentence index.
- The active list is the only list displayed by the widget.
- The target is a planning/display goal, not a hard limit on stored sentences. A list may contain more or fewer sentences than its target. The widget navigates every stored sentence in the active list.
- The default migrated list is named `My first list`, has target `10`, and contains the legacy saved sentence.
- An empty list has no widget sentence. The widget shows `Add a sentence in Settings.` and disables its arrows.
- Switching active lists selects its remembered current sentence index after clamping it to the available range.

### Settings workspace

- Replace the small form with one modern Settings window containing a list sidebar and editor area.
- The sidebar shows each study list with its name and stored-sentence count, indicates the active list, and provides `New list`.
- Users can rename the selected list, set its target, activate it, or delete it. Deleting requires confirmation and is unavailable when it would remove the final remaining list.
- The editor shows an ordered, to-do-like sentence list. It supports adding, editing, and deleting sentences; the selected editor item becomes the widget's current sentence.
- Each saved sentence must continue to meet the existing maximum of 20 whitespace-separated words. Invalid additions or edits show an inline message and do not change saved data.
- `Save changes` validates the full draft and atomically replaces the active app state; `Cancel` discards all draft changes.

## Data Model and Migration

Replace the single `Sentence` field in persisted settings with version 2 data:

```text
AppSettings
  Version = 2
  ActiveListId
  StudyLists[]
    Id
    Name
    TargetSentenceCount
    CurrentSentenceIndex
    Sentences[]
      Id
      Text
      IsCompleted = false
```

Existing window-style and placement fields remain unchanged. On load, version 1 data migrates in memory to version 2 using the legacy sentence as the first sentence in `My first list`; the next successful save writes version 2 JSON. Missing, malformed, duplicate, or invalid list fields fall back to safe defaults. Completion data is persisted now but is neither editable nor changed by this release.

## Architecture

- `PteFloatingSentence.Core` owns immutable study-list models, list validation, navigation/wrap rules, migration, and settings normalization. It remains WPF-free so future mobile clients can reuse it.
- `JsonSettingsStore` serializes version 2 settings and delegates normalization/migration to Core.
- `SettingsWindow` edits an isolated draft and returns one validated `AppSettings` value on Save.
- `App` owns the active app state, applies it to the floating window, serializes persistence through the existing queue, and merges position updates without overwriting list changes.
- `FloatingWindow` renders only the active sentence and exposes navigation events; it does not own list state or persistence.

## Error Handling

- An invalid list name, target below 1, empty sentence, or sentence over 20 words prevents Save and identifies the relevant item.
- If list data cannot be loaded, restore the safe default list instead of failing startup.
- If the active ID is missing, select the first valid list.
- If a stored current index is invalid, clamp it to the list range; use zero for non-empty lists and no active sentence for empty lists.
- Persistence failures remain non-fatal and are handled by the existing serialized persistence queue.

## Testing and Acceptance

Automated tests cover version-1 migration, list/default normalization, active-list fallback, sentence target validation, next/previous wrapping, empty-list behavior, and no completion mutation on navigation. Existing JSON persistence tests are updated for the version-2 schema.

Manual Windows checks cover the hover-only arrows, arrow clicks not initiating drag, soft-border legibility over light and dark apps, list CRUD, selection synchronization between Settings and widget, persistence after restart, and wrap-around navigation.

## Explicitly Deferred

- Masked cue-word test mode
- Typing/scoring a recalled sentence
- Marking sentences done
- Daily automatic generation from a large sentence bank
- Tags, RS/WFD filters, audio, recording, and cloud sync
- Reordering sentences or lists by drag-and-drop

