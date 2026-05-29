# Multi-Platform Publishing — Credential & Platform Setup

How to take each platform from `NotConfigured` to publishing. Config keys use the
deployed convention: nested config sections map to env vars with `__` (double
underscore), e.g. `Publishing:LinkedIn:ClientId` → `Publishing__LinkedIn__ClientId`.

On the Mac Mini deploy, secret **values** go in `~/personal-brand-assistant/.env`
(chmod 600) and are referenced from the `api` service in `docker-compose.override.yml`
as `Some__Key: ${SOME_KEY}`. Never put secret values directly in the tracked YAML.

Prerequisite already done: `Encryption__Key` (base64 32-byte) is set — required for
any platform that stores OAuth tokens/credentials (everything except Blog).

---

## Blog (matthewkruczek.ai) — NOT yet wired (prerequisites missing)

The `BlogConnector` publishes by writing `{RepoPath}/posts/{slug}.html`, then running
`git add` / `git commit` / `git push {RemoteName} {Branch}` — **inside the api container**.

Config section `BlogConnector`:
| Key | Required | Default |
|-----|----------|---------|
| `BlogConnector__RepoPath` | yes | — checkout of the website repo, path **inside the container** |
| `BlogConnector__TemplatePath` | yes | — HTML template file; `BlogFormatter` reads it and throws if missing |
| `BlogConnector__Author` | no | `Matt Kruczek` |
| `BlogConnector__RemoteName` | no | `origin` |
| `BlogConnector__Branch` | no | `main` |
| `BlogConnector__BaseUrl` | no | `https://matthewkruczek.ai` |

**Unmet prerequisites on the deploy (as of 2026-05-29):**
1. The matthewkruczek.ai repo is **not present** on the Mac Mini — must be cloned and
   bind-mounted into the api container (e.g. `~/matthewkruczek-ai:/app/blog-repo:rw`).
2. The api container has **no `git`** (Ubuntu 24.04 aspnet image, git not installed) —
   `BlogConnector` shells out to `git`, so the Dockerfile must `apt-get install -y git`
   and the image rebuilt.
3. **Push credentials** for the website repo must be available inside the container
   (deploy key mounted to `/root/.ssh`, or an HTTPS token in the remote URL / git creds).
4. A blog HTML **template file** must exist at `TemplatePath` (check the website repo or
   the `matt-kruczek-blog-writer` skill for the canonical template).

**Open architectural question:** the `matt-kruczek-blog-writer` skill already publishes
to matthewkruczek.ai via git from the host. Wiring PBA's in-container git push may
duplicate that. Decide whether PBA should publish directly or hand off to the skill
workflow before investing in the container plumbing.

---

## Medium — likely a dead end

Config section `Publishing:Medium`:
| Key | Default |
|-----|---------|
| `Publishing__Medium__Enabled` | `false` |
| `Publishing__Medium__DefaultPublishStatus` | `draft` |

Auth: an integration token, stored encrypted via `POST /api/platforms/medium/credentials`.

**Blocker:** Medium discontinued issuing new integration tokens in 2023. Only viable if
an existing token is still valid. Verify before relying on it.

---

## Substack — no official API

Config section `Publishing:Substack`:
| Key | Default |
|-----|---------|
| `Publishing__Substack__Enabled` | `false` |
| `Publishing__Substack__PublicationSlug` | `""` (your `<slug>.substack.com`) |
| `Publishing__Substack__DefaultAudience` | `everyone` |
| `Publishing__Substack__SendEmailOnPublish` | `true` |

Auth: session cookies, stored encrypted via `POST /api/platforms/substack/credentials`.
No official API — cookie-based auth is fragile and can break on Substack changes.

---

## LinkedIn — externally blocked

Config section `Publishing:LinkedIn` (all required when enabled):
| Key |
|-----|
| `Publishing__LinkedIn__Enabled` |
| `Publishing__LinkedIn__ClientId` |
| `Publishing__LinkedIn__ClientSecret` |
| `Publishing__LinkedIn__RedirectUri` |

Auth: OAuth 2.0. Connect flow: `GET /api/platforms/linkedin/authorize` →
LinkedIn consent → `GET /api/platforms/linkedin/callback` → token stored encrypted.

**Blocker:** Requires a LinkedIn app with the `w_member_social` (post) scope. The
Company Page is deactivated and posting needs Community Management API approval. Not
usable until that's resolved. (See `project_linkedin_blocker` memory.)

---

## Twitter / X — needs paid API tier

Config section `Publishing:Twitter`:
| Key | Required |
|-----|----------|
| `Publishing__Twitter__Enabled` | — |
| `Publishing__Twitter__ClientId` | yes |
| `Publishing__Twitter__ClientSecret` | yes |
| `Publishing__Twitter__RedirectUri` | yes |
| `Publishing__Twitter__ApiKey` | optional |
| `Publishing__Twitter__ApiSecret` | optional |

Auth: OAuth 2.0. Connect flow: `GET /api/platforms/twitter/authorize` → consent →
`GET /api/platforms/twitter/callback` → token stored encrypted.

**Cost:** Create an app in the X Developer Portal. Posting via API now requires a paid
tier (Basic+); the free tier is read-limited / write-restricted.

---

## How to apply config on the deploy

1. Add the secret value to `~/personal-brand-assistant/.env`, e.g.
   `TWITTER_CLIENT_ID=...`, `TWITTER_CLIENT_SECRET=...`
2. Reference it in `docker-compose.override.yml` under the `api` service `environment:`
   `Publishing__Twitter__ClientId: ${TWITTER_CLIENT_ID}`
   `Publishing__Twitter__Enabled: "true"`
3. `docker compose up -d api` to recreate with the new env.
4. Connect via the OAuth flow (LinkedIn/Twitter) or `POST /api/platforms/{platform}/credentials`
   (Medium/Substack) from the Settings → Platform Connections page.
5. Verify `GET /api/platforms/` shows the platform as connected.

## Config-key caveat
The deploy's `docker-compose.override.yml` has older `PlatformIntegrations__*` keys
(Reddit, LinkedIn). Those are a **different** config section and are NOT read by the new
publishing connectors, which use `Publishing:*` and `BlogConnector`. Use the keys above.
