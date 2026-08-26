# LinkedIn Automated Posting — Setup

PBA publishes content to LinkedIn headlessly through its API. LinkedIn posts **as the
authenticated member** (`w_member_social` scope), so a **one-time OAuth consent** by the
account owner is required. After that, posting is fully automated — the stored token
auto-refreshes.

## 1. LinkedIn Developer app

Find (or create) the app at <https://www.linkedin.com/developers/apps>, signed in as the
posting account.

- **Auth** tab → note the **Client ID** and **Primary Client Secret** (regenerate the secret
  if it was never saved).
- **Products** tab → enable both:
  - **Sign In with LinkedIn using OpenID Connect** → scopes `openid profile`
  - **Share on LinkedIn** → scope `w_member_social` — **required to post** (this is the
    make-or-break; a login-only app won't have it).
- **Auth → Authorized redirect URLs** → add exactly the callback below (must match the base
  URL PBA runs on):

  ```
  http://localhost:5001/api/auth/linkedin/callback
  ```

## 2. Store secrets in the DevSecrets vault (NOT `.env`)

Secrets live only in the Windows DevSecrets vault — never in `.env` or the repo. Store them once:

```powershell
Import-Module C:\Users\kruz7\.devsecrets\DevSecrets.psm1
Set-DevSecret -Name LINKEDIN_CLIENT_ID     -Value '<client id>'
Set-DevSecret -Name LINKEDIN_CLIENT_SECRET -Value '<client secret>'
Set-DevSecret -Name EXTERNAL_API_KEY       -Value '<a long random string; guards the external publish route>'
```

`docker-compose*.yml` maps `Publishing__LinkedIn__{ClientId,ClientSecret,RedirectUri}` from the
environment and sets `Publishing__LinkedIn__Enabled=true`. `LINKEDIN_REDIRECT_URI` is a URL (not a
secret) and is set by the launch script below.

## 3. Run + one-time consent

`scripts/dev-up.ps1` pulls the secrets from the vault, injects them as env vars for the run
(nothing persisted to disk), and starts the stack:

```powershell
pwsh scripts/dev-up.ps1
```

Open <http://localhost:5001/api/auth/linkedin/authorize> in a browser and approve. PBA
exchanges the code and stores the encrypted access + refresh tokens in the
`PlatformCredentials` table; the connector auto-refreshes them within a 5-minute expiry window.

## 4. Publish

Content flows through PBA's **create → approve → publish** model (draft copy with the
`matt-kruczek-linkedin-writer` skill, then approve). To publish an already-approved LinkedIn
item **headlessly**:

```
POST http://localhost:5001/api/external/content/{contentId}/publish
Header: X-Api-Key: <EXTERNAL_API_KEY>
Body (optional): { "targetPlatforms": ["LinkedIn"] }
```

### Publish path (for reference)

`src/PBA.Api/Endpoints/ExternalEndpoints.cs` (X-Api-Key guarded)
→ MediatR `PublishContent.Command`
→ `ContentPublisher` (resolves the keyed `IPlatformConnector`)
→ `LinkedInConnector.PublishAsync` → `POST https://api.linkedin.com/rest/posts`.

The internal UI uses the same command via `POST /api/content/{id}/publish` (unauthenticated,
local-only). The `/api/external` route above is the guarded surface for server-to-server callers.

> **Not exposed by design:** ad-hoc "raw text → post". Content must exist and be approved first.
> If one-shot text publishing is ever needed, add a `PublishAdHoc` command that builds a
> transient `PlatformPublishRequest` — this decouples the request from the `Content` entity.
