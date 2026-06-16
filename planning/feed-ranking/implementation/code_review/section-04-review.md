# Code Review — section-04-embed-async

## Findings
- **HIGH — result mapping was positional-zip after OrderBy; non-contiguous/duplicate indices silently misalign.** FIXED (map by index + range/dup/missing validation; tests added).
- **HIGH — no retry on 429/5xx (whole 128-batch lost on a blip).** Per plan, retry is section-06's wrapper. FLAGGED for §06.
- **MEDIUM — first bad vector aborts batch, discards paid-for vectors.** Accepted per retry contract; FLAGGED for §06 (call in manageable groups).
- **MEDIUM — exact-zero vs near-zero magnitude guard.** LET GO (plan specified all-zero; real models don't emit near-zero).
- **MEDIUM — ~28 lines of duplicated transport logic.** FIXED (extracted PostJsonAsync, shared by chat + embeddings).
- **LOW — NaN guard unreachable via standard JSON.** Kept as defense-in-depth, not unit-tested (agreed).
- **LOW — callers must not rely on positional alignment (empties dropped).** FLAGGED for §06/§07.
