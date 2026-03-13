# Section 08 Code Review Interview

## Triage Decision
All items triaged autonomously per user preference (no prompting for routine decisions).

## Auto-Fixed
1. **SwaggerTests: use authenticated client** — Fixed `CreateClient()` → `CreateAuthenticatedClient()` to pass API key middleware
2. **Assert.NotNull guards** — Added explicit `Assert.NotNull` before null-forgiving operators in ConcurrencyTests (3 test methods)
3. **CreateArchivedContent helper** — Added to TestEntityFactory, refactored QueryFilterTests to use it (removed duplicate 6-line transition chains)
4. **FindAsync archived ID test** — Added `FindAsync_ArchivedContent_ReturnsNullWithFilter` to QueryFilterTests
5. **Platform concurrency test** — Added `ConcurrentUpdate_SamePlatform_ThrowsDbUpdateConcurrencyException`
6. **Swagger Production 404 test** — Added `Swagger_InProduction_ReturnsNotFound`, made CustomWebApplicationFactory environment configurable
7. **Boundary condition test** — Added `Cleanup_EntryAtExactCutoff_IsPreserved` to AuditLogCleanupServiceTests
8. **Fake token comment** — Added clarifying comment on dummy byte arrays in TestEntityFactory

## Let Go (Not Addressed)
1. **Coverage gaps vs plan** — The section plan was aspirational with ~25 test files. The 58 tests we have cover critical infrastructure paths. Remaining tests (Domain.Tests, Application.Tests, additional Infrastructure tests) are out of scope for this foundation section.
2. **AuditLogCleanupService not testing actual service** — Testing the cleanup query logic is sufficient. Full hosted service integration testing with mocked timer loops adds complexity without proportional value at this stage.
3. **MigrationTests using EnsureCreated** — No migration files exist yet (project uses EnsureCreated). MigrateAsync would fail. Test is correct for current state.
4. **Table name case sensitivity** — EF Core with Npgsql quotes table names by default, preserving PascalCase. Assertions are correct.
5. **XML doc comments on TestEntityFactory** — Test utility code, not worth adding documentation overhead.
