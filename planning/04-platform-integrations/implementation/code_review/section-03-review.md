# Section 03 - EF Core Configuration Review

**Reviewer:** code-reviewer (Claude Opus 4.6)
**Date:** 2026-03-14
**Verdict:** WARNING -- mergeable with fixes noted below

---

## Summary

This section adds EF Core configurations for `ContentPlatformStatus` and `OAuthState`, adds the `GrantedScopes` text[] column to `PlatformConfiguration`, registers both new DbSets, and provides corresponding test coverage. The code follows established project patterns closely and is well-structured overall.

---

## Critical Issues (must fix)

### [CRITICAL] OAuthState missing xmin concurrency token

**File:** `OAuthStateConfiguration.cs`
**Issue:** Every other entity configuration in the project that extends `EntityBase` or `AuditableEntityBase` includes a PostgreSQL `xmin` concurrency token. `OAuthState` omits it. While OAuth state records are short-lived, concurrent callback handling could cause silent overwrites without optimistic concurrency protection.

**Fix:** Add the xmin block to `OAuthStateConfiguration.Configure()`:

```csharp
builder.Property<uint>("xmin")
    .HasColumnType("xid")
    .ValueGeneratedOnAddOrUpdate()
    .IsConcurrencyToken();
```

Rationale: Consistency is the primary driver here. If this entity is intentionally excluded from concurrency control, that decision should be documented in a code comment. But given that race conditions on OAuth callbacks are a real scenario (user clicks "authorize" twice quickly), the token is warranted.

---

## Warnings (should fix)

### [HIGH] ContentPlatformStatus missing configurations for NextRetryAt and PublishedAt

**File:** `ContentPlatformStatusConfiguration.cs`
**Domain entity:** `ContentPlatformStatus.cs` lines 16-18

The domain entity declares `NextRetryAt`, `PublishedAt`, and `Version` properties, but the configuration does not explicitly configure any of them. While EF Core will map them by convention, the existing `ContentConfiguration` explicitly configures `NextRetryAt` (line 30-31), establishing a pattern of explicit configuration for nullable DateTimeOffset properties. The `Version` property also appears to be a domain-level version counter separate from the `xmin` concurrency token but is never configured.

**Fix:** Add explicit configuration for completeness:

```csharp
builder.Property(c => c.NextRetryAt);
builder.Property(c => c.PublishedAt);
builder.Property(c => c.Version);
```

### [HIGH] OAuthState CodeVerifier should be encrypted or at minimum acknowledged as sensitive

**File:** `OAuthStateConfiguration.cs` line 96
**Issue:** The PKCE `CodeVerifier` is stored as plaintext with a 200-character max length. While OAuth state records are short-lived, the code verifier is a security-sensitive value -- if the database is compromised, an attacker with access to unexpired OAuth state records could potentially complete an authorization code exchange.

**Fix options:**
1. Store it encrypted (consistent with how `Platform.EncryptedAccessToken` / `EncryptedRefreshToken` are handled).
2. If plaintext is an intentional trade-off given the short TTL, add a code comment documenting that decision.
3. Consider whether a shorter max length (128 chars per RFC 7636 maximum) is more appropriate.

### [MEDIUM] Duplicate test methods: DbSet registration tests repeat entity registration tests

**File:** `ApplicationDbContextConfigurationTests.cs`
**Tests:** `DbContext_IncludesContentPlatformStatusesDbSet` (line 225) and `ContentPlatformStatus_IsRegistered` (line 129) are functionally identical -- both assert `context.Model.FindEntityType(typeof(ContentPlatformStatus))` is not null. Same duplication exists for OAuthState (line 231 vs line 184).

**Fix:** Remove the `DbContext_Includes*DbSet` tests, or change them to verify the DbSet property directly:

```csharp
[Fact]
public void DbContext_IncludesContentPlatformStatusesDbSet()
{
    using var context = CreateInMemoryContext();
    var dbSetProperty = typeof(ApplicationDbContext)
        .GetProperties()
        .FirstOrDefault(p => p.PropertyType == typeof(DbSet<ContentPlatformStatus>));
    Assert.NotNull(dbSetProperty);
}
```

### [MEDIUM] ContentPlatformStatus composite index order may not be optimal

**File:** `ContentPlatformStatusConfiguration.cs` line 57
**Issue:** The composite index is `{ ContentId, Platform }`. This is correct for "find all platform statuses for a given content item." However, if queries also need to find "all pending items for a specific platform" (e.g., for batch publishing), a `{ Platform, Status }` index would be beneficial.

**Fix:** Consider whether a second index is needed based on anticipated query patterns:

```csharp
builder.HasIndex(c => new { c.Platform, c.Status });
```

This is not blocking -- add it when the query patterns are confirmed.

---

## Suggestions (consider improving)

### [LOW] Tests use string literals instead of nameof for property names

**File:** `ApplicationDbContextConfigurationTests.cs`
**Example:** Line 141 uses `p.Name == "ContentId"` instead of `p.Name == nameof(ContentPlatformStatus.ContentId)`. The existing Platform test on line 46 already uses `nameof(Platform.Type)` -- the new tests should follow that pattern for refactoring safety.

### [LOW] OAuthState missing index on Platform

**File:** `OAuthStateConfiguration.cs`
**Issue:** If there is ever a need to clean up or query expired OAuth states by platform (e.g., "revoke all Twitter OAuth states"), an index on `Platform` would help. Low priority since the table should remain small with aggressive TTL-based cleanup.

### [LOW] TestEntityFactory.CreateOAuthState sets CreatedAt directly

**File:** `TestEntityFactory.cs` line 268
**Issue:** `OAuthState` extends `EntityBase` (not `AuditableEntityBase`), so `CreatedAt` is a regular property on the entity itself. The factory sets it to `DateTimeOffset.UtcNow`, which is fine. However, this means `CreatedAt` on `OAuthState` has different semantics than `CreatedAt` on `AuditableEntityBase` (which is set by an interceptor/save changes override). Worth noting for documentation clarity, no code change needed.

### [LOW] IdempotencyKey filter in unique index

**File:** `ContentPlatformStatusConfiguration.cs` line 58
**Issue:** The unique index on `IdempotencyKey` will reject null values in PostgreSQL only if there are multiple nulls (PostgreSQL treats NULLs as distinct in unique indexes, so this is actually fine). However, if the business rule is "every ContentPlatformStatus must have an idempotency key," consider making it `.IsRequired()`. If null is valid for draft/pending states, the current approach is correct.

---

## Pattern Consistency Checklist

| Pattern | ContentPlatformStatus | OAuthState | Notes |
|---|---|---|---|
| `ToTable()` | Yes | Yes | Correct |
| `HasKey(Id)` | Yes | Yes | Correct |
| `xmin` concurrency token | Yes | **MISSING** | CRITICAL |
| `Ignore(DomainEvents)` | Yes | Yes | Correct |
| Required properties marked | Yes | Yes | Correct |
| MaxLength on strings | Yes | Yes | Correct |
| Indexes for query patterns | Yes | Yes | Adequate |
| FK with delete behavior | Yes | N/A | Correct |

---

## Verdict

**WARNING** -- Merge is acceptable after addressing:
1. **CRITICAL:** Add xmin concurrency token to `OAuthStateConfiguration` for pattern consistency and concurrent callback safety.
2. **HIGH:** Explicitly configure `NextRetryAt`, `PublishedAt`, `Version` on `ContentPlatformStatus` to match existing patterns.
3. **MEDIUM:** Remove or fix the duplicate DbSet registration tests.

The remaining suggestions are non-blocking improvements that can be addressed in follow-up work.
