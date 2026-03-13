# Section 06 — Docker Environment

## Overview

This section creates the complete Docker containerization setup for the Personal Brand Assistant. It covers multi-stage Dockerfiles for the .NET 10 API and Angular 19 frontend, a production `docker-compose.yml`, a development override file, nginx configuration for SPA routing, and an environment variable template. After completing this section, the entire stack (API, PostgreSQL, Angular) can be started with a single `docker compose up` command.

## Dependencies

- **section-05-api**: The API project (`PersonalBrandAssistant.Api`) and its `Program.cs` must exist and build successfully before the API Dockerfile can produce a working image.
- **section-07-angular**: The Angular project (`src/PersonalBrandAssistant.Web/`) must exist for the Angular Dockerfiles to work. However, sections 06 and 07 can be implemented in parallel since the Dockerfiles only need the project structure and `package.json` to build.
- **section-04-infrastructure**: The database connection string configuration and Data Protection key path configuration must be in place.

## Tests

This section does not have automated unit tests. Verification is done via Docker Compose integration checks, which are formally covered in section-09-verification. The following manual/scripted checks apply:

- `docker compose build` succeeds without errors for all three services (api, db, web).
- `docker compose up -d` starts all three services and they reach healthy/running status.
- API health check at `http://localhost:5000/health` returns HTTP 200.
- Angular app serves on `http://localhost:4200` and returns an HTML page.
- PostgreSQL accepts connections on port 5432 with the credentials from `.env`.
- Data Protection keys volume (`dpkeys`) persists across container restarts (stop, then start again, verify the API can still decrypt previously encrypted data).

These checks should be run manually after all Docker files are created, or scripted as a shell verification step in section-09.

## File Inventory

All paths are relative to the repository root (`C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\personal-brand-assistant`):

| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Api/Dockerfile` | Multi-stage build for the .NET 10 API |
| `src/PersonalBrandAssistant.Web/Dockerfile` | Multi-stage production build for Angular (nginx) |
| `src/PersonalBrandAssistant.Web/Dockerfile.dev` | Development Angular container with hot reload |
| `src/PersonalBrandAssistant.Web/nginx.conf` | Nginx configuration for SPA routing and security headers |
| `docker-compose.yml` | Production orchestration (api, db, web) |
| `docker-compose.override.yml` | Development overrides (hot reload, debug ports) |
| `.env.example` | Template for required environment variables |

## Implementation Details

### 1. API Dockerfile

**File:** `C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\personal-brand-assistant\src\PersonalBrandAssistant.Api\Dockerfile`

Multi-stage build with two stages:

**Build stage:**
- Base image: `mcr.microsoft.com/dotnet/sdk:10.0-alpine`
- Working directory: `/src`
- Copy `.csproj` files first (for Docker layer caching on restore). Copy all four project files (`Domain`, `Application`, `Infrastructure`, `Api`) and the `Directory.Build.props` from the repo root.
- Run `dotnet restore` on the Api project.
- Copy entire `src/` directory, then run `dotnet publish` on the Api project with `-c Release -o /app/publish --no-restore`.

**Runtime stage:**
- Base image: `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (Ubuntu Chiseled, roughly 110MB total image).
- Working directory: `/app`
- Copy published output from the build stage.
- Expose port 8080 (the default ASP.NET Core port in .NET 8+).
- Create a directory `/data-protection-keys` for the Data Protection API key ring.
- Set `ASPNETCORE_URLS=http://+:8080`.
- Entrypoint: `dotnet PersonalBrandAssistant.Api.dll`.

**Key consideration:** The `COPY` of `.csproj` files must reflect the actual solution structure. The build context is set to the repository root in `docker-compose.yml` so that the Dockerfile can access `Directory.Build.props` and all `src/` projects.

### 2. Angular Dockerfile (Production)

**File:** `C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\personal-brand-assistant\src\PersonalBrandAssistant.Web\Dockerfile`

Multi-stage build with two stages:

**Build stage:**
- Base image: `node:22-alpine`
- Working directory: `/app`
- Copy `package.json` and `package-lock.json` first (layer caching).
- Run `npm ci` (clean install, deterministic).
- Copy the rest of the Angular source.
- Run `npx ng build --configuration production`.

