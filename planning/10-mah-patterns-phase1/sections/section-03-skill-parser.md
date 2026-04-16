# Section 03 — Skill Parser

## Overview

This section implements `SkillMetadataParser`, a static utility class that reads a single `SKILL.md` file and returns a parsed `SkillDefinition`. It also defines two internal supporting records: `SkillFrontMatter` (YAML deserialization target) and `SkillCacheEntry` (pairs a `SkillDefinition` with its file path for use by the registry).

No DI registration needed — this is a pure static utility consumed by `SkillRegistry` (section 04).

**Dependencies:**
- section-01-project-setup — NuGet packages `Markdig` and `YamlDotNet` must be in `Infrastructure.csproj`
- section-02-domain-interfaces — `SkillDefinition` record must exist

**Blocks:** section-04-skill-registry

**Parallelizable with:** section-05, section-06

---

## Files to Create

| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Infrastructure/Skills/SkillMetadataParser.cs` | Static parser class |
| `src/PersonalBrandAssistant.Infrastructure/Skills/SkillCacheEntry.cs` | Internal record pairing definition + file path |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Skills/SkillMetadataParserTests.cs` | All parser tests |

`SkillFrontMatter` is a private or internal nested type within `SkillMetadataParser.cs` — not a separate file.

---

## Tests First

**Test file:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Skills/SkillMetadataParserTests.cs`

Use xUnit. Parser is static so no mocking needed — pass raw strings directly.

Helper — minimal valid SKILL.md content reusable across tests:

```csharp
private const string ValidSkillMd = """
    ---
    schema_version: 1
    name: Writer
    id: writer
    description: Blog post generation
    category: content
    tags: [blog, writing]
    skill_type: creative
    allowed_tools: []
    ---

    You are a content writer. {{ brand_voice_block }}
    """;
```

Test list:

```
# Happy path
Parse_ValidSkillMd_ReturnsSkillDefinitionWithAllFields
Parse_ValidSkillMd_IdIsNormalizedToLowercase
Parse_ValidSkillMd_ReturnsLevel2Body
Parse_ValidSkillMd_TagsAndAllowedToolsDefaultToEmptyLists

# Schema version
Parse_UnknownSchemaVersion_ReturnsNull
Parse_MissingSchemaVersion_ReturnsNull

# Required fields
Parse_MissingId_ReturnsNull
Parse_MissingName_ReturnsNull
Parse_EmptyId_ReturnsNull

# YAML edge cases
Parse_UnknownYamlKeys_IgnoresThemAndSucceeds
Parse_TagsAsString_ReturnsEmptyList

# Frontmatter structure edge cases
Parse_MissingClosingDelimiter_ReturnsNull
Parse_NoFrontmatterBlock_ReturnsNull
Parse_ExtraTripleDashInBody_BodyContainsIt
Parse_WindowsCRLFLineEndings_ParsesCorrectly
Parse_Utf8BomPrefix_ParsesCorrectly
Parse_EmptyFrontmatter_ReturnsNull

# Body extraction
Parse_MultipleBlankLinesAfterFrontmatter_BodyIsTrimmed
Parse_BodyWithLiquidSyntax_BodyReturnedVerbatim
```

Key test notes:
- `Parse_ExtraTripleDashInBody_BodyContainsIt`: a `---` in the body must not be treated as a third delimiter.
- `Parse_Utf8BomPrefix_ParsesCorrectly`: prepend `"\uFEFF"` to a valid document string.
- `Parse_BodyWithLiquidSyntax_BodyReturnedVerbatim`: assert the returned body contains the literal `{{ brand_voice_block }}` — parser does not render Liquid.
- `Parse_WindowsCRLFLineEndings_ParsesCorrectly`: construct with `\r\n` line endings explicitly.

---

## Implementation

### SkillFrontMatter (private/internal)

YAML deserialization target record. All properties map via `UnderscoredNamingConvention`.

```csharp
internal record SkillFrontMatter
{
    public int SchemaVersion { get; init; }
    public string Name { get; init; } = "";
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public List<string> Tags { get; init; } = [];
    public string SkillType { get; init; } = "";
    public string? ModelId { get; init; }
    public List<string> AllowedTools { get; init; } = [];
}
```

### SkillCacheEntry (internal record)

File: `src/PersonalBrandAssistant.Infrastructure/Skills/SkillCacheEntry.cs`

```csharp
internal record SkillCacheEntry(SkillDefinition Definition, string FilePath);
```

Level2Body lazy cache is managed separately in `SkillRegistry` — not part of `SkillCacheEntry`.

### SkillMetadataParser (static class)

```csharp
/// <summary>
/// Parses a SKILL.md file. Returns null if the file is invalid or fails validation.
/// filePath is used only for logging — not stored in the returned SkillDefinition.
/// </summary>
internal static class SkillMetadataParser
{
    private const int SupportedSchemaVersion = 1;

