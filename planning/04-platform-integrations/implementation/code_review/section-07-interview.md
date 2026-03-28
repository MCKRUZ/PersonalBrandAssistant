# Code Review Interview: Section 07 - Content Formatters

**Date:** 2026-03-15

## User Decisions

### HIGH-02: Surrogate pair truncation
**Decision:** Fix — Created `FormatterHelpers.SafeTruncate()` that checks for high surrogate at cutoff boundary and steps back one character. Applied to all four formatters.

## Auto-fixes Applied

### HIGH-01: Thread numbering overflow
Replaced fixed 6-char suffix reservation with dynamic `ComputeSuffixLength()` that calculates actual ` N/M` width based on part count digits. Two-pass approach: estimate part count, compute suffix, then build with correct reservation.

### HIGH-03: LinkedIn hashtag dedup bug
Fixed `TrimStart('#')` before checking `text.Contains($"#{tag}")` so tags with or without leading `#` are normalized before dedup check.

### MEDIUM: Mutable Dictionary in PlatformContent
Created `FormatterHelpers.EmptyMetadata` (shared `ReadOnlyDictionary`) and `FormatterHelpers.ToReadOnly()` helper. All formatters now return `ReadOnlyDictionary<string, string>` instead of mutable `Dictionary`.

## Items Let Go

- MEDIUM: Twitter character counting (Twitter's weighted algorithm is complex, acceptable for v1)
- MEDIUM: Sentence splitting on URLs/abbreviations (edge case, can iterate)
- MEDIUM: Carousel lower-bound check (0/negative caught by media_count > 0 check)
- MEDIUM: Hashtag truncation slicing (unlikely given typical lengths)
- MEDIUM: Unnecessary empty Dictionary allocation (now fixed via shared instance)
- LOW: ArgumentNullException guards (formatters are internal, called by pipeline)
- LOW: YouTube comma-separated tags (standard format, no escaping needed for v1)

## Final Test Count: 37 passing