**Runtime stage:**
- Base image: `nginx:alpine`
- Remove the default nginx config.
- Copy the custom `nginx.conf` to `/etc/nginx/nginx.conf`.
- Copy the built Angular output from the build stage (`/app/dist/personal-brand-assistant/browser/`) to `/usr/share/nginx/html/`.
- Expose port 80.

**Note:** The exact output directory from `ng build` depends on the Angular project name configured in section-07. The path `dist/personal-brand-assistant/browser/` assumes the project name is `personal-brand-assistant` and Angular 19 defaults.

### 3. Angular Dockerfile.dev (Development)

**File:** `C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\personal-brand-assistant\src\PersonalBrandAssistant.Web\Dockerfile.dev`

Single-stage development container:

- Base image: `node:22-alpine`
- Working directory: `/app`
- Copy `package.json` and `package-lock.json`.
- Run `npm install` (not `npm ci`, since dev dependencies are needed and lockfile may drift during development).
- Expose port 4200.
- Command: `npx ng serve --host 0.0.0.0 --poll 2000` (polling needed for volume-mounted file watching on some Docker hosts).

The source code is mounted as a volume in `docker-compose.override.yml`, so changes on the host are reflected in the container. An anonymous volume is used for `node_modules` to prevent the host's `node_modules` (if any) from overwriting the container's.

### 4. Nginx Configuration

**File:** `C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\personal-brand-assistant\src\PersonalBrandAssistant.Web\nginx.conf`

The nginx config must handle:

- **SPA fallback:** `try_files $uri $uri/ /index.html` so that Angular routes (e.g., `/content`, `/dashboard`) do not return 404 on page refresh.
- **Gzip compression:** Enable gzip for `text/html`, `application/javascript`, `text/css`, `application/json`, and `image/svg+xml`.
- **Security headers:**
  - `X-Frame-Options: DENY`
  - `X-Content-Type-Options: nosniff`
  - `X-XSS-Protection: 1; mode=block`
  - `Referrer-Policy: strict-origin-when-cross-origin`
  - `Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self' http://api:8080;`
- **Static asset caching:** Cache `*.js`, `*.css`, and image files with a long `Cache-Control` (e.g., 1 year with immutable) since Angular output files are content-hashed. The `index.html` should have `no-cache` so that updates are picked up immediately.
- **Upstream proxy (optional):** If the Angular app needs to proxy API calls to avoid CORS in production, add a `location /api/` block that `proxy_pass`es to `http://api:8080/api/`. This is the recommended production pattern (same-origin). Whether to include this depends on the CORS strategy; include it since the plan states production uses same-origin via nginx.

Structure the config as a complete `nginx.conf` with an `http` block containing a single `server` block listening on port 80.

### 5. docker-compose.yml (Production)

**File:** `C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\personal-brand-assistant\docker-compose.yml`

Three services:

**db (PostgreSQL 17):**
- Image: `postgres:17-alpine`
- Container name: `pba-db`
- Environment variables from `.env`: `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`
- Port mapping: `5432:5432`
- Health check: `pg_isready -U postgres` with interval 5s, timeout 5s, retries 5
- Volume: named volume `pgdata` mounted to `/var/lib/postgresql/data`
- Restart policy: `unless-stopped`

**api (.NET 10 API):**
- Build context: `.` (repository root), Dockerfile: `src/PersonalBrandAssistant.Api/Dockerfile`
- Container name: `pba-api`
- Port mapping: `5000:8080`
- Depends on: `db` with condition `service_healthy`
- Environment variables:
  - `ConnectionStrings__DefaultConnection=Host=db;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}`
  - `ApiKey=${API_KEY}`
  - `DataProtection__KeyPath=/data-protection-keys`
  - `ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-Production}`
- Volumes:
  - Named volume `dpkeys` mounted to `/data-protection-keys`
  - Named volume `logs` mounted to `/app/logs`
- Restart policy: `unless-stopped`

