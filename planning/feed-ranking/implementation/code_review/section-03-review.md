# Code Review — section-03-idea-entity-changes

## Findings
- **HIGH — PillarSubScores jsonb NOT NULL with no default → migration fails on populated Ideas table.** FIXED.
- **HIGH — Embedding Npgsql-only gating not visible in diff.** REJECTED (gate exists in ApplicationDbContext, proven by InMemory tests passing).
- **MEDIUM — converter uses default JsonSerializerOptions (diverges from EnableDynamicJson).** LET GO (sole read/write path, self-consistent).
- **MEDIUM — comparer order-sensitive / JSON-clone fragile.** LET GO (sub-scores written wholesale, deterministic order, flat shape).
- **LOW — ScoreAttempts ValueGeneratedOnAdd interaction.** Non-issue (correct; default 0 backfills existing rows; sweep updates existing entities).
- **LOW — test path/namespace differs from plan.** LET GO (matches existing ListIdeasHandlerTests convention).
- **LOW — Idea mapping split across two config files.** Already documented with cross-reference comments.
