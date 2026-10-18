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

## 5. More exercise types

The mode switch now has five choices: **Translate | Dictation | Paraphrase | Combine | EN → VI**.

| Mode | The learner sees | The learner writes | Checked by |
| --- | --- | --- | --- |
| Translate | Vietnamese sentence | English translation | AI |
| Dictation | English audio (meaning hidden) | What they hear | Local word scoring |
| Paraphrase | English sentence | The same meaning in other words | AI (meaning 40, grammar/words 40, how much changed 20) |
| Combine | Two short English sentences + "Use: although" | One sentence with that connector | AI (connector/structure 40, meaning 40, grammar 20) |
| EN → VI | English sentence | Vietnamese translation | AI, meaning only; missing diacritics are not mistakes |

- Each mode has its own generate prompt and level rules (`ParaphraseLevelRules`, `CombineLevelRules`).
  Combine must make the learner **transform** the sentences, never just insert a word between them:
  Easy and/but with the repeated subject removed, because, so, when, before/after + -ing, who/which;
  Medium who/which/whose/where replacing a repeated noun, so/such … that, too … to / enough to, unless (from if-not),
  because of / due to + noun phrase, in order to, although at the start;
  Hard (2–3 sentences) despite + V-ing, having + past participle, participle clauses, reduced relative clauses,
  "which" for the whole clause, Not only did …, Not until … did, No sooner had … than, the more …, the more …, noun clauses.
  The connector is shown as a pattern ("Use: despite + V-ing"). Combine exercises have no word list, which would give the connector away.
- `WritingPrompt` gained `Mode`, `SourceEnglish` (the sentence to paraphrase, or the two sentences, one per
  line) and `Instruction` (the connector). Dictation keeps using translate prompts.
- Switching to a mode whose kind of sentence differs (e.g. Translate → Combine) requests a new sentence at once;
  until it arrives the old sentence keeps its own mode. Prefetching is per kind of sentence.
- A paraphrase identical to the original (ignoring case and punctuation) is stopped locally with
  "That is the original sentence. Rewrite it in your own words." and no AI call.
- The listen buttons appear in every mode except Translate (where the English is the answer). Only dictation
  plays automatically.
- In EN → VI the answer and its correction are Vietnamese, so audio, auto-play and "Save to study list" use the
  English sentence instead.
- After checking a paraphrase or combine exercise the Vietnamese meaning is shown.
- New skill tag **Sentence structure** (clauses, connectors, conditionals, participle clauses); it can be
  targeted by weak-point practice. Weak points are ignored for EN → VI sentences.

## Settings

`WritingPracticeMode`, `WritingRetypeEnabled` (true), `WritingAutoPlayCorrected` (false),
`WritingFocusWeakPoints` (false). All Writing preferences are copied together by
`WritingTopicRules.CopyPreferences` (settings merge, Settings draft, floating window, app).

## Code map

| Area | Files |
| --- | --- |
| Core models and rules | `WritingHistory.cs`, `WritingHistoryRules.cs`, `WritingSkillTags.cs`, `WritingTextMatch.cs`, `WritingModes.cs`, `WritingModels.cs` (issue `Tag`, prompt `FocusTags`, `Mode`, `SourceEnglish`, `Instruction`) |
| Notebook storage | `Infrastructure/WritingHistoryBook.cs` (book, JSON and in-memory stores) |
| AI | `AiWritingCoach.cs` (focus rule in generate, `tag` in check), `IWritingCoach.cs` (`FocusTags`) |
| Flow | `WritingSession.cs` (mode, retype, history, focus tags, `EnterActionFor`) |
| UI | `WritingPage.xaml(.cs)`, `FloatingWritingPanel.xaml(.cs)`, `FloatingWindow.xaml.cs` (`WritingAudioRequest`), `SettingsWindow.xaml.cs`, `App.xaml.cs` |

## Tests

Core: `WritingTextMatchTests`, `WritingHistoryRulesTests`, `WritingModesTests`. Windows: `WritingPracticeFlowTests`
(session, notebook, JSON store, presentation), `AiWritingCoachFocusTests`, `WritingModesFlowTests` (per-mode prompts,
parsing, session), `WritingPracticeUiTests` and `WritingModesUiTests` (page and floating panel on STA).
