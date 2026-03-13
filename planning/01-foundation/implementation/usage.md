# Usage Guide — Personal Brand Assistant Foundation

## Quick Start

### Prerequisites
- .NET 10 SDK
- Node.js 20+
- Docker & Docker Compose
- PostgreSQL 17 (or use Docker)

### Development Setup

```bash
# 1. Clone and configure
cp .env.example .env
# Edit .env with your DB_PASSWORD and API_KEY

# 2. Run with Docker Compose
docker compose up -d --build

# 3. Or run locally
dotnet run --project src/PersonalBrandAssistant.Api
cd src/PersonalBrandAssistant.Web && ng serve
```

### Access Points
- **API:** http://localhost:5000
- **Angular App:** http://localhost:4200
- **Swagger:** http://localhost:5000/swagger (Development only)
- **Health Check:** http://localhost:5000/health (no auth required)

## API Reference

All endpoints except `/health` require the `X-Api-Key` header.

### Content Endpoints

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/content` | Create content |
| GET | `/api/content/{id}` | Get content by ID |
| GET | `/api/content` | List content (keyset pagination) |
| PUT | `/api/content/{id}` | Update content |
| DELETE | `/api/content/{id}` | Delete content |

### Create Content
```bash
curl -X POST http://localhost:5000/api/content \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: your-key" \
  -d '{
    "contentType": "BlogPost",
    "title": "My First Post",
    "body": "Content body here",
    "targetPlatforms": ["TwitterX", "LinkedIn"]
  }'
```

### List Content (Keyset Pagination)
```bash
curl "http://localhost:5000/api/content?pageSize=10" \
  -H "X-Api-Key: your-key"

# Next page using cursor from response
curl "http://localhost:5000/api/content?pageSize=10&cursor=BASE64_CURSOR" \
  -H "X-Api-Key: your-key"
```

## Running Tests

```bash
# All tests (Domain + Application + Infrastructure)
dotnet test

# Infrastructure tests only (requires Docker for Testcontainers)
dotnet test tests/PersonalBrandAssistant.Infrastructure.Tests/

# Angular tests
cd src/PersonalBrandAssistant.Web && ng test
```

**Test counts:** 58 infrastructure integration tests, 48 application unit tests, domain unit tests.

## Docker Compose Stack Verification

```bash
# Bash (Linux/macOS/WSL)
./scripts/verify-stack.sh

# PowerShell (Windows)
./scripts/verify-stack.ps1
```

Runs 4 automated checks: health endpoint, CRUD round-trip, Angular serves, data persistence across restart.

## Architecture Overview

```
src/
├── PersonalBrandAssistant.Domain/       # Entities, enums, value objects, events
├── PersonalBrandAssistant.Application/  # CQRS handlers, validation, Result<T>
├── PersonalBrandAssistant.Infrastructure/ # EF Core, interceptors, services
├── PersonalBrandAssistant.Api/          # Minimal APIs, middleware, endpoints
└── PersonalBrandAssistant.Web/          # Angular 19, PrimeNG, NgRx Signals

tests/
├── PersonalBrandAssistant.Domain.Tests/
├── PersonalBrandAssistant.Application.Tests/
└── PersonalBrandAssistant.Infrastructure.Tests/
```

### Key Patterns
- **Result<T>** for explicit error handling (no exceptions for expected failures)
- **MediatR CQRS** with validation and logging pipeline behaviors
- **xmin optimistic concurrency** on all entities
- **Keyset pagination** with Base64-encoded cursors
- **Timing-safe API key authentication** via SHA256 + FixedTimeEquals
- **Global query filter** excluding archived content by default
- **Audit logging** via EF Core interceptors (AuditableInterceptor + AuditLogInterceptor)

### Content Status State Machine
```
Draft → Review → Approved → Scheduled → Publishing → Published → Archived
  ↑       ↓        ↓          ↓                         ↓
  └───────┘        └──→Draft  └──→Draft     Failed──→Draft/Archived
                                              ↑
                                          Publishing
```
