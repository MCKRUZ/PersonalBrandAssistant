# Section 04 — Skill Registry

## Overview

This section implements `SkillRegistry`, the singleton that discovers all `SKILL.md` files at startup, caches Level 1 metadata, and provides lazy Level 2 body loading. Also defines `SkillOptions`.

**Dependencies:**
- section-01-project-setup — NuGet packages must be present
- section-02-domain-interfaces — `ISkillRegistry`, `SkillDefinition` must exist
- section-03-skill-parser — `SkillMetadataParser` and `SkillCacheEntry` must be implemented

**Blocks:** section-07-skill-files, section-08-capability-base, section-10-di-wiring

---

## Files to Create

| File | Action |
|------|--------|
| `src/PersonalBrandAssistant.Infrastructure/Skills/SkillOptions.cs` | Create |
| `src/PersonalBrandAssistant.Infrastructure/Skills/SkillRegistry.cs` | Create |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Skills/SkillRegistryTests.cs` | Create |

Note: `SkillCacheEntry.cs` is created in section-03.

---

## Tests First

**Test file:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Skills/SkillRegistryTests.cs`

Use a **real temp filesystem directory** — no mocking for filesystem operations. Create minimal valid `SKILL.md` files, construct `SkillRegistry`, and assert behaviour. Inject mock `ILogger<SkillRegistry>`.

Minimal valid SKILL.md content for test fixtures:

```
---
schema_version: 1
name: Writer
id: writer
description: Test skill
category: content
tags: [writing]
skill_type: creative
allowed_tools: []
---

You are a writer. {{ brand_voice_block }}
```

Test list:

```
# Discovery
Discover_FiveValidSkillFiles_FindsAll
Discover_NestedBeyondMaxDepth_SkipsDeep          // SKILL.md at depth 4 is excluded
Discover_InvalidSkillFile_SkipsAndLogs
Discover_EmptyDirectory_DiscoversZero

# GetSkillById
GetSkillById_ExistingId_ReturnsDefinition
GetSkillById_NonExistentId_ReturnsNull
GetSkillById_CaseInsensitiveLookup_Succeeds       // "Writer" finds "writer"

# Startup validation
Startup_AllRequiredSkillsPresent_NoException
Startup_MissingRequiredSkill_ThrowsInProduction
Startup_MissingRequiredSkill_LogsWarningInDevelopment

# GetAllSkills
GetAllSkills_FiveFilesDiscovered_ReturnsFive
GetAllSkills_ReturnsLevel1Only                   // Instructions is null

# LoadLevel2
LoadLevel2_FirstCall_ReadsBodyFromFile
LoadLevel2_SecondCall_ReturnsCachedValue          // file read count stays at 1
LoadLevel2_ConcurrentFirstAccess_FileReadOnce     // use Parallel.For with 50 threads
LoadLevel2_UnknownSkillId_ThrowsKeyNotFoundException

# Startup logging
Startup_LogsSHA256HashOfEachFile
Startup_LogsDiscoveredSkillCount
```

**Depth test setup:** Create SKILL.md at `skills/a/b/c/d/SKILL.md` (4 levels) — registry must not discover it.

**Concurrency test:** Use `Parallel.For` with 50 threads all calling `LoadLevel2("writer")` simultaneously. Verify the file body is materialized exactly once using an `Interlocked.Increment` counter in a test-only subclass.

---

## Implementation

### `SkillOptions.cs`

```csharp
public class SkillOptions
{
    public const string SectionName = "Skills";

    /// <summary>
    /// Root path for SKILL.md discovery.
    /// Defaults to AppContext.BaseDirectory/skills at runtime.
    /// Single-file publish is NOT supported in Phase 1.
    /// </summary>
    public string SkillsPath { get; init; } =
        Path.Combine(AppContext.BaseDirectory, "skills");

    /// <summary>
    /// Skill IDs that must be present at startup.
    /// Production: throws if any are missing.
    /// Development: logs a warning and continues.
    /// </summary>
    public IReadOnlyList<string> RequiredSkillIds { get; init; } =
        ["writer", "social", "repurpose", "engagement", "analytics"];
}
```

### `SkillRegistry.cs`

Constructor signature:

```csharp
public sealed class SkillRegistry : ISkillRegistry
{
    public SkillRegistry(
        IOptions<SkillOptions> options,
        IHostEnvironment environment,
        ILogger<SkillRegistry> logger)
    { /* discover, validate, log */ }

    public SkillDefinition? GetSkillById(string id) { ... }
    public IReadOnlyCollection<SkillDefinition> GetAllSkills() { ... }

    /// <summary>
    /// Returns the raw (unrendered) Level 2 Liquid template body.
    /// Loads from disk on first call; subsequent calls return cached value.
    /// Throws KeyNotFoundException if skillId is not in the registry.
    /// </summary>
    public string LoadLevel2(string skillId) { ... }

    private static IReadOnlyDictionary<string, SkillCacheEntry> Discover(
        string skillsPath, int maxDepth, ILogger logger) { ... }

    private static int ComputeRelativeDepth(string basePath, string filePath) { ... }

    private static string ComputeSha256(string filePath) { ... }
}
```

