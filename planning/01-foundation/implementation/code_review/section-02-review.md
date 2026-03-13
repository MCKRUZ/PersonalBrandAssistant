# Section 02 -- Domain Layer: Code Review

**Reviewer:** code-reviewer agent
**Date:** 2026-03-13
**Verdict:** BLOCK (1 CRITICAL, 1 HIGH -- both straightforward fixes)

---

## CRITICAL Issues

### [CRITICAL-01] Domain project has a NuGet dependency on MediatR.Contracts

**File:** src/PersonalBrandAssistant.Domain/PersonalBrandAssistant.Domain.csproj (line 4)

The spec states unambiguously that the Domain must have zero NuGet dependencies -- no MediatR, no FluentValidation, no EF Core. The design decision section clarifies that IDomainEvent is a plain marker interface and the Application layer wraps events via a DomainEventNotification adapter.

The implementation correctly defines IDomainEvent as a plain marker interface with no reference to MediatR anywhere in the domain code. However, the .csproj still declares a PackageReference to MediatR.Contracts Version 2.0.1. This dependency is unused and violates the zero-dependency constraint. It also creates a transitive coupling: any project referencing the Domain project will silently pull in MediatR.Contracts.

**Fix:** Remove the ItemGroup containing the MediatR.Contracts PackageReference from the .csproj. The file should contain only the Project Sdk root element with no ItemGroups (target framework and other common properties are inherited from Directory.Build.props).

---

## HIGH Issues

### [HIGH-01] AuditLogEntry inherits IAuditable through EntityBase, contradicting the spec

**File:** src/PersonalBrandAssistant.Domain/Entities/AuditLogEntry.cs (line 5)

The spec explicitly states that AuditLogEntry does not need IAuditable -- it has its own Timestamp field and is write-once (never updated).

Because AuditLogEntry inherits from EntityBase, and EntityBase implements IAuditable, the Infrastructure layer SaveChangesInterceptor will automatically set CreatedAt and UpdatedAt on audit log entries. This is redundant with the entity Timestamp field and semantically incorrect -- UpdatedAt implies mutability, but audit logs are append-only by design.

This is rated HIGH rather than CRITICAL because the functional impact is limited (the interceptor will harmlessly set timestamps), but it introduces a semantic contradiction that will confuse future developers and could cause subtle bugs if the interceptor logic evolves.

**Fix options (choose one):**

1. **Preferred:** Split EntityBase into two classes: a non-auditable EntityBase (with Id, domain events only) and an AuditableEntityBase that extends it and adds CreatedAt/UpdatedAt via IAuditable. Then AuditLogEntry inherits EntityBase directly, while all other entities inherit AuditableEntityBase.

2. **Minimal:** Keep the current hierarchy but exclude AuditLogEntry from the auditable interceptor filter in the Infrastructure layer (document this decision with a comment in both places). Less clean but defers the refactor.

---

## MEDIUM Issues (Warnings)

### [MEDIUM-01] Value objects are classes, not records -- missed opportunity for structural equality

**Files:** All files under src/PersonalBrandAssistant.Domain/ValueObjects/

The spec acknowledges these must be mutable classes for EF Core complex type compatibility, so this is not a violation. No action required now. Documenting for awareness that a future EF Core release supporting record complex types would allow migration to records with structural equality and with-expression support.

### [MEDIUM-02] Missing XML doc comment on ContentStateChangedEvent

**File:** src/PersonalBrandAssistant.Domain/Events/ContentStateChangedEvent.cs

The spec includes a summary XML doc comment on this record. The implementation omits it. Since this is the only domain event and serves as the pattern for all future events, the doc comment should be included.

**Fix:** Add the XML doc comment above the record declaration.

### [MEDIUM-03] UUIDv7 chronological ordering test could use a clarifying comment

**File:** tests/PersonalBrandAssistant.Domain.Tests/Common/EntityBaseTests.cs (lines 22-29)

The test SequentialEntities_HaveChronologicallyOrderedIds asserts >= on the string comparison. This is correct and the test is sound. Consider adding a brief comment explaining why string comparison works for UUIDv7 (the timestamp occupies the most-significant bits and the standard GUID string format preserves this ordering).

