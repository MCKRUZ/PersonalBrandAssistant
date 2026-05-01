# Section 03: Skill Parser — Staged Diff Summary

## New Files
- `src/PersonalBrandAssistant.Infrastructure/Skills/SkillCacheEntry.cs` — internal record pairing SkillDefinition + FilePath
- `src/PersonalBrandAssistant.Infrastructure/Skills/SkillMetadataParser.cs` — static parser + SkillFrontMatter record
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Skills/SkillMetadataParserTests.cs` — 19 tests

## SkillCacheEntry
Single-line internal record:
```csharp
internal record SkillCacheEntry(SkillDefinition Definition, string FilePath);
```

## SkillFrontMatter (internal record in SkillMetadataParser.cs)
YAML deserialization target with UnderscoredNamingConvention. All props have safe defaults.

## SkillMetadataParser (internal static class)
Algorithm:
1. Strip UTF-8 BOM
2. Normalize CRLF → LF
3. Assert `---\n` prefix (else null)
4. Find closing `\n---\n` delimiter — only first occurrence after opening (body `---` not treated as delimiter)
5. Extract YAML block; return null if empty/whitespace
6. Deserialize with YamlDotNet UnderscoredNamingConvention + IgnoreUnmatchedProperties; catch all exceptions → null
7. Validate schema_version == 1; id non-empty; name non-empty
8. Normalize id to lowercase
9. Null-safe list defaults for Tags/AllowedTools
10. Extract Level2Body as trimmed substring after closing delimiter
11. Return (SkillDefinition, Level2Body) tuple

Static IDeserializer built once at class level.

## Test Coverage (19 tests)
- Happy path: all fields, lowercase id normalization, Level2Body extraction, empty tags/tools defaults
- Schema version: unknown version returns null, missing version returns null
- Required fields: missing id returns null, missing name returns null, empty id returns null
- YAML edge cases: unknown keys ignored, tags as scalar (permissive — either null or empty list)
- Frontmatter structure: no closing delimiter, no frontmatter at all, extra `---` in body preserved, CRLF line endings, UTF-8 BOM, empty frontmatter
- Body extraction: multiple blank lines trimmed, Liquid syntax returned verbatim

## Test Results
- 19/19 pass