**web (Angular via nginx):**
- Build context: `src/PersonalBrandAssistant.Web`, Dockerfile: `Dockerfile`
- Container name: `pba-web`
- Port mapping: `4200:80`
- Depends on: `api`
- Restart policy: `unless-stopped`

**Named volumes** (declared at the top level):
- `pgdata` -- PostgreSQL data persistence
- `dpkeys` -- Data Protection API key ring (critical state, must be backed up)
- `logs` -- API log files

**Network:** Use the default compose network. All services can reference each other by service name (e.g., the API connects to `db`, nginx proxies to `api`).

### 6. docker-compose.override.yml (Development)

**File:** `C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\personal-brand-assistant\docker-compose.override.yml`

Docker Compose automatically merges this file with `docker-compose.yml` when running `docker compose up` without `-f` flags.

**api overrides:**
- Build from source instead of using a pre-built image (the build section stays the same, but add a volume mount for hot reload).
- Volume mount: `./src:/src` for source code access.
- Environment: `ASPNETCORE_ENVIRONMENT=Development`
- Command override: `dotnet watch run --project /src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj --urls http://+:8080` for hot reload during development.
- Additional port: `5001:5001` for potential debug/HTTPS port (optional).

**web overrides:**
- Replace the nginx production build with the dev Dockerfile.
- Build context: `src/PersonalBrandAssistant.Web`, Dockerfile: `Dockerfile.dev`
- Volume mounts:
  - `./src/PersonalBrandAssistant.Web:/app` -- Source code mount for hot reload.
  - `/app/node_modules` -- Anonymous volume to preserve container's node_modules and prevent host node_modules from overriding.
- Port mapping: `4200:4200` (Angular dev server default).

**db overrides:**
- No changes needed. Development uses the same PostgreSQL container.

### 7. .env.example

**File:** `C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\personal-brand-assistant\.env.example`

Template file that developers copy to `.env` and fill in with actual values. The `.env` file itself must be gitignored.

Contents:

```
# PostgreSQL Configuration
POSTGRES_USER=pba
POSTGRES_PASSWORD=<your-secure-password>
POSTGRES_DB=personal_brand_assistant

# API Configuration
API_KEY=<your-api-key>

# Environment (Development or Production)
ASPNETCORE_ENVIRONMENT=Development
```

Ensure `.env` is listed in the project's `.gitignore` to prevent secrets from being committed.

## Implementation Order

Within this section, implement files in this order:

1. `.env.example` -- Simple template, no dependencies.
2. `src/PersonalBrandAssistant.Web/nginx.conf` -- Static config file, no dependencies.
3. `src/PersonalBrandAssistant.Api/Dockerfile` -- Requires knowledge of the solution structure from sections 01-05.
4. `src/PersonalBrandAssistant.Web/Dockerfile` -- Requires the Angular project from section-07 to exist.
5. `src/PersonalBrandAssistant.Web/Dockerfile.dev` -- Same dependency as above.
6. `docker-compose.yml` -- References both Dockerfiles.
7. `docker-compose.override.yml` -- Extends the production compose file.

## Important Notes

- The API Dockerfile build context is the repository root (`.`), not the `src/PersonalBrandAssistant.Api/` directory. This is necessary because `dotnet restore` and `dotnet publish` need access to `Directory.Build.props` and all referenced projects (`Domain`, `Application`, `Infrastructure`).
- The Angular Dockerfile build context is `src/PersonalBrandAssistant.Web/` since Angular builds are self-contained within that directory.
- The Ubuntu Chiseled base image (`aspnet:10.0-noble-chiseled`) does not include a shell. This means you cannot `exec` into the container for debugging. If shell access is needed during development, the override file's `dotnet watch` approach uses the SDK image instead.
- Data Protection keys in the `dpkeys` volume are critical. If this volume is lost, all encrypted tokens (platform access/refresh tokens) become undecryptable. Document this in the `.env.example` or a README.
- Port 8080 is the default HTTP port for ASP.NET Core in .NET 8+ containerized apps. The compose file maps this to host port 5000.
- The `--poll 2000` flag on `ng serve` in `Dockerfile.dev` is necessary because filesystem events from Docker volume mounts (especially on Windows/macOS hosts) are unreliable. Polling ensures file changes are detected.