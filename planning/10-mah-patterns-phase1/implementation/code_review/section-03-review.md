# Section 03: Code Review — SkillMetadataParser

**Verdict: Approve with one warning**

## Warning (fix before commit)
**Mutable List<string> assigned to IReadOnlyList<string> — contract violation**
- `Tags` and `AllowedTools` from `SkillFrontMatter` are `List<string>` instances that get assigned to `IReadOnlyList<string>` properties on `SkillDefinition`. Callers could downcast and mutate.
- Fix: freeze with `.AsReadOnly()` before assigning.

## Suggestions (consider improving)
1. `catch (Exception ex)` breadth — pragmatic for "return null on failure" contract, but a comment would signal intent. Not blocking.
2. `Description`, `Category`, `SkillType` not validated for emptiness — will silently default to `""`. Intentional per spec (only id/name required).
3. Missing test: `\n---` at exact EOF (the `EndsWith` branch). Add one.

## Thread Safety
Static `IDeserializer` is thread-safe per YamlDotNet docs. Pattern is correct.

## Test Coverage
19 tests, strong coverage. One gap: no test exercising the `content.EndsWith("\n---")` branch.