### [MEDIUM-04] Moq dependency in test project is unnecessary

**File:** tests/PersonalBrandAssistant.Domain.Tests/PersonalBrandAssistant.Domain.Tests.csproj (line 9)

The test project references Moq 4.20.72 but no tests in this section use mocking. Consider removing it until the Application layer tests (Section 03) actually need it.

---

## Suggestions (Low Priority)

### [SUGGESTION-01] Consider making Content.Create validate required parameters

**File:** src/PersonalBrandAssistant.Domain/Entities/Content.cs (lines 28-41)

The Create factory method accepts body as a required string but does not validate it. A lightweight guard clause (ArgumentException.ThrowIfNullOrWhiteSpace) would enforce the invariant at the domain level. This is a suggestion -- the spec does not mandate domain-level validation.

### [SUGGESTION-02] Consider using FrozenDictionary for the transition table

**File:** src/PersonalBrandAssistant.Domain/Entities/Content.cs (lines 3-13)

The AllowedTransitions dictionary is initialized once and never modified. .NET 10 provides FrozenDictionary (from System.Collections.Frozen), a BCL type optimized for read-heavy write-never scenarios. Minor performance improvement that also signals intent.

---

## Spec Compliance Summary

| Spec Requirement | Status | Notes |
|-----------------|--------|-------|
| File structure matches spec | PASS | All 17 domain files + 9 test files present |
| Enums have correct values/counts | PASS | All 4 enums verified |
| EntityBase uses Guid.CreateVersion7() | PASS | |
| IAuditable interface | PASS | |
| IDomainEvent as plain marker | PASS | |
| Content state machine transitions | PASS | All 8 states and transitions match spec table exactly |
| Content.Status has private setter | PASS | private set on Status property |
| Content.Create factory method | PASS | Private constructor + static Create method |
| ContentStateChangedEvent is sealed record | PASS | |
| Value objects are mutable classes with defaults | PASS | All 5 value objects correct |
| Platform tokens are byte[] | PASS | EncryptedAccessToken and EncryptedRefreshToken are byte[]? |
| Zero NuGet dependencies | **FAIL** | MediatR.Contracts 2.0.1 in .csproj (CRITICAL-01) |
| AuditLogEntry does not need IAuditable | **FAIL** | Inherits via EntityBase (HIGH-01) |
| 42 test cases total | PASS | 2 + 22 + 3 + 2 + 2 + 3 + 2 + 2 + 4 = 42 |
| Tests cover all spec scenarios | PASS | All 20 transition cases, event raise, platform tests, etc. |

---

## Security Assessment

| Check | Result |
|-------|--------|
| Hardcoded credentials | PASS -- No secrets in code |
| Token storage | PASS -- byte[] encrypted tokens, never auto-decrypted |
| Input validation | N/A -- Domain layer; validation at Application boundary per spec |
| SQL injection | N/A -- No data access in domain |
| XSS | N/A -- No rendering in domain |

---

## Test Count Verification

| Test File | Test Count | Method |
|-----------|-----------|--------|
| EntityBaseTests.cs | 2 | 2 Facts |
| ContentTests.cs | 22 | 1 Fact + 18 Theory + 3 Facts |
| ContentMetadataTests.cs | 3 | 3 Facts |
| PlatformTests.cs | 2 | 2 Facts |
| BrandProfileTests.cs | 2 | 2 Facts |
| ContentCalendarSlotTests.cs | 3 | 3 Facts |
| AuditLogEntryTests.cs | 2 | 2 Facts |
| UserTests.cs | 2 | 2 Facts |
| EnumTests.cs | 4 | 4 Facts |
| **Total** | **42** | Matches spec requirement |

---

## Verdict

**BLOCK** -- Two issues must be resolved before merge:

1. **CRITICAL-01:** Remove the unused MediatR.Contracts NuGet dependency from the Domain .csproj. This is a one-line deletion.
2. **HIGH-01:** Address the AuditLogEntry inheriting IAuditable contradiction. The preferred fix (splitting EntityBase into auditable/non-auditable variants) is a small refactor touching 3 files.

Both fixes are straightforward and should not require re-running the full test suite beyond a build and test pass. After these are addressed, the section is ready to approve.
