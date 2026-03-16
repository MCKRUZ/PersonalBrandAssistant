# Platform Integrations — Usage Guide

## Quick Start

The platform integration layer enables publishing content to Twitter, LinkedIn, Instagram, and YouTube through a unified API. All services are registered automatically via `DependencyInjection.AddInfrastructure()`.

### Prerequisites

1. **Database migration:** Run EF Core migrations to create `ContentPlatformStatuses`, `OAuthStates`, and `PlatformRateLimitEntries` tables.
2. **User secrets:** Configure OAuth credentials per platform:
   ```bash
   dotnet user-secrets set "PlatformIntegrations:Twitter:ClientId" "your-client-id"
   dotnet user-secrets set "PlatformIntegrations:Twitter:ClientSecret" "your-client-secret"
   dotnet user-secrets set "PlatformIntegrations:LinkedIn:ClientId" "your-client-id"
   dotnet user-secrets set "PlatformIntegrations:LinkedIn:ClientSecret" "your-client-secret"
   dotnet user-secrets set "PlatformIntegrations:Instagram:AppId" "your-app-id"
   dotnet user-secrets set "PlatformIntegrations:Instagram:AppSecret" "your-app-secret"
   dotnet user-secrets set "MediaStorage:SigningKey" "your-hmac-signing-key"
   ```

### API Endpoints

All endpoints require `X-Api-Key` header.

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/platforms` | List all connected platforms |
| GET | `/api/platforms/{type}/status` | Get platform connection status |
| GET | `/api/platforms/{type}/auth-url` | Generate OAuth authorization URL |
| POST | `/api/platforms/{type}/callback` | Exchange OAuth code for tokens |
| POST | `/api/platforms/{type}/revoke` | Disconnect platform |
| POST | `/api/platforms/{type}/test-post` | Publish a test post (requires `Confirm: true`) |
| GET | `/api/platforms/{type}/engagement/{postId}` | Get post engagement metrics |
| GET | `/api/media/{id}` | Serve media files with HMAC-signed URLs |

### OAuth Flow

```
1. GET /api/platforms/twitter/auth-url
   → Returns { authUrl, state, codeChallenge }

2. User authorizes in browser, redirected to callback URL with code + state

3. POST /api/platforms/twitter/callback
   Body: { code, codeVerifier, state }
   → Tokens stored encrypted, platform marked connected
```

### Publishing Content

The `IPublishingPipeline` is the main entry point:

```csharp
var result = await publishingPipeline.PublishAsync(
    contentId: contentGuid,
    platforms: [PlatformType.Twitter, PlatformType.LinkedIn],
    ct: cancellationToken);
```

The pipeline:
1. Validates platform connections and rate limits
2. Formats content per platform (character limits, hashtags, media)
3. Publishes via platform adapters
4. Tracks status in `ContentPlatformStatus` entities
5. Handles async processing (Instagram containers, YouTube uploads)

### Background Processors

Three hosted services run automatically:

- **TokenRefreshProcessor** (every 5 min) — Proactively refreshes OAuth tokens before expiry
- **PlatformHealthMonitor** (every 15 min) — Validates connectivity and scope integrity
- **PublishCompletionPoller** (every 30 sec) — Polls async upload status for Instagram/YouTube

## Architecture

```
API Endpoints (PlatformEndpoints.cs)
  ├── IOAuthManager → OAuthManager (strategy pattern per platform)
  ├── IPublishingPipeline → PublishingPipeline
  │     ├── IRateLimiter → DatabaseRateLimiter (sliding window + caching)
  │     ├── IPlatformContentFormatter[] (per-platform formatting)
  │     ├── ISocialPlatform[] (per-platform adapters)
  │     └── IMediaStorage → LocalMediaStorage (HMAC-signed URLs)
  └── INotificationService (user notifications)
```

## Key Interfaces

| Interface | Implementation | Purpose |
|-----------|---------------|---------|
| `ISocialPlatform` | Twitter/LinkedIn/Instagram/YouTubePlatformAdapter | Platform API communication |
| `IOAuthManager` | OAuthManager | OAuth token lifecycle |
| `IRateLimiter` | DatabaseRateLimiter | Per-platform rate limiting |
| `IMediaStorage` | LocalMediaStorage | File storage with signed URLs |
| `IPlatformContentFormatter` | 4 platform-specific formatters | Content adaptation |
| `IPublishingPipeline` | PublishingPipeline | Orchestrates publishing flow |

## Configuration

Non-secret config in `appsettings.json`:

```json
{
  "PlatformIntegrations": {
    "Twitter": { "CallbackUrl": "...", "BaseUrl": "https://api.x.com/2" },
    "LinkedIn": { "CallbackUrl": "...", "ApiVersion": "202603", "BaseUrl": "https://api.linkedin.com/rest" },
    "Instagram": { "CallbackUrl": "..." },
    "YouTube": { "CallbackUrl": "...", "DailyQuotaLimit": 10000 }
  },
  "MediaStorage": { "BasePath": "./media" }
}
```

## Test Suite

628 tests total across 3 projects:
- **Domain Tests (138):** Entity validation, enum coverage, value objects
- **Application Tests (106):** Handler logic, pipeline behavior, validator rules
- **Infrastructure Tests (384):** Adapters, formatters, OAuth, rate limiter, background processors, DI registration, API endpoints
