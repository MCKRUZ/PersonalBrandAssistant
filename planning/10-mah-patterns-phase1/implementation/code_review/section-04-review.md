# Section 04: Code Review — SkillRegistry

**Verdict: Warning — 3 issues to fix before commit**

## W1: Double-read divergence in ReadLevel2Body
ReadLevel2Body re-reads the SKILL.md file from disk. File could change between startup and first LoadLevel2 call, causing Level1 metadata and Level2 body to diverge.
Fix: Store RawContent in SkillCacheEntry at discovery time; extract Level2 body from cached string.

## W2: LogStartupInfo re-reads all files for SHA-256
Discover already reads all files (File.ReadAllText). LogStartupInfo calls File.ReadAllBytes again. That's 2N reads at startup.
Fix: Compute SHA-256 during Discover, store in SkillCacheEntry.

## W3: GetAllSkills allocates on every call
Singleton creates new List<T> + ReadOnlyCollection<T> on each call. Fix: compute once in constructor.

## S1: ComputeSha256 dead code after fix (remove the static method, inline into Discover)
## S2: ComputeRelativeDepth symlink edge case (comment, not a real issue in this context)
## S3: Missing test for custom SkillsPath outside BaseDirectory (low priority)

## Thread Safety
ConcurrentDictionary + Lazy<T> with ExecutionAndPublication is correct. No issues.
