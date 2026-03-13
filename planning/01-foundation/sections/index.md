<!-- PROJECT_CONFIG
runtime: dotnet
test_command: dotnet test
END_PROJECT_CONFIG -->

<!-- SECTION_MANIFEST
section-01-scaffolding
section-02-domain
section-03-application
section-04-infrastructure
section-05-api
section-06-docker
section-07-angular
section-08-testing
section-09-verification
END_MANIFEST -->

# Implementation Sections Index

## Dependency Graph

| Section | Depends On | Blocks | Parallelizable |
|---------|------------|--------|----------------|
| section-01-scaffolding | - | all | Yes |
| section-02-domain | 01 | 03, 04, 08 | No |
| section-03-application | 02 | 04, 05, 08 | No |
| section-04-infrastructure | 03 | 05, 08 | No |
| section-05-api | 04 | 06, 08 | No |
| section-06-docker | 05 | 07, 09 | Yes |
| section-07-angular | 01 | 09 | Yes (parallel with 02-05) |
| section-08-testing | 05 | 09 | No |
| section-09-verification | 06, 07, 08 | - | No |

## Execution Order

1. **Batch 1:** section-01-scaffolding (no dependencies)
2. **Batch 2:** section-02-domain (after 01), section-07-angular (after 01) — parallel
3. **Batch 3:** section-03-application (after 02)
4. **Batch 4:** section-04-infrastructure (after 03)
5. **Batch 5:** section-05-api (after 04)
6. **Batch 6:** section-06-docker (after 05), section-08-testing (after 05) — parallel
7. **Batch 7:** section-09-verification (after 06, 07, 08)

## Section Summaries

### section-01-scaffolding
Create .sln, all project files, Directory.Build.props, NuGet package references, project references. Establish Clean Architecture structure.

### section-02-domain
Entity base class (UUIDv7), all domain entities (Content with TPH, Platform, BrandProfile, ContentCalendarSlot, AuditLogEntry, User), enums, Content status state machine, ContentMetadata complex type, domain events.

### section-03-application
Result<T> with ErrorCode, interfaces (IApplicationDbContext, IEncryptionService, IDateTimeProvider), MediatR pipeline behaviors (validation, logging), Content CRUD commands/queries with keyset pagination, FluentValidation validators.

### section-04-infrastructure
ApplicationDbContext with entity configurations (TPH, jsonb, xmin, query filters, array types, indexes), EF migrations, encryption service (Data Protection API), auditable interceptor, audit log interceptor, seed data hosted service, audit log cleanup service, DependencyInjection.cs.

### section-05-api
Program.cs with service registration, API key authentication middleware, Content endpoints with Result-to-HTTP mapper, ProblemDetails error responses, health endpoints, global exception handler, Swagger configuration, CORS.

### section-06-docker
API Dockerfile (multi-stage), Angular Dockerfile (prod with nginx), Angular Dockerfile.dev, docker-compose.yml (production), docker-compose.override.yml (dev), .env.example, nginx.conf for Angular SPA.

### section-07-angular
Angular 19 project creation, PrimeNG setup, app shell layout (sidebar, header, main), lazy-loaded routing, shared components (PageHeader, StatusBadge, LoadingSpinner, EmptyState), NgRx signals store, API service with key interceptor, environment configuration.

### section-08-testing
Domain tests (state machine, entity creation), application tests (handlers, validators, behaviors, Result mapping), infrastructure integration tests (Testcontainers, migrations, CRUD, encryption, concurrency, query filters, auth).

### section-09-verification
End-to-end Docker Compose validation: stack starts, API health returns 200, CRUD operations work, Angular serves, data persists across restart.
