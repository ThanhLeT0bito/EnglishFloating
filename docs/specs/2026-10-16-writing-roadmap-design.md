# Writing & grammar roadmap

Date: 2026-10-16 · Branch: `feature/writing-practice-v2`

## Goal

The Writing and Grammar pages had many exercises and lessons but no order. A beginner did not know what to learn
first, when a topic was "done", or what to do today. The Roadmap page gives one path from simple sentences to PTE
writing tasks, shows where the learner is, and runs a short guided session each day.

## Stages

`WritingRoadmap` (Core) holds six stages. Every built-in grammar topic belongs to exactly one stage, in learning
order. `Practices[0]` is the stage's main exercise.

| # | Stage | CEFR | Grammar | Exercises |
| --- | --- | --- | --- | --- |
| 1 | Simple sentences · Câu đơn chuẩn | A1–A2 | present simple, past simple, articles, countable/uncountable, prepositions, agreement | Translate Easy, Dictation Easy |
| 2 | Getting tenses right · Đúng thì | A2–B1 | present continuous, present perfect, past continuous, future, questions, pronouns, quantifiers | Translate Medium, Dictation Medium, EN → VI Easy |
| 3 | Linking ideas · Nối ý | B1 | linking words, relative clauses, V-ing / to V, comparisons, modals, word order | Combine Medium, Translate Medium, Combine Easy |
| 4 | Academic sentences · Câu phức học thuật | B1–B2 | passive, conditionals, past perfect, present perfect continuous, reported speech, participle clauses | Combine Hard, Translate Hard, Paraphrase Medium |
| 5 | Flexible expression · Diễn đạt linh hoạt | B2 | word form, adjective/adverb, collocations, dependent prepositions, phrasal verbs, inversion | Paraphrase Hard, Dictation Hard, Translate Hard |
| 6 | Paragraphs & PTE tasks | B2–C1 | — | coming soon (paragraphs, Summarize Written Text, Essay) |

## When a stage is done

`RoadmapRules.Progress` (Core), from the Writing notebook:

- **Lessons**: every topic's quick check passed (two thirds right or better).
- **Drills**: two thirds of the topics (rounded up) passed a 5-sentence drill (4/5 or better).
- **Sentences**: 15+ checked sentences in the stage — in one of its exercises, or a drill of one of its topics — and
  the last 15 average 75 or more.
- **No grammar that needs review** among its topics (3+ mistakes since the last drill).

Percent = the average of the four parts, each capped at 100%; 99% at most until the stage is complete.
The current stage is the one the learner chose ("Set as my stage"), otherwise the first stage that is not complete.

## Notebook log

`writing-history.json` gains:

- `checks`: every check's time, exercise, level, score and drill topic (last 1000).
- `quizzes`: every finished quick check — topic, correct, total (last 300).
- `roadmapStageKey`: the chosen stage, or empty to follow progress. Unknown or coming-soon stages are dropped.

Older files load as before; the roadmap starts from the checks made after this update, while lessons and drills
already saved still count.

## Today's session

`RoadmapRules.TodayPlan` builds up to four steps for the current stage:

1. **Review** (3 sentences): a topic that needs review (the stage's own first), otherwise the weak points when there
   are enough recent mistakes. Skipped when there is nothing to review.
2. **Learn**: the first topic whose quick check is not passed — the lesson, then its quick check.
3. **Drill** (5 sentences): the learned topic, otherwise the first topic not mastered.
4. **Apply** (8 sentences): one of the stage's exercises, rotating each day.

Review and drill use the stage's main exercise; dictation and EN → VI become Translate because those steps need
English answers checked by the AI. The session length is shown on the start button (about 1.5 min per sentence and
5 min per lesson).

The Settings window runs the session (`RoadmapSession`):

- A banner under the page title shows "Today · Step i of n · …", **Next step →** (highlighted once the step is
  done; a step that is not done can be skipped), **Roadmap** and **End**.
- Learn opens the lesson on the Grammar page; finishing its quick check marks the step done.
- Drill starts a grammar drill on the Writing page in the stage's exercise and level.
- Review and Apply start a guided run (`GuidedPractice`) on the Writing page with a fixed exercise, level and count.
  It reuses the drill banner ("Apply: Translate · Easy · sentence 3 of 8 · average 82").
- Work done ahead counts: a drill started from the lesson's Practice button completes the drill step.
- Finish or End shows a summary on the Roadmap page: steps done, sentences, average, new mistakes, stage percent and
  the streak (days in a row with at least one checked sentence).

## Roadmap page

- **Today card**: stage title, streak and sentences today, the plan, and *Start today's session* (or *Resume*).
- **Your path**: a vertical timeline of stage cards with state (done, current, upcoming, coming soon), CEFR, goal,
  a progress bar, exercise chips (start an 8-sentence run), grammar chips with a status dot (green mastered, blue
  started, amber needs review, gray not started; click opens the lesson) and, for the current stage, the checklist.
  Other stages have *Set as my stage*; a chosen stage has *Follow my progress*.

## Quick check

The lesson's quick check now ends with a score line. Passing (two thirds right) says the lesson counts on the
roadmap; otherwise it asks to re-read the core idea and offers *Try the quick check again*.

## Tests

- Core: `RoadmapTests` — topic coverage, progress parts, completion, needs review, percent, chosen stage, plans,
  streak, normalization.
- Windows: `RoadmapSessionTests` (session, guided runs, notebook log, texts) and `RoadmapUiTests` (guided run and
  roadmap drill on the Writing page, quick check on the Grammar page, the Roadmap page, a full session in the
  Settings window and ending early).

## Later

Placement test, stage 6 (paragraph translation, free paragraph, Summarize Written Text, Essay), a progress chart and
spaced repetition of mastered topics.
