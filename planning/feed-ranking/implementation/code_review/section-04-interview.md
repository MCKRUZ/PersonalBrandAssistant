# Code Review Triage — section-04-embed-async

Run mode: auto-run. No user input needed (correctness + DRY fixes).

## FIXED
- **HIGH index mapping.** Results are now mapped to inputs by the response `index` (not positional zip
  after OrderBy). Out-of-range, duplicate, and missing indices throw. Added tests:
  EmbedAsync_NonContiguousIndices_Throws, EmbedAsync_DuplicateIndex_Throws.
- **MEDIUM transport duplication.** Extracted `PostJsonAsync(path, payload, ct)` (auth/referer headers,
  per-request timeout, error mapping); SendPromptAsync and EmbedBatchAsync both use it. Removed the
  redundant early ApiKey check in EmbedAsync (the helper owns it; all-empty input returns [] without a key).

## LET GO
- **Exact-zero vs near-zero magnitude.** Plan specified all-zero; a real embedding model never returns a
  near-zero garbage vector, and a magic norm threshold is over-engineering. Zero-vector + wrong-dimension
  guards cover the reachable R-H2 cases.
- **NaN guard untestable via JSON.** System.Text.Json rejects NaN on serialize/deserialize, so the guard
  is unreachable through the API boundary — kept as cheap defense-in-depth, documented, not asserted.

## FLAGGED for section-06 (and 07)
- Retry/backoff on 429/5xx is the embedding SERVICE's responsibility (section-06). Because EmbedAsync
  batches internally at BatchSize=128 and is all-or-nothing per call, section-06 should call EmbedAsync in
  manageable groups so one 429 doesn't waste a whole 128-batch of API spend; failed groups leave items
  Embedding==null for the next sweep.
- EmbedAsync drops empty inputs, so the result length can be < input length. Sections 06/07 must map
  returned vectors to ideas by filtering empties up front, never by positional index against the original list.
