# Review triage & decisions — section-08

Triaged autonomously (per the user's "don't prompt for routine workflow decisions" preference). The one
Critical was investigated against authoritative docs before being dismissed.

## C1 (Critical) — investigated, NOT a bug
The reviewer claimed projecting the value-converted `PillarSubScores` collection into the `RankRow`
projection may fail to translate on Npgsql. **Verified against the EF Core "Value Conversions → Limitations"
docs (Microsoft Learn):** the documented limitation is #2 — *"It isn't possible to query INTO value-converted
properties, e.g. reference members on the value-converted .NET type in your LINQ queries"* (GitHub #10434).

Our projection assigns the **whole** property (`PillarSubScores = i.PillarSubScores`) and never references a
member of `PillarSubScore` inside the LINQ/SQL query — every rank computation runs in memory *after*
materialization. Projecting a converted property out selects the underlying jsonb column and applies the
converter on materialization, which is fully supported (the original handler already projects the jsonb
`Tags` collection the same way and runs in production on Npgsql). So C1 does not apply. R-C1a holds by
construction (EF emits only the columns named in the projection; `Embedding` is not among them).
Section-12 Testcontainers will give the final real-Postgres confirmation (C1a's legitimate verification gap).

## Applied fixes
- **H1** — sharpened the materialization comment to name the real cost driver: the default *unfiltered* Idea
  Bank view materializes the whole table (O(table)) to return one page, not just the "25k rows" case.
- **M1/M2** — the in-memory string sorts (title/sourcename/category) now use a fixed
  `StringComparer.OrdinalIgnoreCase` so ordering is deterministic regardless of the server's current culture
  (was: default current-culture comparison, a silent behavior change from the old SQL-collation sort).
  `category` uses `?? ""` so nulls order deterministically.

## Decisions to NOT change
- **H2 (default sort → rank)** — intentional per R-M2 / section-08; the full suite passed unchanged, so no
  in-process caller or test broke. The API-contract change IS the feature.
- **M3 (Score clamp)** — section-07 owns `Score` (`round(Clamp(brandFit,0,1)*10)`); section-08 must not
  re-derive it. The spec's test checklist is internally contradictory; no code change.
- **L1 / L4** — pillar-vector load is negligible (5 pillars); the pure function takes `now` as a param
  (unit-testable), consistent with the rest of the codebase.

## Verification after fixes
`dotnet build` clean (0 warnings). PBA.Application.Tests: 374/374 green (incl. 11 ComputeRank + 6 new
ListIdeas rank tests). Full solution re-run before commit.
