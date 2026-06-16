# Code Review Triage — section-03-idea-entity-changes

Run mode: auto-run. No items required user input (the one HIGH was a clear correctness fix).

## FIXED (HIGH — production-breaking)
- **PillarSubScores NOT NULL with no default.** Adding a NOT NULL jsonb column to the populated Ideas
  table (~3,800 rows on dev + Mac Mini) would fail ("column contains null values") — invisible to the
  empty-DB tests. Added `.HasDefaultValueSql("'[]'::jsonb")` to the property and regenerated the
  migration; PillarSubScores now scaffolds `nullable: false, defaultValueSql: "'[]'::jsonb"`, backfilling
  existing rows. (ScoreAttempts already had `defaultValue: 0`, so it was already safe.) Verified: build
  green, 356 tests pass, no model drift.

## REJECTED (false positive)
- **HIGH "Embedding Npgsql-only gating not shown / could be regressed."** The gate
  `if (Database.IsNpgsql()) PgVectorModelConfiguration.Apply(modelBuilder)` lives in
  ApplicationDbContext.OnModelCreating (committed in section-02, not in this diff). It is proven working:
  all 356 InMemory tests build the model and pass; a regressed gate would fail model validation on the
  Vector property (as it did transiently during development before the guard was added). No change.

## LET GO
- **Converter default JsonSerializerOptions.** The converter is the SOLE read/write path for the column
  (nothing hand-parses the raw jsonb), so write/read are self-consistent regardless of casing. Documented
  in the config comment. Matching EnableDynamicJson's internal options buys nothing here.
- **Comparer order-sensitivity / JSON-clone.** Sub-scores are written wholesale by the analyzer in a
  deterministic order and never reordered, so no spurious change-tracking. The flat shape (Guid/string/
  double/string) round-trips cleanly. Standard EF collection-comparer pattern.
- **ScoreAttempts ValueGeneratedOnAdd.** Correct and necessary (default 0 backfills the populated table);
  the sweep updates already-persisted entities (UPDATE, not INSERT), so no surprise.
- **Test location/namespace.** Placed in PBA.Application.Tests/FeedRanking matching the existing
  ListIdeasHandlerTests convention (Infrastructure.Tests has no InMemory ApplicationDbContext pattern).

## DEFERRED (accepted)
- vector(1536) round-trip + literal jsonb column type are Postgres-only → section-12 Testcontainers.
