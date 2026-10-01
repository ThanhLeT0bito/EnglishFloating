# Audio practice modes and TTS hardening

## Scope

This extends the Windows `feature/sentence-audio-tts` branch. macOS work is out of scope. The floating sentence's existing speaker button remains available in normal reading mode. Practice in both the floating widget and Settings uses the same rules, controls, and feedback.

## TTS review fixes

- A cache miss has a bounded synthesis attempt (20 seconds total, including WebSocket connect and receive). The timeout is linked to user cancellation. Timeout, offline, and protocol failure stop loading, release the socket, keep the current sentence, and show a short retryable error; the next click retries. No automatic duplicate request is made.
- The cache limit is fixed at 20 MB. Remove the unused `TtsMaxCacheSizeBytes` model/draft/merge field; older JSON containing it remains readable because unknown fields are ignored. The cache manager and Settings label use the same constant. Voice and speed remain persisted preferences.
- Playback and pending synthesis stop when changing the sentence, list, practice mode, or overlay visibility, or closing the window. A late response must not play after any of those changes.

## Practice modes

Settings > Practice has one app-wide saved choice, defaulting to **Text hints** for existing installations:

1. **Text hints**: keep the current deterministic masked-word projection. Visible words remain visible; the learner enters hidden words in sequence. Answered words appear only after being entered correctly.
2. **Listen & write**: do not render any sentence text, punctuation, word count, token boxes, or other answer-length clues before completion. Show a speaker button and one free-form input for the entire sentence. The learner may replay or stop audio. Audio never starts automatically.

The speaker button is always shown in both practice modes and on both surfaces (floating widget and Settings Practice). It is disabled only when the selected list has no sentence. It uses the saved voice/speed and the same TTS/cache/playback behavior as normal reading. When switching modes, clear any partial entry and stop audio while preserving the selected list and sentence.

## Answer and progress rules

- Text hints retains the existing word-level comparison and completion behavior.
- Listen & write compares the complete sentence after collapsing whitespace and removing punctuation around words; comparison ignores letter case but preserves word order, spelling, and internal apostrophes. Empty or partial answers are incorrect. An incorrect answer displays a generic retry prompt without revealing the expected text or marking the sentence done.
- Only a correct answer marks the sentence complete. On success, stop audio and advance to the next sentence; after the last sentence, show the completion state. Manual Previous/Next and Restart stay available. No Show answer control is offered in either surface, because it would conflict with the correct-answer completion rule.
- Existing completion storage remains list/sentence based. The selected mode does not create a second completion record; switching modes does not silently mark or unmark sentences.

## Shared presentation and state

Use one practice interaction component for both hosts, with a compact floating layout and a Settings-width layout. The component owns projection/input rendering, audio button state, answer submission, feedback, and navigation commands. The existing `ReviewPracticeSession` remains the source of sentence progress, with a new domain-level whole-sentence answer rule for Listen & write. Settings owns list selection and mode selection; floating receives the saved mode and selected list from app settings. Both hosts share one audio workflow service rather than duplicate WebSocket/cache/playback state machines.

Changing the saved mode in Settings updates its practice preview immediately and is committed through the existing Save flow. After Save, the floating practice view picks up the mode without exiting the current list. Both hosts must show matching progress labels, speaker behavior, error messages, completion state, and navigation behavior, while adapting only spacing and size.

## Verification

- Domain tests for whole-sentence comparison: case, whitespace, outer punctuation, internal apostrophes, missing/reordered/misspelled words, and empty input.
- TTS tests for timeout, cancellation, late-response suppression, retry, cache hit/miss, and consistent 20 MB limit.
- UI/workflow tests for both practice modes on floating and Settings, no answer leakage in Listen & write, speaker visibility and playback, mode switch reset, auto-advance, and identical completion behavior.
- Full Windows test suite and Release build must pass. Live TTS smoke testing is useful but must not be the sole proof because the external endpoint can change independently of the app.
