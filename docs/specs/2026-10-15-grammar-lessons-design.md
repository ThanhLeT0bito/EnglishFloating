# Grammar lessons from your mistakes

Date: 2026-10-15 · Branch: `feature/writing-practice-v2`

## Goal

After some Writing practice the learner keeps repeating the same kind of mistake. The app now says which grammar
point it is (e.g. the present simple), offers a lesson that explains the **essence first** (what the form means and
why English uses it) and then the form, uses, mistakes and examples, and lets the learner practice that point in
real sentences.

## Grammar topics

- `GrammarTopics` (Core) holds 31 built-in topics in six groups: tenses (present simple … future), verbs (passive,
  modals, V-ing / to V, agreement, phrasal verbs), sentence structures (conditionals, relative clauses, reported
  speech, questions, comparisons, participle clauses, inversion), nouns and articles, prepositions and linking words,
  word form and choice. Each has an English and a Vietnamese name, a focus hint for the AI and search keywords.
- Search matches English and Vietnamese, with or without diacritics ("hien tai don" finds the present simple).
  Anything else becomes a custom topic (`custom:used to`) with its own lesson.

## Tagging mistakes

- The check prompt asks for `grammarTopic` per issue, chosen from the built-in keys (or empty). Unknown keys are
  dropped. A drill sentence also accepts its own custom key.
- `WritingMistake.TopicKey` is saved in `writing-history.json`; older mistakes have none.

## Progress

`GrammarProgressRules` (last 30 days):

| Status | When |
| --- | --- |
| Needs review | 3+ mistakes on the topic since the last drill (or ever, without a drill) |
| Improving | 1-2 mistakes since the last drill, or the last drill failed |
| Good | the last drill passed (≥ 4/5) and no mistake since |
| Not practiced | nothing yet |

Recommendations: needs review first, then improving, by mistakes since the last drill (top 5).

## Where it shows

- **Writing → Result**: when a mistake belongs to a topic that needs review, a button
  "You often get Present simple wrong (4 times). Learn it →" opens the lesson. Same in floating Writing (opens Settings).
- **Writing → My mistakes**: "Grammar to review" rows with status and a Learn button; the folded summary adds
  "Review: Present simple".
- **Settings → Grammar** (new page): search box, "Your weak grammar" with the newest own mistake as an example, and
  all topics by group (a colored dot shows the status).

## Lesson

Written by the AI in the explanation language (Writing setting) and saved in `grammar-lessons.json`
(newest 100), so it opens instantly next time; **Rewrite lesson** asks for a new version. Sections:

1. **The core idea / Bản chất** — 2-4 sentences on the meaning (time line for tenses), compared with Vietnamese.
2. Form — pattern rows with examples.
3. When to use it.
4. Signal words.
5. Don't confuse it with …
6. Common mistakes of Vietnamese learners.
7. **Your mistakes** — from the notebook, no AI.
8. Examples with Vietnamese and a play (TTS) button.
9. Quick check — 3 multiple-choice questions with explanations.
10. **Practice 5 sentences** — starts a drill on the Writing page; shows the last drill result.

Lessons use separate AI clients with longer limits (45 s per model, 90 s in total); Claude's `max_tokens` is now 4096.

## Drill

- 5 checked sentences generated with `FocusTopic` ("this exercise drills one grammar point …"). Prefetching is per topic.
- A sentence passes when it has no mistake tagged with the topic (custom topics: score ≥ 80). A re-check of the
  same sentence does not count again.
- Dictation and EN → VI drill in Translate. The banner shows progress, a Lesson button and End drill; at the end a
  summary and Practice again. The result is saved and changes the topic status. The next sentence is a normal one.

## Code map

| Area | Files |
| --- | --- |
| Core | `GrammarTopics.cs`, `GrammarProgress.cs`, `GrammarLesson.cs`, `WritingModels.cs` (`TopicKey`, `FocusTopicKey`), `WritingHistory.cs` (`Drills`, mistake `TopicKey`) |
| AI | `AiWritingCoach.cs` (grammarTopic, focus topic), `AiGrammarTutor.cs`, `GrammarLessonLibrary.cs` (stores + cache) |
| Flow | `WritingSession.cs` (focus topic), `GrammarDrill.cs`, `GrammarPresentation.cs`, `WritingHistoryBook.cs` |
| UI | `GrammarPage.xaml(.cs)`, `WritingPage` (drill banner, hint, review rows), `FloatingWritingPanel` (hint), `SettingsWindow` (nav + wiring), `FloatingWindow`/`App` (open lesson from floating) |

## Tests

Core `GrammarTests`; Windows `GrammarAiTests` (tagging, focus generation, tutor, library, store, drill, notebook),
`GrammarUiTests` (Grammar page, drill, hints, Settings navigation, floating hint).
