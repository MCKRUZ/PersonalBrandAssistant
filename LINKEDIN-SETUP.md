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

## 2. Configuration (`.env` — git-ignored, never commit)

Add to `personal-brand-assistant/.env`:

```
LINKEDIN_CLIENT_ID=<client id>
LINKEDIN_CLIENT_SECRET=<client secret>
LINKEDIN_REDIRECT_URI=http://localhost:5001/api/auth/linkedin/callback
EXTERNAL_API_KEY=<a long random string; guards the external publish route>
```

`docker-compose*.yml` maps these to `Publishing__LinkedIn__{ClientId,ClientSecret,RedirectUri}`
and sets `Publishing__LinkedIn__Enabled=true`, so a Docker run picks them up automatically.

## 3. Run + one-time consent

```
docker compose up -d          # from the PBA repo root
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
