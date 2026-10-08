# Writing Skill (Vietnamese → English Translation) Design

## Goal

Build writing skill through translation practice. The learner picks one or more topics and a level, Gemini generates a natural Vietnamese sentence, the learner writes the English translation, and Gemini checks it: which words are wrong, why (grammar, vocabulary, spelling, word order, missing meaning), and a corrected sentence. Phase 1 is one sentence at a time. Phase 2 (later) adds rounds of many sentences, history and review of mistakes.

## User flow (phase 1)

1. Settings → new **Writing** page.
2. Topics: a wrap panel of toggle chips. Built-in topics plus the learner's own topics. Several topics can be selected at once. **+ Add topic** adds a custom topic; custom chips can be removed. With no topic selected the generator mixes all built-in topics.
3. Level: Easy / Medium / Hard (segmented buttons). **Explain in:** English (default) / Tiếng Việt chooses the language of feedback explanations.
4. **Generate sentence** shows a Vietnamese sentence with its topic and level. The English reference stays hidden.
5. The learner types a translation. **Check** (or Ctrl+Enter) sends it for feedback. **Skip** generates a new sentence without checking.
   - **💡 Tip** (Ctrl+H) on the prompt card reveals help one tier per click, from mild to strong: (1) key words, which are up to 5 Vietnamese words or phrases from the sentence with their English equivalent and part of speech; (2) a grammar hint about the tense or structure to use, in the chosen explanation language; (3) a sentence starter of at most 3 English words. The button shows progress such as `Tip 1/3`. A muted line under the answer box reads "Stuck? Press 💡 Tip". Tips are generated together with the sentence (no extra AI call) and are never revealed automatically. Using tips does not change the score; the result shows "Used N tips".