**Key design decisions:**

- `Microsoft.Extensions.FileSystemGlobbing.Matcher` with pattern `**/SKILL.md`, depth-limited to 3. Matcher doesn't natively limit depth — filter matched paths: reject any where directory separators between base path and matched file exceed 3.
- `IReadOnlyDictionary<string, SkillCacheEntry>` with `StringComparer.OrdinalIgnoreCase`, keyed by lowercased `Id`.
- `ConcurrentDictionary<string, Lazy<string>>` for Level 2 cache with `LazyThreadSafetyMode.ExecutionAndPublication` — factory runs exactly once.

**Lazy Level 2 loading:**

```csharp
public string LoadLevel2(string skillId)
{
    if (!_skills.TryGetValue(skillId, out var entry))
        throw new KeyNotFoundException($"Skill '{skillId}' not found in registry.");

    return _level2Cache.GetOrAdd(
        skillId,
        _ => new Lazy<string>(
            () => ReadLevel2Body(entry.FilePath),
            LazyThreadSafetyMode.ExecutionAndPublication)
    ).Value;
}
```

**skill.load Activity span:** Create inside the `Lazy<string>` factory only (cold path). The `AgentTelemetry.Source` is defined in section-09. Reference it as `AgentTelemetry.Source?.StartActivity("skill.load")` using null-conditional so this compiles before section-09 is complete.

**SkillsPath missing:** Throw `DirectoryNotFoundException` with a clear message during constructor execution.

**SkillsPath outside BaseDirectory:** Log warning if custom absolute path falls outside `AppContext.BaseDirectory` (allows Docker volume mounts but flags unexpected config).

**Startup validation:**

```
missingIds = RequiredSkillIds.Except(discoveredIds, OrdinalIgnoreCase)
if missingIds.Any():
  if Production:
    throw InvalidOperationException($"Required skill(s) missing: {string.Join(", ", missingIds)}")
  else:
    logger.LogWarning("Required skill(s) missing in Development: {MissingIds}", missingIds)
```

**Startup logging (Information level):**
```
SkillRegistry discovered {Count} skill(s): {SkillIds}
SHA-256 [{SkillId}]: {Hash}    (one line per skill, using SHA256.HashData)
```

---

## Depth Limiting Detail

After globbing with `**/SKILL.md`, filter out paths where the relative path (from skills root) has more than 3 directory separator characters:

- `writer/SKILL.md` — depth 1, included
- `a/b/c/SKILL.md` — depth 3, included
- `a/b/c/d/SKILL.md` — depth 4, excluded

---

## Known Constraints

- **No hot-reload:** Singleton. Changes require restart. Unlike Liquid templates (FileSystemWatcher), skills are versioned config.
- **Single-file publish not supported in Phase 1** — document in SkillOptions XML doc.
- Level 2 body returned raw (unrendered Liquid). Rendering is in `AgentCapabilityBase` (section-08) via `IPromptTemplateService.RenderRawAsync`.

---

## As-Built Notes

**Implemented as planned** with the following deviations:

### Deviations from Plan
1. **`SkillCacheEntry` extended with `RawContent` and `Sha256Hash`** — Code review (W1+W2): storing raw file content at discovery time eliminates a second disk read for Level2 body extraction and SHA-256 hashing. Parsed from cached string, not from disk.

2. **`GetAllSkills` result cached in constructor** — Code review (W3): singleton was allocating a new `List<T>` + `ReadOnlyCollection<T>` on every call. Now computed once at construction.

3. **`Directory.EnumerateFiles` used instead of `FileSystemGlobbing.Matcher`** — Simpler implementation; avoids adding the NuGet package. Depth filtering via `Path.GetRelativePath` + separator count achieves identical behavior.

4. **`skill.load` Activity span deferred** — `AgentTelemetry` (section-09) does not exist yet. The span is intentionally omitted here; section-09 will add it when the telemetry infrastructure is in place.

5. **Concurrency test uses consistency check, not read-count** — `SkillRegistry` is `sealed`, so a test-only subclass for counting reads is not possible. 50 concurrent `LoadLevel2` calls verified to return identical results. The `ExecutionAndPublication` lazy mode guarantees single materialization by design.

### Files Created/Modified
- `src/.../Skills/SkillOptions.cs` (22 lines)
- `src/.../Skills/SkillRegistry.cs` (143 lines)
- `src/.../Skills/SkillCacheEntry.cs` (modified — added RawContent, Sha256Hash)
- `tests/.../Skills/SkillRegistryTests.cs` (18 tests, 296 lines)

### Test Results
- 18/18 pass
- Infrastructure.Tests: pre-existing failure count unchanged
