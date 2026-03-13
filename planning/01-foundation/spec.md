# 01 — Foundation & Infrastructure

## Overview
Solution scaffolding, shared domain models, database infrastructure, Docker environment, and API skeleton for the Personal Brand Assistant. Everything else builds on this.

## Requirements Reference
See `../requirements.md` for full project context and `../deep_project_interview.md` for design decisions.

## Stack
- .NET 10 (Preview), C#
- PostgreSQL 16+ (Docker container)
- EF Core 10 + Npgsql provider
- Angular 19 (scaffold only — full build in 06)
- Docker Compose for local development
- Self-hosted deployment target: Synology NAS

## Scope

### Solution Structure
- Clean Architecture or Vertical Slices (decide during /deep-plan)
- Project layout: API, Domain, Application, Infrastructure, Tests
- Shared contracts/interfaces that other splits will implement

### Domain Models (Core Entities)
- **Content** — Central entity. Types: BlogPost, SocialPost, Thread, VideoDescription. Has lifecycle state, platform target(s), parent/child relationships (blog → thread → posts).
- **Platform** — Enum + config: Twitter/X, LinkedIn, Instagram, YouTube. Stores connection status, OAuth tokens (encrypted), rate limit state.
- **BrandProfile** — Voice settings, tone descriptors, keywords, topics, persona definition. Injected into all AI prompts.
- **ContentCalendar** — Strategy/themes by time period, scheduled slots.
- **User** — App user (likely single-user initially, but model for multi-tenant future).
- **AuditLog** — Tracks all state transitions and actions.

### Database
- PostgreSQL with EF Core migrations
- JSON columns for flexible content metadata (PostgreSQL `jsonb`)
- Encrypted columns for OAuth tokens and secrets
- Seed data for brand profile and platform configurations

### API Skeleton
- Minimal APIs or MediatR/CQRS (decide during /deep-plan)
- Health check endpoints
- Swagger/OpenAPI documentation
- CORS configuration for Angular dev server
- Global error handling with structured responses (Result<T> pattern)
- Request/response logging

### Docker Compose
- API container (.NET 10)
- PostgreSQL container
- Angular dev server (hot reload)
- Volume mounts for database persistence
- Environment variable management

### Cross-Cutting Infrastructure
- Structured logging (Serilog → console + file)
- Configuration management (appsettings + User Secrets + environment variables)
- FluentValidation setup for DTO validation
- Dependency injection conventions
- Unit test project setup (xUnit + Moq)

## Out of Scope
- Workflow engine logic (→ 02)
- AI/LLM integration (→ 03)
- Platform API calls (→ 04)
- Content creation logic (→ 05)
- Angular UI beyond scaffold (→ 06)

## Key Decisions Needed During /deep-plan
1. Clean Architecture vs Vertical Slices — which fits better for a modular agent system?
2. Minimal APIs vs MediatR/CQRS — simplicity vs structure?
3. Single-user vs multi-tenant data model from day one?
4. How to handle encrypted storage for OAuth tokens (EF Core value converters vs dedicated service)?

## Dependencies
- **Depends on:** Nothing (this is Wave 1)
- **Blocks:** All other splits (02, 03, 04, 05, 06)

## Definition of Done
- Solution builds and runs in Docker Compose
- Database migrations apply cleanly
- Health check endpoint returns 200
- Swagger UI accessible
- Angular scaffold serves on localhost
- All domain models mapped to database
- Unit test project runs with at least one passing test
