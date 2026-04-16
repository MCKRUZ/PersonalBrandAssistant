# Section 04: Skill Registry — Staged Diff Summary

## New Files
- `src/.../Skills/SkillOptions.cs` — config class: SkillsPath, RequiredSkillIds (5 defaults)
- `src/.../Skills/SkillRegistry.cs` — singleton implementing ISkillRegistry
- `tests/.../Skills/SkillRegistryTests.cs` — 18 tests using real temp filesystem

## Modified Files
- `src/.../Skills/SkillCacheEntry.cs` — added RawContent + Sha256Hash fields (code review fix)

## SkillRegistry Design
- Discovers SKILL.md files via Directory.EnumerateFiles + depth filter (≤3 separators)
- Parses with SkillMetadataParser at startup; skips invalid files
- Validates required skill IDs: throws InvalidOperationException in Production, LogWarning in Development
- GetAllSkills: computed once in constructor, returned as cached IReadOnlyCollection
- LoadLevel2: ConcurrentDictionary<string, Lazy<string>> with ExecutionAndPublication — exactly-once factory
- Level2 body extracted from cached RawContent (no second file read)
- SHA-256 hash computed from raw content bytes during Discover (no second read)
- Startup logs: discovered count + IDs, SHA-256 per skill

## Test Coverage (18 tests)
- Discovery: 5 files found, depth-4 excluded, invalid file skipped, empty dir
- GetSkillById: exists, null, case-insensitive (WRITER/Writer/wRiTeR)
- Startup validation: all present OK, throws in prod, warns in dev
- GetAllSkills: count, returns SkillDefinition (Level1)
- LoadLevel2: reads body, cached after file deleted, 50-thread concurrent consistency, unknown ID throws
- Startup logging: SHA-256 no-error, count no-error

## Test Results
- 18/18 pass
