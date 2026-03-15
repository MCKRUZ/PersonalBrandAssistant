# Section 07 - Content Formatters: Code Review

**Reviewer:** code-reviewer agent
**Date:** 2026-03-15
**Scope:** TwitterContentFormatter, LinkedInContentFormatter, InstagramContentFormatter, YouTubeContentFormatter + tests
**Verdict:** WARNING -- no critical issues, several high items to address before merge

---

## Findings

### HIGH-01: Thread numbering reservation of 6 chars breaks at 10+ parts

**File:** TwitterContentFormatter.cs (BuildThread method)
**Issue:** The thread builder reserves a fixed 6 characters for the numbering suffix (e.g. " 1/9"). Once the thread reaches 10+ parts, numbering like " 10/12" requires 7 characters, and at 100+ parts it needs 8. Tweets in longer threads can silently exceed 280 characters.
**Suggestion:** Calculate the numbering overhead dynamically based on estimated part count, or use a two-pass approach: split first with a conservative estimate, then re-split if the actual count changes the digit width.

### HIGH-02: Truncation can split mid-surrogate pair or mid-grapheme cluster

**File:** TwitterContentFormatter.cs (Truncate), InstagramContentFormatter.cs (line 58), YouTubeContentFormatter.cs (title and description truncation)
**Issue:** All truncation uses text[..(maxLength - 3)] which operates on UTF-16 code units, not characters or grapheme clusters. Content with emoji (very common on social media) could be sliced mid-surrogate pair, producing invalid Unicode.
**Suggestion:** Use System.Globalization.StringInfo to find a safe truncation boundary. At minimum, use char.IsHighSurrogate to avoid splitting within a surrogate pair.

### HIGH-03: LinkedIn duplicate-hashtag detection has a matching gap

**File:** LinkedInContentFormatter.cs (lines 99-101)
**Issue:** The inline detection checks text.Contains with the pattern "#{tag}", but the tag value may already start with "#". If tag = "#leadership", the check searches for "##leadership" in the text, which will never match, so the tag gets appended as a duplicate.
**Suggestion:** Normalize the tag by stripping the leading "#" before constructing the search pattern. Apply the normalization before the Where clause, then re-add the "#" prefix in the Select.

### MEDIUM-01: Mutable Dictionary passed to immutable record

**File:** All four formatters
**Issue:** PlatformContent.Metadata is typed as IReadOnlyDictionary, but a plain Dictionary is passed in. The caller retains the ability to cast back to Dictionary and mutate it, violating the immutability contract.
**Suggestion:** Use ImmutableDictionary via .ToImmutableDictionary() and ImmutableArray for the media parameter instead of Array.Empty.

### MEDIUM-02: Instagram validates carousel_count but ignores lower bound

**File:** InstagramContentFormatter.cs (lines 35-41)
**Issue:** Carousel validation checks carouselCount > MaxCarouselItems but does not check for zero or negative values. A carousel_count of 0 or -1 silently passes.
**Suggestion:** Add a lower-bound check: carouselCount < 1 || carouselCount > MaxCarouselItems.

### MEDIUM-03: Sentence splitting is naive for URLs, abbreviations, and decimals

**File:** TwitterContentFormatter.cs (SplitIntoSentences)
**Issue:** The sentence splitter treats any period, exclamation, or question mark followed by a space (or end-of-string) as a sentence boundary. This incorrectly splits on URLs, abbreviations (Dr. Smith), decimal numbers (version 3.0), and ellipses.
**Suggestion:** At minimum, do not split on "." when the preceding characters look like a URL. Document known limitations if a perfect solution is out of scope.

### MEDIUM-04: Twitter FitsInSingleTweet counts code units, not Twitter characters

**File:** TwitterContentFormatter.cs (FitsInSingleTweet)
**Issue:** Twitter uses weighted character counting (URLs=23, CJK=2). The current implementation uses string.Length (UTF-16 code units). A tweet with a URL could be rejected as too long when Twitter would accept it.
**Suggestion:** For Phase 1, document this as a known limitation with a comment referencing the Twitter developer docs.

### MEDIUM-05: Unnecessary empty Dictionary allocation on every call

**File:** LinkedInContentFormatter.cs, InstagramContentFormatter.cs, TwitterContentFormatter.cs (single-tweet path)
**Issue:** When there is no metadata, a new empty Dictionary is allocated on every call.
**Suggestion:** Use a shared empty instance via ImmutableDictionary.Empty.

### MEDIUM-06: Instagram caption truncation can cut through hashtags

**File:** InstagramContentFormatter.cs (lines 56-59)
**Issue:** Hashtags are appended to the caption, then the combined string is truncated at 2200. This can result in a half-cut hashtag like "#progra..." which looks broken and is non-functional.
**Suggestion:** Truncate the caption body first to leave room for hashtags, or remove trailing hashtags that would be cut.

### LOW-01: No null guard on content parameter

**File:** All four formatters
**Issue:** None of the FormatAndValidate methods check for a null content argument. A null input produces a NullReferenceException with an unhelpful stack trace.
**Suggestion:** Add ArgumentNullException.ThrowIfNull(content) as the first line of each method.

### LOW-02: Test coverage gaps for identified edge cases

**File:** All test files
**Issue:** Several edge cases lack test coverage:
- Twitter: Single sentence longer than 280 chars (forces truncation within thread building)
- Twitter: Content with URLs containing periods (sentence splitter edge case)
- Twitter: Thread with 10+ parts (numbering width overflow from HIGH-01)
- Instagram: Tags that already start with "#" (the StartsWith branch)
- LinkedIn: Tag that already starts with "#" (duplicate detection bug from HIGH-03)
- YouTube: Tags containing commas (breaks the comma-joined metadata format)
- All formatters: Unicode content with emoji and surrogate pairs

**Suggestion:** Add targeted tests for these edge cases, especially those corresponding to HIGH-01 through HIGH-03.

### LOW-03: YouTube tags are comma-separated without escaping

**File:** YouTubeContentFormatter.cs (tag metadata serialization)
**Issue:** Tags are joined with a comma separator. If a tag itself contains a comma, the downstream consumer cannot reliably split them back apart.
**Suggestion:** Either validate that tags do not contain commas, use a different separator, or serialize as a JSON array.

---

## Summary

| Severity | Count | Items |
|----------|-------|-------|
| CRITICAL | 0 | -- |
| HIGH     | 3 | Thread numbering overflow, surrogate pair truncation, LinkedIn hashtag dedup bug |
| MEDIUM   | 6 | Mutable dictionary, carousel validation, sentence splitting, Twitter char counting, empty dict alloc, Instagram hashtag truncation |
| LOW      | 3 | Null guards, test gaps, YouTube tag escaping |

## Verdict: WARNING -- Recommend fixing HIGH items before merge

The three HIGH items are logic bugs that will produce incorrect output in real-world usage:
- **HIGH-01** will cause tweets over 280 chars for threads with 10+ parts (common for blog-to-thread conversion)
- **HIGH-02** will produce invalid Unicode when truncating emoji-heavy social media content
- **HIGH-03** will duplicate hashtags on LinkedIn when tags arrive with the "#" prefix

The formatters are otherwise well-structured: clean separation of concerns, good use of the Result pattern, appropriate use of sealed, and solid test coverage for the happy paths. The code is readable and each file is well within size limits.
