# Personal Brand Assistant

AI agent that manages personal branding end to end — capturing content ideas, drafting and reviewing posts and blog articles, scheduling, multi-platform publishing, and tracking website performance. Built as a self-hosted .NET + Angular application with an LLM orchestration layer.

## Stack

- **Backend:** .NET 10, C#, Minimal APIs with MediatR/CQRS, `Result<T>` for expected failures
- **Frontend:** Angular 19 (standalone components, NgRx signals), PrimeNG 20, chart.js 4
- **Database:** PostgreSQL (EF Core)
- **AI/LLM:** routed through a sidecar/OpenRouter client (`ISidecarClient`) — never called directly from controllers/components
- **Analytics sources:** Google Analytics 4 (Data API) + Google Search Console
- **Deployment:** Docker Compose (self-hosted)

## Repository layout

```
src/
  PBA.Domain/                 Domain models, Result<T> (PBA.Domain.Common)
  PBA.Application/            CQRS features (MediatR), interfaces, DTOs
  PBA.Infrastructure/         EF Core, external services (GA4/GSC, RSS, encryption, scheduling)
  PBA.Api/                    Minimal API endpoints, DI, configuration
  PersonalBrandAssistant.Web/ Angular 19 frontend
tests/                        xUnit (backend) + Jasmine/Karma (frontend)
docs/superpowers/             Design specs and implementation plans
PBA.slnx                      Solution file
```

## Features

| Area | Frontend route | API |
|------|----------------|-----|
| Idea Bank — capture/triage content ideas | `/ideas` | `/api/ideas`, `/api/idea-sources` |
| Content Studio — draft, review, voice-check | `/content` | `/api/content` |
| Feed — activity and discovery feed | `/feed`, `/discover`, `/news` | `/api/feed` |
| Calendar — scheduling | `/calendar` | — |
| Audience listening | `/listening` | — |
| Multi-platform publishing (LinkedIn, Reddit, Twitter/X) | `/settings` | `/api/platforms`, `/api/auth` (OAuth) |
| **Website Analytics (GA4 + Search Console)** | `/analytics` | `/api/analytics` |

## Local development

```bash
# Backend
dotnet build
dotnet test
dotnet run --project src/PBA.Api

# Frontend
cd src/PersonalBrandAssistant.Web
npm install
ng serve            # http://localhost:4200
ng test --watch=false --browsers=ChromeHeadless

# Full verify
dotnet build && dotnet test && cd src/PersonalBrandAssistant.Web && ng build
```

## Deployment

The stack runs via Docker Compose (`docker-compose.yml` + `docker-compose.override.yml`): `db` (Postgres), `api`, and `web`. The api container listens on `8080` internally (published to `5001`); the web dev container serves on `4201`.

```bash
docker compose up -d --build        # build + start all services
docker compose up -d --build api    # rebuild just the api after backend changes
```

### Configuration & secrets

Runtime configuration comes from `appsettings.json` plus environment variables (via a gitignored `.env` consumed by the override). Secrets are **never** committed.

- **`.env`** supplies: `Encryption__Key`, `OPENROUTER_API_KEY` / `OPENROUTER_MODEL`, LinkedIn and Reddit OAuth credentials, and optional ComfyUI image-generation settings.
- **`secrets/`** is mounted read-only into the api at `/app/secrets`. Place service-account keys and deploy keys here. Everything under `secrets/` is gitignored.

## Website Analytics

The `/analytics` page reports live website performance for matthewkruczek.ai, sourced from Google Analytics 4 and Search Console.

**Endpoints**
- `GET /api/analytics/website?period=7d|30d|90d` (or `?from=&to=`) — overview KPIs (users, sessions, page views, new users, bounce rate, avg session), top pages, traffic sources, and top search queries.
- `GET /api/analytics/health` — `{ "ga4": bool, "searchConsole": bool }`.

**Configuration** (`appsettings.json` → `GoogleAnalytics`)
- `PropertyId` — GA4 numeric property id
- `SiteUrl` — Search Console site URL
- `CredentialsPath` — path to the service-account JSON (default `secrets/google-analytics-sa.json`; override in Docker via `GoogleAnalytics__CredentialsPath=/app/secrets/google-analytics-sa.json`)

**Service account.** A single Google service-account JSON authenticates both sources. It must be granted **Viewer** on the GA4 property *and* added as a user on the Search Console site — these are two separate grants. The key file is gitignored and volume-mounted at deploy time.

**Fault tolerance.** The GA4 and Search Console SDK clients build lazily, so a missing or unauthorized credential degrades gracefully: `/health` reports the affected source as `false` and the UI shows a warning banner instead of crashing.

## Conventions

See `CLAUDE.md` and `~/.claude/rules/` for the full engineering standards: immutable patterns, small files, `Result<T>` for expected failures, FluentValidation at boundaries, 80% test coverage on new code, and all LLM calls routed through the sidecar layer.
