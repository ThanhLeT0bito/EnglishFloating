# AI Sentence Phrasing Design

## Goal

Help the learner read a sentence in natural groups. Keep ordinary Add sentence unchanged. Add with AI checks spelling and proposes reading groups for explicit review before a sentence is saved.

## User flow

1. The user enters an English sentence of at most 20 words in Setup.
2. Add sentence saves it without AI or phrase breaks.
3. Add with AI sends the input to the configured Gemini API and opens a review dialog. The original remains visible. The proposal is editable as one group per line. A live preview shows the groups on one line with visibly wider spaces between them and no slash characters.
4. Confirm & add validates the edited groups, joins them with single spaces into canonical sentence text, stores break locations, and adds one sentence to the selected list. Cancel does not add a sentence or change the input.
5. API errors, missing key, malformed response, or invalid proposal leave the original editor text untouched and show a concise error. The ordinary Add sentence action remains available.

## AI contract and safety

- Request only spelling and punctuation corrections plus natural speech grouping. Keep the original word order and wording apart from corrections; no paraphrasing or translation.
- Gemini returns a JSON array of ordered nonempty groups. Reject an empty array, empty groups, more than 20 total words, or a response with no usable sentence.
- A correction is never persisted without user confirmation. The review dialog shows original and proposed text so changes can be inspected.
- The user may edit the groups before confirming. A newline separates groups in the review editor; spaces within a group remain ordinary word spaces.
- The existing protected Gemini key is reused. No new credential is stored.

## Data and rendering

- `StudySentence.Text` remains canonical text with one space between words. Add optional `PhraseBreakAfterWordIndices`, a sorted, unique list of one-based word positions where a wide visual gap follows. For example, `I usually go to the gym after work with my friends.` has breaks after words 6 and 8.
- Legacy sentences without the property have no breaks. Normalize invalid break positions away when loading settings. Updating a sentence manually clears its old breaks, avoiding stale boundaries.
- The floating sentence renderer inserts visual gap text at the stored boundaries without changing `StudySentence.Text`. Vocabulary selection text is whitespace-normalized before lookup and add, preserving multiword vocabulary across a gap. Existing vocabulary highlights remain matched against canonical text. Practice and flashcards continue using canonical text.
- The render signature includes break metadata so moving between two sentences with identical text but different phrasing redraws the overlay.

## Components

- Core: phrase-group parser and boundary validator plus optional sentence metadata.
- Gemini sentence phrasing service: accepts original sentence and returns proposed groups; uses the same key provider pattern as vocabulary explanations and bounded request/response handling.
- Setup: Add with AI action and a review dialog; only confirmation calls the draft's add operation.
- Floating overlay: renders wide gaps while preserving vocabulary interactions.

## Verification

- Core tests cover group conversion, 20-word validation, boundary normalization, JSON compatibility, and manual-update behavior.
- Service tests cover valid and malformed Gemini responses, missing key, and request wording.
- UI tests cover Add with AI review/cancel/confirm behavior and vocabulary selection across a visual gap where practical.
- Release build and full test suite must pass. Manual QA checks the wide gap in the floating overlay and the review dialog.

## Out of scope

Audio pronunciation, intonation arrows, automatic correction without review, editing phrase breaks for an existing sentence, and a standalone grammar checker for ordinary Add sentence.
