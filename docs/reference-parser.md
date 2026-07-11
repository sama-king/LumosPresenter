# Reference Parser Design

*Implemented in `LumosPresenter.Core/Parsing`. Pure logic, no I/O; validated by the xUnit
corpus in `tests/LumosPresenter.Tests/Parsing` (140+ cases).*

## Problem

Detect Bible references in live ASR transcripts of preaching. The hard parts:

- Speech engines **endpoint on pauses**, so one reference arrives split across several
  utterances with filler between: *"turn to Ephesians"* … *[pages flip]* … *"let's go to
  the fifth chapter"* … *"verse twelve"*.
- ASR mangles book names ("Filipians"), numbers arrive as words ("one hundred and
  nineteen"), and everyday words collide with book names ("the numbers don't add up").
- Output feeds a live display, so **false positives are worse than misses** — every
  heuristic result carries a confidence score for the confirm gate.

## Pipeline

```
utterance → Tokenizer → BookCatalog match / continuation logic → BibleReference(book,
chapter, verseStart?, verseEnd?, confidence)
```

### Tokenizer (`Tokenizer.cs`)

Normalizes spoken numbers into digit tokens: "twenty three" / "twenty-third" → 23,
"one hundred and nineteen" → 119, "one oh five" → 105, ordinals ("fifth" → 5), digit forms
("3:16", "16-18", "1st"). Deliberately does **not** merge adjacent simple numbers —
"three sixteen" stays `3, 16` (chapter then verse).

### Book matching (`BookCatalog.cs`)

- 66 canonical books with chapter counts (used for validation: "John 25" is rejected).
- Alias table: canonical names, abbreviations, curated ASR mis-transcriptions
  ("filipians", "galations", "revelations", "habakuk", …).
- Ordinal-prefixed books via a numbered-base table: "First / 1st / I / one Corinthians".
- Fuzzy fallback: bounded Levenshtein (≥5 chars, distance ≤1, ≤2 for ≥8 chars) with a
  confidence penalty. **Exact-only aliases** (currently `numbers`) are excluded from fuzzy
  matching so "number"/"numbered" can never resolve to the book Numbers.

### Grammar

Chapter and verse in either word order: "chapter five" *and* "the fifth chapter", "verse
twelve" *and* "twelfth verse", colon forms ("3:16"), ranges ("sixteen through eighteen",
"16-18", "13 to 18"), reversed psalms ("the twenty-third psalm"), single-chapter books
("Jude three" → Jude 1:3), and the Psalm 119 ambiguity ("Psalm one nineteen" → 119 at low
confidence; an explicit "verse" keyword forces 1:19).

## Cross-utterance state

Two layers, deliberately different lifetimes:

1. **Sticky context** (`_contextBook`, `_contextChapter`) — survives
   `UtteranceWindow` utterances (default **15**, configurable via
   `POST /api/parser/window/{n}` and `Parser:UtteranceWindow`). Bridges filler between
   book, chapter, and verse. Any detection refreshes the window; it decays to nothing
   after it lapses.
2. **Last-reference memory** (`_lastBook`, `_lastChapter`) — never decays (only
   `Reset()`). Long after the window has lapsed, an **explicit** verse/chapter cue
   ("let's continue to verse 13", "move to chapter six") resumes the last passage.
   A bare number cannot — too ambiguous to hijack a passage the speaker may have left.

## Cue libraries (`ReferenceCues.cs`)

- **Book cues** ("turn to", "the book of", "let's read from", …): when one immediately
  precedes a book match, confidence is boosted (×1.15, capped at 1.0) and shaky matches
  are rescued. An ambiguous everyday-word book ("numbers") with *neither* a cue *nor*
  following numbers is suppressed entirely.
- **Verse cues** ("start from", "beginning at", "pick up at", …): let a bare number attach
  as a verse against an established chapter even mid-utterance — *"we'll start from one"*
  after Romans 8 → Romans 8:1. Without a cue, bare numbers only attach at utterance start
  with a "clean tail" (`IsCleanTail`), which is what keeps *"four people came forward"*
  from becoming a verse.

Bare single prepositions ("in", "to") are excluded from both sets — too common to be
reliable signals.

## Confidence model (summary)

`confidence = bookFactor × structureFactor`, clamped to [0, 1].

| Signal | Factor |
|---|---|
| Exact alias / curated mis-transcription | 1.0 |
| Fuzzy distance 1 / 2 | 0.85 / 0.7 |
| Bare numbered base ("corinthians" without ordinal) | 0.7 |
| Book cue precedes book | ×1.15 (capped) |
| Explicit chapter+verse keywords or colon | 1.0 |
| Bare number pair ("john three sixteen") | 0.95 |
| Psalm 119-style ambiguity | 0.6 (0.75 with verse) |
| In-window continuation ("verse twelve") | 0.9 |
| Verse-cue continuation ("start from one") | 0.8 |
| Bare-number verse upgrade ("john three" \| "sixteen") | 0.75 |

The thresholds are tuned so anything inferred (rather than explicitly spoken) lands below
0.9 and will surface in confirm mode rather than auto-publish.

## Known trade-offs

- "John three" followed by a sentence *starting* with a clean number ("sixteen through
  eighteen…") upgrades to John 3:16-18 at 0.75 — genuinely ambiguous; the confirm gate is
  the backstop.
- A dangling "chapter" at the end of any utterance arms a chapter expectation against the
  current context book; rare false positives are accepted at 0.85.
- Chapter-only detections ("John 3") emit immediately and are *refined* by a later verse —
  the display pipeline should treat same-book-same-chapter refinements as upgrades, not
  duplicates.
