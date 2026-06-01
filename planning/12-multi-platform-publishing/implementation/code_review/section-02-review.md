# Section 02: Domain Model Changes -- Code Review

**Reviewer:** code-reviewer agent
**Verdict:** APPROVE with warnings

No critical issues. The new entity, EF configurations, ValidationBehavior, and test coverage are solid. Two warnings merit attention -- one about migration drift that silently drops constraints, one about the PlatformCredential uniqueness gap. Three suggestions for downstream resilience.

---

## Findings

### [WARNING] Migration silently drops Idea/IdeaSource constraints and DeduplicationKey index
**File:** `src/PBA.Infrastructure/Data/Migrations/20260527124807_AddMultiPlatformPublishing.cs`
**Severity:** Warning

This migration does far more than add multi-platform publishing columns. It also:

1. **Drops `IX_Ideas_DeduplicationKey` index** -- supports deduplication queries on 3800+ row Ideas table.
2. **Removes `MaxLength` constraints** from 11 Idea and IdeaSource columns (all changed from `varchar(N)` to `text`).
3. **Changes `Ideas.Tags` and `SavedIdeas.Tags/SuggestedPlatforms`** from `jsonb` to `text[]`.
4. **Removes `OnDelete(SetNull)`** from `Ideas -> IdeaSources` FK relationship.

**Root cause:** The initial migration (20260511) was generated from a model state that had EF configurations for Idea/IdeaSource (never committed to this branch). No `IdeaConfiguration.cs` or `IdeaSourceConfiguration.cs` exists. EF detected the divergence and included corrections.

**Impact:** DeduplicationKey index drop affects idea dedup query performance. Constraint drops are less critical (PostgreSQL `text` is functionally equivalent to `varchar(n)`) but remove defense-in-depth.

**Fix:** Create `IdeaConfiguration.cs` and `IdeaSourceConfiguration.cs` to restore constraints/index, then regenerate migration. Or split into a separate, named migration (`DropIdeaLegacyConstraints`) if intentional.

```csharp
// src/PBA.Infrastructure/Data/Configurations/IdeaConfiguration.cs
public class IdeaConfiguration : IEntityTypeConfiguration<Idea>
{
    public void Configure(EntityTypeBuilder<Idea> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Title).IsRequired().HasMaxLength(500);
        builder.Property(i => i.DeduplicationKey).IsRequired().HasMaxLength(500);
        builder.Property(i => i.SourceName).IsRequired().HasMaxLength(200);
        builder.Property(i => i.Url).HasMaxLength(2000);
        builder.Property(i => i.ThumbnailUrl).HasMaxLength(2000);
        builder.Property(i => i.Category).HasMaxLength(100);
        builder.HasIndex(i => i.DeduplicationKey);
        builder.HasOne(i => i.IdeaSource)
            .WithMany(s => s.Ideas)
            .HasForeignKey(i => i.IdeaSourceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
```

---

### [WARNING] PlatformCredential has no unique constraint on (Platform, IsActive=true)
**File:** `src/PBA.Infrastructure/Data/Configurations/PlatformCredentialConfiguration.cs:19`
**Severity:** Warning

The composite index on `{Platform, IsActive}` is non-unique. One-active-per-platform is enforced at service layer (section-06). Without a DB constraint, race conditions could leave two active credentials for the same platform.

**Recommendation:** Add a filtered unique index (PostgreSQL partial unique index):

```csharp
builder.HasIndex(c => c.Platform)
    .IsUnique()
    .HasFilter("\"IsActive\" = true");
```

Allows multiple inactive credentials per platform (audit) but enforces one active.

---

### [SUGGESTION] ValidationBehavior reflection path should be cached
**File:** `src/PBA.Application/Common/Behaviors/ValidationBehavior.cs:36-39`
**Severity:** Suggestion

The reflection call `typeof(TResponse).GetMethod(...)` runs on every validation failure. Cache `MethodInfo` in a static `ConcurrentDictionary<Type, MethodInfo>`. Minor perf concern, but fragile: if `Result<T>.ValidationFailure` is renamed, `method!` throws `NullReferenceException` at runtime instead of a compile error.

```csharp
private static readonly ConcurrentDictionary<Type, MethodInfo> _cache = new();

var method = _cache.GetOrAdd(typeof(TResponse), type =>
    type.GetMethod(nameof(Result<object>.ValidationFailure),
        [typeof(IReadOnlyList<string>)])
    ?? throw new InvalidOperationException(...));
```

---

### [SUGGESTION] PlatformCredential.Platform should be init-only
**File:** `src/PBA.Domain/Entities/PlatformCredential.cs:8`
**Severity:** Suggestion

`Platform` is `{ get; set; }` but should never change after creation. Use `{ get; init; }` per immutability-first convention.

**Fix:** `public Platform Platform { get; init; }`

---

### [SUGGESTION] Content.TargetPlatforms -- IReadOnlyList tension
**File:** `src/PBA.Domain/Entities/Content.cs:18`
**Severity:** Suggestion

Public collections should use `IReadOnlyList<T>`. `TargetPlatforms` is `List<Platform>`. EF Core requires concrete `List<T>` for JSON column materialization. Same applies to pre-existing `Tags` -- codebase-wide pattern, not a regression.

---

### [NOTE] Test fixes are pre-existing cleanup, not section-02 changes
**Severity:** Note

Roughly 60% of the diff is test compilation fixes for pre-existing issues:
1. `ContentType.BlogPost` -> `ContentType.Blog` (renamed in c3371f6)
2. `Idea.SourceName` became `required` (c3371f6) -- tests now provide `SourceName`
3. `IdeaSourceType.Manual` -> `IdeaSourceType.API` (c3371f6)

Legitimate fixes. 437 passing tests confirm no regressions.

---

### [NOTE] TargetPlatforms jsonb configuration is correct
**Severity:** Note

`Content.TargetPlatforms` (`List<Platform>`) configured as `jsonb`. EF Core 10/Npgsql stores enum values as string names. Resilient to enum reordering. Migration default `[]` for existing rows is correct.

---

### [NOTE] ValidationBehavior DI registration is correctly ordered
**Severity:** Note

`cfg.AddOpenBehavior(typeof(ValidationBehavior<,>))` is the only pipeline behavior. Validation should run first if more behaviors added later. Correct.

---

## Summary

| Severity | Count | Action Required |
|----------|-------|----------------|
| Critical | 0 | -- |
| Warning | 2 | Should fix before merge |
| Suggestion | 3 | Consider improving |
| Note | 3 | Informational |

**Blocking issues:** None.

**Recommended before merge:**
1. Create `IdeaConfiguration.cs` and `IdeaSourceConfiguration.cs` (or separate migration) to address constraint/index drift -- especially the DeduplicationKey index.
2. Consider the filtered unique index on PlatformCredential for the one-active-per-platform invariant.

**Non-blocking improvements:**
- Cache reflection in ValidationBehavior
- Make `PlatformCredential.Platform` init-only
- Consider IReadOnlyList for TargetPlatforms (codebase-wide decision)
