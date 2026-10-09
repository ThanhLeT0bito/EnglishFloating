# Writing practice v2: mistake notebook, retype, listen, dictation

Date: 2026-10-09 · Branch: `feature/writing-practice-v2` (from `feature/writing-skill`)

## Goals

Writing practice should train the learner's weak spots, not only grade random sentences. Four additions,
all available on the Settings → Writing page and in floating Writing:

1. **Mistake notebook and weak-point practice**
2. **Retype the corrected sentence**
3. **Listen to the corrected sentence**
4. **Dictation** (PTE "Write from Dictation")

## 1. Mistake notebook

- Every check records each issue except punctuation/capitalization (which never count as mistakes) in
  `%LOCALAPPDATA%\PteFloatingSentence\writing-history.json`. The newest 500 mistakes are kept.
- Each record: time, mode, issue type, **skill tag**, original, suggestion, explanation, source sentence
  (Vietnamese, or the English sentence in dictation), the learner's answer, corrected sentence.
- **Skill tags** are fine-grained grammar points: tense, articles, prepositions, subject–verb agreement,
  singular/plural, word form, word choice, word order, spelling, missing words, extra words, other.
  The check prompt asks the AI for a `tag` per issue; unknown or missing tags fall back from the issue type.
- **Statistics** cover the last 30 days, per tag: mistakes, passed weak-point sentences and a weight
  (mistakes − passes, at least 0). Weakest first.
- **Weak-point practice** (switch, off by default) needs ≥ 5 mistakes in 30 days. New sentences then target
  up to 3 targetable tags with weight > 0 (spelling, missing/extra words and "other" are tracked but not
  targeted). After the check, each focus tag is recorded as passed when the answer has no issue with that tag.
- Settings → Writing shows a folded **My mistakes** card: summary, tag chips (`Tense · 6 · ✓2`), the 8 newest
  mistakes and a **Clear history** button that needs two clicks.

## 2. Retype

- On by default. After a check with real (non-punctuation) mistakes, when the answer's words differ from the
  corrected sentence, the answer box is cleared and the learner types the corrected sentence once.
- Compared locally on words, ignoring case and punctuation. A mismatch names the first wrong word
  ("Word 2: expected "has", you typed "have"."). A match plays the corrected sentence; Enter then goes on.
- **Skip** ends the step. Enter in the box: check → submit retype → next sentence.

## 3. Listen

- The audio button on the corrected sentence already existed on both surfaces.
- New switch **Play the corrected sentence after checking** (off by default) plays it when no retype is needed.
- Uses the existing TTS voice, speed and audio cache.

## 4. Dictation

- Mode switch **Translate | Dictation** (Translate by default).
- No extra AI call: the English reference already generated with the Vietnamese sentence is read aloud as
  soon as the sentence is ready. **Listen** (Ctrl+Space) and **Slow** (80 % speed, Ctrl+Shift+Space).
- The Vietnamese meaning and the tip button are hidden until the answer is checked.
- Scored locally and instantly, PTE style: words are aligned by minimum edit distance; one point for each
  reference word typed correctly. Close misspellings are "spelling", other substitutions "wrong word",
  plus missing and extra words. The result is shown, retyped and recorded like a translation check.

## Settings

`WritingPracticeMode`, `WritingRetypeEnabled` (true), `WritingAutoPlayCorrected` (false),
`WritingFocusWeakPoints` (false). All Writing preferences are copied together by
`WritingTopicRules.CopyPreferences` (settings merge, Settings draft, floating window, app).

## Code map

| Area | Files |
| --- | --- |
| Core models and rules | `WritingHistory.cs`, `WritingHistoryRules.cs`, `WritingSkillTags.cs`, `WritingTextMatch.cs`, `WritingModels.cs` (issue `Tag`, prompt `FocusTags`) |
| Notebook storage | `Infrastructure/WritingHistoryBook.cs` (book, JSON and in-memory stores) |
| AI | `AiWritingCoach.cs` (focus rule in generate, `tag` in check), `IWritingCoach.cs` (`FocusTags`) |
| Flow | `WritingSession.cs` (mode, retype, history, focus tags, `EnterActionFor`) |
| UI | `WritingPage.xaml(.cs)`, `FloatingWritingPanel.xaml(.cs)`, `FloatingWindow.xaml.cs` (`WritingAudioRequest`), `SettingsWindow.xaml.cs`, `App.xaml.cs` |

## Tests

Core: `WritingTextMatchTests`, `WritingHistoryRulesTests`. Windows: `WritingPracticeFlowTests` (session,
notebook, JSON store, presentation), `AiWritingCoachFocusTests`, `WritingPracticeUiTests` (page and floating
panel on STA).
