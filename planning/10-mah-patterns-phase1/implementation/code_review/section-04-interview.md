# Section 04: Code Review Interview — section-04-skill-registry

## Triage Summary

### Auto-Fixes Applied
**1. Store RawContent + Sha256Hash in SkillCacheEntry (W1 + W2)**
- W1: `ReadLevel2Body` re-read from disk → divergence risk. Fixed: `SkillCacheEntry` now carries `RawContent`; `ExtractLevel2Body` parses from cached string.
- W2: `LogStartupInfo` called `File.ReadAllBytes` on every file again. Fixed: SHA-256 computed from `rawContent` bytes during `Discover` and stored in `SkillCacheEntry.Sha256Hash`.
- Impact: `SkillCacheEntry` gains two fields; no test changes needed (tests cover Level2 caching behavior).

**2. Cache GetAllSkills result in constructor (W3)**
- Every call previously allocated a new `List<T>` + `ReadOnlyCollection<T>`. Fixed: computed once in constructor, `GetAllSkills()` returns the cached reference.

### Let Go
- **S2 (symlink edge case in ComputeRelativeDepth)**: Symlinks to files outside skills/ are not a supported use case. No guard needed.
- **S3 (missing test for custom SkillsPath outside BaseDirectory)**: SkillsPath is effectively the test temp dir in all tests. The warning log is low-risk functionality; adding a test would require capturing log output (too much ceremony for the value). Let go.

### No User Interview Required
All fixes were straightforward corrections with no real trade-offs.

## Outcome
- 3 auto-fixes applied (W1, W2, W3 addressed together)
- Tests: 18/18 pass (no new tests needed)
