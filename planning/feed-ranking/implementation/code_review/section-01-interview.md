# Code Review Triage — section-01-foundation

Run mode: auto-run (user: "ask only on real decisions"). No items required user input.

## AUTO-FIX (applied)
- **NaN/Inf clamp in CosineSimilarity (MEDIUM).** Add `double.IsFinite(result) ? result : 0.0`
  as the final return so a corrupt (NaN/Inf) input vector can never yield a NaN downstream — fully
  honoring the R-H2 "never NaN" contract. Add a test feeding a NaN-containing vector.

## REJECTED (reviewer factually incorrect)
- **"`using Npgsql;` is redundant."** Verified empirically via a net10 reflection probe: the
  data-source `UseVector()` is `Npgsql.VectorExtensions.UseVector(INpgsqlTypeMapper)` in namespace
  `Npgsql`, and `NpgsqlDataSourceBuilder` implements `INpgsqlTypeMapper`. Without `using Npgsql;` the
  call does not compile (this was the actual build error we hit). `using Pgvector.EntityFrameworkCore;`
  resolves the separate EF-options `o.UseVector()`. Both usings are required. KEEP.

## LET GO (YAGNI / out of scope per plan)
- Null-arg `ArgumentNullException`: callers (EF / EmbedAsync) never pass null; not worth a guard.
- Options validation (thresholds in range, BatchSize>0): plan didn't require; operator-set values.

## PROCESS
- R-M4 gate result recorded in the section-01 commit message (OpenRouter /api/v1/embeddings,
  openai/text-embedding-3-small, HTTP 200, 1536-dim confirmed live 2026-06-16).
- Full DB migration apply against real Postgres is section-12's Testcontainers responsibility; the
  generated migration was verified by inspection (extension annotation only, correct Down()).