    public static (SkillDefinition Definition, string Level2Body)? Parse(
        string content, string filePath, ILogger logger)
    { ... }
}
```

**Parsing algorithm:**

1. Strip UTF-8 BOM (`\uFEFF`) from start of `content` if present.
2. Normalize line endings to `\n` (replace `\r\n` with `\n`).
3. Verify content starts with `---\n`. If not, log warning and return null.
4. Find the second occurrence of `\n---\n` (or `\n---` at end-of-string). This is the closing delimiter. If not found, log warning and return null.
5. Extract YAML block as substring between the two `---` markers (exclusive).
6. If YAML block is empty or whitespace only, return null.
7. Deserialize YAML block using YamlDotNet `DeserializerBuilder` with:
   - `.WithNamingConvention(UnderscoredNamingConvention.Instance)`
   - `.IgnoreUnmatchedProperties()`
8. Post-deserialization validation:
   - `SchemaVersion` must equal `SupportedSchemaVersion` (1). Otherwise log warning, return null.
   - `Id` must be non-null and non-empty after `.Trim()`. Missing → return null.
   - `Name` must be non-null and non-empty. Missing → return null.
9. Normalize `Id` to lowercase with `.ToLowerInvariant()`.
10. Default `Tags` and `AllowedTools` to empty lists if null (defensive for YAML type mismatch).
11. Extract Level 2 body as everything after the closing `---` delimiter, trimmed of leading/trailing whitespace.
12. Build and return `SkillDefinition` from validated front matter + body.

**Important:** The `---` search in step 4 looks for `\n---\n` — a `---` that appears mid-body must not be treated as a third delimiter. Search only for the first `\n---\n` occurrence after the opening.

**Error handling:** All YamlDotNet exceptions caught, logged as warnings with `filePath`, return null. Never throw from this method.

**Tags type mismatch:** If YamlDotNet assigns null to `Tags` or `AllowedTools` due to type mismatch, default both to `[]` rather than propagating the error.

---

## Key Invariants

- `SkillMetadataParser` is `internal static` — not registered with DI.
- Returned `SkillDefinition` never contains `FilePath` — stored in `SkillCacheEntry`.
- Level 2 body is returned as raw Liquid template string. Rendering happens in `AgentCapabilityBase` via `IPromptTemplateService.RenderRawAsync`.
- `Id` is always lowercase after parsing — registry can rely on this invariant.
- CRLF normalization happens first, before any delimiter scanning.

---

## As-Built Notes

**Implemented exactly as planned** with the following deviations:

### Deviations from Plan
1. **`Tags` and `AllowedTools` frozen with `.AsReadOnly()`** — Auto-fix from code review. Prevents callers from downcasting `List<string>` to mutate what are logically immutable collections on a record.

2. **One test added beyond plan** — `Parse_ClosingDelimiterAtEndOfFile_ParsesCorrectly` — exercises the `content.EndsWith("\n---")` branch that had no coverage. Total: 20 tests (plan specified ~18).

3. **`SkillFrontMatter` placed in `SkillMetadataParser.cs`** — Plan said "private or internal nested type within SkillMetadataParser.cs". Implemented as a top-level `internal record` in the same file (not nested). Either satisfies the intent; top-level avoids extra indentation.

### Files Created
- `src/PersonalBrandAssistant.Infrastructure/Skills/SkillMetadataParser.cs` (140 lines)
- `src/PersonalBrandAssistant.Infrastructure/Skills/SkillCacheEntry.cs` (5 lines)
- `tests/.../Skills/SkillMetadataParserTests.cs` (20 tests, 290 lines)

### Test Results
- 20/20 pass
- Infrastructure.Tests overall: 645+20/698+20 (pre-existing failures unchanged)
