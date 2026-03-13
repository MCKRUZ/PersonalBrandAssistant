# Research Findings — 01 Foundation

## .NET 10 & EF Core 10 Status (March 2026)

### .NET 10 — Production Ready (LTS)
- Released November 2025, currently v10.0.4 (patched March 10, 2026)
- **LTS release** supported until November 14, 2028
- C# 14: field-backed properties, extension blocks, null-conditional assignment, partial constructors
- Runtime: improved JIT, NativeAOT enhancements
- ASP.NET Core 10: OpenAPI enhancements, passkey support
- Native container image creation from console apps

### EF Core 10 — Key Features
| Feature | Impact |
|---------|--------|
| **Complex types with ToJson()** | **Recommended** over owned entities for jsonb mapping. Value semantics, ExecuteUpdateAsync support. |
| **LeftJoin/RightJoin LINQ** | No more SelectMany/GroupJoin/DefaultIfEmpty |
| **Named query filters** | Multiple named filters per entity with selective disable |
| **ExecuteUpdateAsync for JSON** | Bulk update JSON properties in complex types |
| **Parameterized collections** | Multiple scalar params with padding (better query plans) |
| **SQL injection analyzer** | `FromSqlRaw` warnings |

### Breaking Changes from EF Core 9
- EF tools require `--framework` for multi-target projects
- Application Name injected into connection string
- Complex type column names uniquified
- DateTime/DateTimeOffset handling changed in SQLite (not relevant for PostgreSQL)

### Npgsql EF Core 10.0.1
- Full support for EF 10's ComplexProperty().ToJson()
- More efficient jsonb scalar collection SQL (`@>` operator)
- PostgreSQL 18 virtual generated columns, UUIDv7 support

## EF Core PostgreSQL jsonb Patterns

### Recommended: Complex Types with ToJson()
```csharp
public class Content { public ContentMetadata Metadata { get; set; } }
public class ContentMetadata { public List<string> Tags { get; set; } public Dictionary<string, string> PlatformData { get; set; } }

modelBuilder.Entity<Content>().ComplexProperty(c => c.Metadata, m => m.ToJson());
```
- Supports nested types, collections, structs
- Full LINQ querying translates to PostgreSQL jsonb operators
- ExecuteUpdateAsync works for partial updates

### GIN Indexing
- Use `jsonb_path_ops` for containment queries (most common)
- Use default `jsonb_ops` if key-existence queries needed

### Schema-less Data
- Use `JsonDocument` for truly dynamic data (limited LINQ, requires disposal)

## Encrypted Column Patterns

### Recommended: Value Converter + ICryptographyService
```csharp
public interface ICryptographyService { string Encrypt(string data); string Decrypt(string data); }
// Use with value converters per-property or attribute-based scanning
```

### Key Management
- **Dev:** ASP.NET Data Protection API or User Secrets
- **Prod:** Azure Key Vault (or DPAPI on self-hosted)
- **Critical:** Encrypted columns cannot be WHERE-filtered. Use hash columns if needed.

### Attribute-Based Pattern
- `[Encrypted]` attribute on properties
- Scan all entities in `OnModelCreating` to apply converters
- Scales well across many entities

## Docker Compose — .NET 10 + PostgreSQL + Angular

### Dockerfile Best Practices
- Multi-stage builds: SDK for build, aspnet for runtime
- **Ubuntu Chiseled images** (~110MB) for production
- Alpine images (~218MB) as alternative
- Layer caching: copy .csproj first, restore, then copy source

### Docker Compose Pattern
- Use `service_healthy` condition with `pg_isready` health check for PostgreSQL
- Anonymous volumes for `node_modules` in Angular container
- `.env` file for secrets (gitignored)
- `docker-compose.override.yml` for dev-specific config
- PostgreSQL 17 recommended (20x faster vacuum, 2x write throughput)

### Synology NAS Deployment
- **Container Manager** (DSM 7.2+) supports Docker Compose via Project tab
- Docker Engine is outdated (24.0.2) — security consideration
- **Build images externally**, push to registry, pull on NAS (building on NAS is slow)
- Use `/volume1/docker/` paths for volumes
- Verify x86 CPU (ARM NAS units won't run .NET)
- Minimum 4GB RAM, ideally 8GB+ for this stack
- Use `pg_dump` via cron for database backups
- Consider Portainer for better Docker management

## Testing Setup (New Project)
- **Framework:** xUnit (standard for .NET, integrates with `dotnet test`)
- **Mocking:** Moq or NSubstitute
- **Integration:** `WebApplicationFactory<Program>` with Testcontainers for PostgreSQL
- **Coverage:** `dotnet test --collect:"XPlat Code Coverage"` with Coverlet

## Sources
- [What's new in .NET 10](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview)
- [EF Core 10 What's New](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew)
- [EF Core 10 Breaking Changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes)
- [Npgsql EF Core 10.0 Release Notes](https://www.npgsql.org/efcore/release-notes/10.0.html)
- [Npgsql JSON Mapping](https://www.npgsql.org/efcore/mapping/json.html)
- [Docker Multi-Stage Builds Guide 2026](https://devtoolbox.dedyn.io/blog/docker-multi-stage-builds-guide)
- [Synology Container Manager](https://www.wundertech.net/container-manager-on-a-synology-nas/)
