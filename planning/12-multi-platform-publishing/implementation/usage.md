# Multi-Platform Publishing — Usage Guide

## Quick Start

### 1. Configure Platform Credentials

Set secrets via User Secrets (never commit to appsettings.json):

```bash
cd src/PBA.Api
dotnet user-secrets set "Encryption:Key" "<base64-encoded-32-byte-key>"
dotnet user-secrets set "Publishing:LinkedIn:ClientId" "<your-client-id>"
dotnet user-secrets set "Publishing:LinkedIn:ClientSecret" "<your-client-secret>"
dotnet user-secrets set "Publishing:LinkedIn:RedirectUri" "https://your-app/oauth/linkedin/callback"
dotnet user-secrets set "Publishing:Twitter:ClientId" "<your-client-id>"
dotnet user-secrets set "Publishing:Twitter:ClientSecret" "<your-client-secret>"
dotnet user-secrets set "Publishing:Twitter:RedirectUri" "https://your-app/oauth/twitter/callback"
```

Enable platforms in appsettings.json:
```json
{
  "Publishing": {
    "Medium": { "Enabled": true },
    "Substack": { "Enabled": true, "PublicationSlug": "your-slug" },
    "LinkedIn": { "Enabled": true },
    "Twitter": { "Enabled": true }
  }
}
```

### 2. Connect Platforms (Frontend)

1. Navigate to Settings > Platform Connections
2. Click "Connect" for each platform
3. Complete OAuth flow (LinkedIn, Twitter) or enter token (Medium, Substack)

### 3. Publish Content

1. Open a content item in the editor
2. Select target platforms using the checkboxes below the toolbar
3. Character/word counts update in real-time per platform
4. Click "Publish" to open the confirmation modal
5. Toggle platforms on/off in the modal, confirm
6. View per-platform status badges on the content list

## API Endpoints

### OAuth
| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/oauth/{platform}/authorize` | Get OAuth authorization URL |
| POST | `/api/oauth/{platform}/callback` | Handle OAuth callback |

### Platform Connections
| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/platforms` | List all platforms with connection status |
| DELETE | `/api/platforms/{platform}/disconnect` | Disconnect a platform |

### Publishing
| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/content/{id}/publish` | Publish to target platforms |
| GET | `/api/content/{id}/publish-status` | Get per-platform publish status |
| POST | `/api/content/{id}/retry/{platform}` | Retry failed platform publish |

## Architecture

### Keyed DI Resolution

Platform connectors and formatters are registered via keyed DI:
```csharp
// Resolved at runtime by platform enum value
var connector = serviceProvider.GetKeyedService<IPlatformConnector>(Platform.Medium);
var formatter = serviceProvider.GetKeyedService<IPlatformFormatter>(Platform.Medium);
```

### Content Transformation Pipeline

```
Content (Markdown) → IContentTransformer → IPlatformFormatter (keyed by platform) → Platform-specific format
```

- `BlogFormatter`: Markdown → HTML (Markdig)
- `MediumFormatter`: Markdown → Medium HTML subset
- `LinkedInFormatter`: Markdown → plain text (3000 char limit)
- `TwitterFormatter`: Markdown → plain text (280 char limit, thread splitting)
- `SubstackFormatter`: Markdown → Substack HTML

### Retry Strategy

Failed publishes are automatically retried with exponential backoff:
- Retry 1: 5 minutes
- Retry 2: 30 minutes
- Retry 3: 2 hours (max)

Individual platforms can be manually retried via the content list UI (retry button on failed badges).

### Security

- Platform tokens are encrypted at rest using AES-256 (`ITokenEncryptor`)
- OAuth flows use PKCE for Twitter and state validation for LinkedIn
- Tokens are refreshed automatically when expiring (10-minute window for Twitter, 5-minute for LinkedIn)

## Platform Support

| Platform | Connector | Auth | Features |
|----------|-----------|------|----------|
| Blog | BlogConnector | None (local git) | Markdown → HTML, git push |
| Medium | MediumConnector | Integration token | HTML publish, draft/public |
| LinkedIn | LinkedInConnector | OAuth 2.0 | Text posts, articles, images |
| Twitter | TwitterConnector | OAuth 2.0 + PKCE | Tweets, threads, media |
| Substack | SubstackConnector | Session cookie | Newsletter drafts |

## Running Tests

```bash
# All .NET tests (598 tests)
dotnet test

# Publishing DI tests only
dotnet test tests/PBA.Infrastructure.Tests --filter "PublishingDependencyTests"

# Angular tests (397 tests)
cd src/PersonalBrandAssistant.Web && ng test --watch=false --browsers=ChromeHeadless
```