6. Result panel:
   - Score 0–100 and a verdict (Correct / Almost / Needs work).
   - The learner's answer with each wrong span highlighted and color-coded by issue type; hovering a span shows the fix.
   - Issue list: type, original → suggestion, short explanation.
   - **Corrected sentence** (minimal edits to the learner's answer) with an audio button (existing TTS).
   - **Natural alternative** (how a native speaker would say it) when it differs.
   - **Save to study list** adds the corrected sentence to a chosen study list so it can be practiced in the floating widget. **Next sentence** (Enter after a check) generates the next one with the same topics and level.
7. Errors (missing Gemini key, network, timeout, invalid AI response) show a concise message and a Retry button; the typed translation is never lost.

Mockup:

~~~text
Writing practice
Topics  [Working in IT✓] [Traffic✓] [Daily life] [Travel] [Health] ... [+ Add topic]
Level   ( Easy ) [ Medium ] ( Hard )                      [ Generate sentence ]

┌ Translate into English ─────────────────────────── Traffic · Medium ┐
│ Sáng nay đường bị kẹt xe nên tôi đến công ty muộn gần nửa tiếng.     │
│                                                       [ 💡 Tip 1/3 ] │
│ Key words  kẹt xe → traffic jam (n.) · đến công ty → get to the office │
└─────────────────────────────────────────────────────────────────────┘
[ This morning the road was traffic jam so I came to company late...  ]
[ Check (Ctrl+Enter) ]  [ Skip ]

Score 68 · Almost
This morning the road [was traffic jam] so I came to [company] late almost half an hour.
• Grammar      "was traffic jam" → "was jammed" / "there was a traffic jam"
• Vocabulary   "company" → "the office": ...
• Word order   "late almost half an hour" → "almost half an hour late"
Corrected:  There was a traffic jam this morning, so I got to the office almost half an hour late. 🔊
Natural:    I was nearly half an hour late for work because of the traffic this morning.
[ Save to study list ▾ ]  [ Next sentence ]
~~~

## Levels

| Level  | Vietnamese source | Expected English |
|--------|-------------------|------------------|
| Easy   | 5–10 words, one clause, everyday words | A2: simple present/past/future, common vocabulary |
| Medium | 10–16 words, two clauses or a connector | B1–B2: perfect tenses, comparatives, because/although/when, common collocations |
| Hard   | 14–24 words, complex ideas | C1, PTE-like: passive, relative/conditional clauses, formal or academic vocabulary |

The English reference translation must be at most 20 words at every level, so any corrected sentence can be saved to a study list (existing 20-word limit).

## Built-in topics

Working in IT, Traffic & transport, Daily life, Work & meetings, Travel, Health, Education, Shopping, Food & cooking, Family & friends, Environment, Technology, Weather, Housing.

Custom topics: name 1–40 characters, case-insensitive unique across built-in and custom, at most 30 custom topics.

## AI contract

Reuse the protected Gemini key, the candidate model list and fallback on 404/429/500/503, a 30-second timeout and a 64 KB response cap. Both prompts treat learner text and topic names as data, never as instructions. Both use `responseMimeType=application/json` with a response schema.

Generate request: topic names, level rules, and the last 20 Vietnamese sentences of this session (to avoid repeats). Response:

~~~json
{
  "vietnamese": "…",
  "english": "…",
  "topic": "Traffic & transport",
  "tips": {
    "vocabulary": [ { "vietnamese": "kẹt xe", "english": "traffic jam", "partOfSpeech": "noun" } ],
    "grammar": "Use the past simple…",
    "starter": "There was"
  }
}
~~~

Reject an empty sentence or reference, a Vietnamese sentence over 40 words, an English reference over 20 words, or a topic that cannot be matched to the requested ones (a case-insensitive or contained match is accepted; with one requested topic it is used as the label). Tips are sanitized rather than rejected: at most 5 vocabulary items without empty entries, a vocabulary item that equals the whole reference is dropped, the starter is cut to its first 3 words and dropped when it would reveal the whole reference. Empty tiers are skipped, so a prompt may offer fewer than 3 tips.

Check request: Vietnamese source, hidden reference, level and the learner's translation. The reference is a hint; any translation with the same meaning that is grammatical and natural is correct. Response:

~~~json
{
  "score": 0,
  "correctedSentence": "minimal edit of the learner's answer",
  "naturalAlternative": "optional",
  "summary": "one short sentence",
  "issues": [
    { "type": "grammar | vocabulary | spelling | word_order | missing_meaning | extra_meaning | punctuation",
      "original": "exact text copied from the learner's answer, empty for missing meaning",
      "suggestion": "replacement",
      "explanation": "short explanation" }
  ]
}
~~~

Score is clamped to 0–100 and the verdict is computed by the client (>= 90 Correct, >= 60 Almost, otherwise Needs work). Punctuation and capitalization issues are listed (type `punctuation`) but never lower the score: the prompt says so, and when every issue is punctuation the client sets the score to 100. Unknown issue types become Grammar. An issue whose `original` cannot be found in the answer is still listed, just not highlighted. `explanation` and `summary` use the chosen explanation language (English by default, Vietnamese when selected); `correctedSentence`, `naturalAlternative` and `suggestion` are always English.

## Data

Core additions (saved in settings JSON, normalized on load and merge):

- `WritingLevel` enum: Easy = 0, Medium = 1, Hard = 2 (stable numeric values). Default Medium.
- `WritingTopic(string Key, string Name)`: built-in keys are stable slugs such as `it-work`, and custom keys are `custom:{guid}`.
- `WritingExplanationLanguage` enum: English = 0, Vietnamese = 1. Default English.
- `AppSettings.CustomWritingTopics`, `AppSettings.SelectedWritingTopicKeys` (unknown keys dropped), `AppSettings.WritingLevel`, `AppSettings.WritingExplanationLanguage`.

Phase 1 keeps no history on disk. Recent sentences for de-duplication live in memory for the session.

## Components

- Core `WritingTopics` (built-in catalog) and `WritingTopicRules` (add/remove/normalize custom topics and selection).
- Core `WritingFeedbackRules`: map issues to non-overlapping highlight spans in the learner's answer (case-insensitive, whole-word when the text starts or ends with a letter or digit, first unused occurrence), clamp score, punctuation-only rule, verdict.
- Core `WritingTipRules`: sanitize tips and list the available tip tiers in reveal order.
- Windows `GeminiJsonClient`: shared bounded Gemini request with model fallback, extracted so the new service does not copy the HTTP code a third time. The existing phraser and explainer keep their own code in this feature and migrate later.
- Windows `IWritingCoach` / `GeminiWritingCoach`: GenerateAsync and CheckAsync with the prompts and parsing above.
- Windows `WritingSession`: UI-independent state machine (Idle → Generating → Answering → Checking → Reviewed, with an error message and Retry). A newer request cancels the older one, a request counter rejects stale results, recent sentences are kept, and it tracks how many tip tiers are revealed.
- Windows `WritingPage` (UserControl): new `SettingsPageId.Writing` nav item. Topic chips, level, prompt card, answer box, result panel. Audio reuses `SentenceAudioPlayback`, and saving reuses `StudyListDraft.AddSentence`.

## Verification

- Core tests: topic add/remove/duplicate/limits, selection normalization, JSON compatibility for old settings, span mapping (repeated words, overlap, not-found, case), score clamp.
- Service tests with a fake HttpMessageHandler: request contains topics, level rules and recent sentences; learner text sent as data; malformed or oversized responses; missing key; model fallback.
- Session tests: stale result ignored after Skip; check disabled for empty answer; errors keep the answer; recent-sentence cap.
- UI tests: chips toggle and persist through the draft, Ctrl+Enter checks, highlights rendered, Save to study list adds the corrected sentence.
- Full test suite and Release build pass. Manual QA uses a real Gemini key at all three levels.

## Phase 2 (not in this feature)

- Rounds: choose 5 or 10 sentences, generate them in one call, translate all in a list, check all in one call, then a summary with a score per sentence.
- History saved per attempt (bounded), a "My mistakes" review list built from issues, and stats per topic, level and issue type.
- Spaced review that regenerates sentences targeting the learner's frequent mistakes.
- Topic word bank: 15–20 common words per topic, with "Add to flashcards" into the existing decks.
- Reverse direction (English → Vietnamese), a writing prompt in the floating widget, and PTE tasks (Summarize Written Text, essay) later.

## Out of scope

Speech input, offline generation without Gemini, sharing or exporting history, and changes to the existing phrasing or vocabulary services beyond extracting the shared client.

## Decisions

1. Explanations are English by default; a setting on the Writing page switches them to Vietnamese.
2. Punctuation and capitalization mistakes are only listed; they do not lower the score.
3. Phase 1 lives on a Settings page only. A floating-widget writing card is a later phase.
