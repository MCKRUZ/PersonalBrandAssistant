# Section 01 Code Review Interview

## Auto-Fixed

### HIGH #1 & #2: CrossPosts collection type
- **Finding**: `CrossPosts` used `IReadOnlyList<ContentPlatformPublish>` but EF needs mutable `List<T>` for navigation properties (same pattern as `Children`)
- **Action**: Changed to `List<ContentPlatformPublish>` in Content.cs
- **Rationale**: EF Core's change tracker needs to add/remove from navigation collections. `IReadOnlyList` backed by `[]` (array) throws on Add. Matches the fix already applied to `Children`.

### MEDIUM #1: Unused using
- **Finding**: `using PBA.Domain.Enums;` in SchemaUpdateTests.cs was unused
- **Action**: Removed
- **Rationale**: Dead import, no functional impact

## Let Go

### MEDIUM #2: Seed data jsonb properties
- **Finding**: BrandProfile seed data doesn't explicitly set Topics, Vocabulary, AvoidWords (jsonb `List<string>`)
- **Decision**: Let go. Entity defaults (`= []`) produce empty JSON arrays. Explicit seed values would add verbosity without changing behavior.

### MEDIUM #3: Test coupling to EF internals (IModelSource)
- **Finding**: Seed data test uses `IModelSource.GetModel()` with `ModelCreationDependencies` — internal EF APIs
- **Decision**: Let go. This is the only way to test seed data in EF Core 10 (runtime model throws on `GetSeedData()`). Accept the coupling — test will fail loudly on EF upgrades, which is acceptable.

### MEDIUM #4: IsDeleted index
- **Finding**: No index on `Content.IsDeleted` for soft-delete filter performance
- **Decision**: Let go. InMemory provider doesn't use indexes. Real index should be added in a migration when PostgreSQL is wired up. Not blocking for section-01.

### LOW #1-3: Pre-existing issues
- **Finding**: Timestamps set at construction, missing ScheduledAt index, missing FeedItems DbSet
- **Decision**: Let go. Pre-existing patterns, deferred per review notes.

## Verification
- All 9 tests pass after fixes
- Files re-staged
