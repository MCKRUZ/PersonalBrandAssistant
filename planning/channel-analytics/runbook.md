# Channel Analytics — Console Runbook

Step-by-step external setup for YouTube, Instagram, and TikTok analytics. Do this **in parallel with
the build** — the code goes green against mocks without any of it. Each platform "lights up" only once
its console steps here are done **and** its per-platform gate is flipped
(`ChannelAnalytics:{Platform}Enabled=true`).

You are **reusing the existing ai-video-producer OAuth apps** — you add PBA's redirect URIs and the
analytics scopes to those apps, you do **not** create new apps.

**Reference facts used throughout:**
- PBA Mac Mini host: `matthews-mac-mini.tail2800e3.ts.net` (Tailscale). Callback base is `/api/auth`.
- Callback URL per platform: `https://matthews-mac-mini.tail2800e3.ts.net/api/auth/{platform}/callback`
  where `{platform}` is the lowercase token `youtube` | `instagram` | `tiktok`.
- Consent link the UI opens: `/api/auth/{platform}/authorize?purpose=analytics`.
- Secrets live in `dotnet user-secrets` (dev) and Docker-compose env vars (prod). Never in code or
  committed config. `Encryption:Key` already exists and is reused for token encryption — don't touch it.

---

## 1. Overview & order of operations

Run all three in parallel. They differ only in how long the provider takes to approve:

| Platform  | Approval needed?                          | Practical lead time |
|-----------|-------------------------------------------|---------------------|
| YouTube   | **No review** (read-only scopes, own acct) | Minutes — do first  |
| Instagram | **No App Review** for your own account     | Minutes             |
| TikTok    | **Review required** for scope additions    | Days — submit early |

Recommended sequence:
1. **Submit the TikTok review first** (longest wait), then continue with the rest while it's pending.
2. Do **YouTube** next — fastest to fully working.
3. Do **Instagram**.
4. As each provider is ready, flip that platform's gate and run the per-platform verification in §6.

For every platform the shape is identical: **add redirect URI → add analytics scopes → store secrets →
flip gate → Connect in the UI → verify.**

---

## 2. Google Cloud / YouTube

In the **Google Cloud project** backing the existing ai-video-producer app:

1. **Enable APIs** (APIs & Services → Library):
   - **YouTube Data API v3**
   - **YouTube Analytics API v2**
2. **OAuth client → Authorized redirect URIs** — add:
   ```
   https://matthews-mac-mini.tail2800e3.ts.net/api/auth/youtube/callback
   ```
   (Confirm this matches the host/path PBA actually serves before saving.)
3. **OAuth scopes** — ensure the app requests:
   - `https://www.googleapis.com/auth/yt-analytics.readonly`
   - `https://www.googleapis.com/auth/youtube.readonly`
4. **API key** — create or confirm an API key (APIs & Services → Credentials) for public Data API reads.
5. **⚠ CRITICAL — OAuth consent screen Publishing status = "In Production".**
   - If left in **"Testing"**, Google refresh tokens **expire after 7 days** — this is the classic
     "worked for a week, then broke" failure. Move it to **In Production**.
   - For these **read-only** scopes at **single-user** scale, publishing does **not** trigger Google
     verification. The status simply must not be "Testing".
6. **Store secrets** (keys — values never committed):
   - `Publishing:YouTube:ClientId`
   - `Publishing:YouTube:ClientSecret`
   - `Publishing:YouTube:ApiKey`
   - `Publishing:YouTube:RedirectUri` = the callback URL from step 2.

---

## 3. Meta / Instagram

1. **Confirm the account type** — the Instagram account must be **Business** or **Creator**. Personal
   accounts expose **no insights** and will return empty analytics.
2. **Use "Instagram API with Instagram Login"** (`graph.instagram.com`). This is the current model and
   **removes the old Facebook-Page requirement** — you do not need a linked Facebook Page.
3. **Scopes** — add:
   - `instagram_business_basic`
   - `instagram_business_manage_insights`
4. **Redirect URI** — add:
   ```
   https://matthews-mac-mini.tail2800e3.ts.net/api/auth/instagram/callback
   ```
5. **App Review is NOT required for your own account.** Selecting **"My app is only for a business I
   own or manage"** grants **Standard Access** with no review. App Review + Business Verification are
   only for **Advanced Access** (serving *other* businesses). **Do not wait on a review you don't need.**
6. **Store secrets**:
   - `Publishing:Instagram:ClientId`
   - `Publishing:Instagram:ClientSecret`
   - `Publishing:Instagram:RedirectUri` = the callback URL from step 4.

---

## 4. TikTok

In the **existing TikTok developer app** (currently holds `video.publish` for the other project):

1. **Add products / scopes**:
   - `user.info.stats`
   - `video.list`
2. **⚠ Scope migration note** — account stats (`follower_count`, etc.) **moved out of `user.info.basic`
   into the dedicated `user.info.stats` scope**. It must be requested **explicitly** and the account
   **re-authorized** — an existing token without it will return null stats.
3. **Redirect URI** — add:
   ```
   https://matthews-mac-mini.tail2800e3.ts.net/api/auth/tiktok/callback
   ```
4. **Submit for review** — TikTok reviews scope additions before granting **production** access.
   Meanwhile the app works in **sandbox** for your own registered test account, so you can test the
   flow before approval lands.
5. **⚠ Data ceiling — set expectations.** The Display API gives only **cumulative counts**: followers,
   and per-video views / likes / comments / shares. **Reach, demographics, profile views, and retention
   are NOT available** via any self-service creator API. Those TikTok *trends* in PBA come from PBA's
   **own daily snapshot deltas**, not from TikTok.
6. **Store secrets**:
   - `Publishing:TikTok:ClientId`
   - `Publishing:TikTok:ClientSecret`
   - `Publishing:TikTok:RedirectUri` = the callback URL from step 3.

---

## 5. Secrets placement

Same config keys, two stores depending on environment:

- **Dev** — `dotnet user-secrets` on `src/PBA.Api`:
  ```
  dotnet user-secrets set "Publishing:YouTube:ClientId"      "<value>" --project src/PBA.Api
  dotnet user-secrets set "Publishing:YouTube:ClientSecret"  "<value>" --project src/PBA.Api
  dotnet user-secrets set "Publishing:YouTube:ApiKey"        "<value>" --project src/PBA.Api
  dotnet user-secrets set "Publishing:YouTube:RedirectUri"   "https://matthews-mac-mini.tail2800e3.ts.net/api/auth/youtube/callback" --project src/PBA.Api
  # …repeat for Publishing:Instagram:* and Publishing:TikTok:*
  ```
- **Prod (Mac Mini)** — environment variables in the Docker-compose env (double underscore = `:`):
  ```
  Publishing__YouTube__ClientId=…
  Publishing__YouTube__ClientSecret=…
  Publishing__YouTube__ApiKey=…
  Publishing__YouTube__RedirectUri=https://matthews-mac-mini.tail2800e3.ts.net/api/auth/youtube/callback
  Publishing__Instagram__ClientId=…   Publishing__Instagram__ClientSecret=…   Publishing__Instagram__RedirectUri=…
  Publishing__TikTok__ClientId=…      Publishing__TikTok__ClientSecret=…      Publishing__TikTok__RedirectUri=…
  ```
  Never commit these. `Encryption:Key` already exists in the prod env and is reused to encrypt stored
  tokens — leave it as-is.

**Per-platform gates** (config, safe to commit as `false`; flip to `true` when a platform is ready):
```
ChannelAnalytics:YouTubeEnabled     (env: ChannelAnalytics__YouTubeEnabled)
ChannelAnalytics:InstagramEnabled   (env: ChannelAnalytics__InstagramEnabled)
ChannelAnalytics:TikTokEnabled      (env: ChannelAnalytics__TikTokEnabled)
```

---

## 6. Verification (per platform)

Run this for each platform once its console steps are done:

1. **Flip the gate** — set `ChannelAnalytics:{Platform}Enabled=true` (dev config or prod env), restart
   the API.
2. **Connect** — in the PBA UI, open the platform's analytics tab and click **Connect** (or **Reconnect**).
   You're redirected to the provider's consent screen; **grant the analytics scopes**.
3. **Status flips to Connected** — after the callback you land back in PBA and the channel status shows
   **Connected**.
4. **First snapshot** — wait for the next daily poll (or trigger a run) and confirm a first
   `ChannelMetricSnapshot` row appears for the platform.
5. **KPIs render** — the tab shows KPI cards immediately; **trend charts fill in over subsequent days**
   as more daily snapshots accrue (a single snapshot has no delta yet).

---

## 7. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| **YouTube worked ~7 days then broke** (refresh token dead) | OAuth consent screen still in **"Testing"** | Move consent screen to **In Production** (§2.5), then reconnect. |
| **TikTok stats are null** (followers/likes zero or missing) | `user.info.stats` scope not added, or account not **re-authorized** after adding it | Add the scope (§4.1), then **Reconnect** so a new token includes it. |
| **Instagram insights empty** | Account is **Personal**, not Business/Creator — or a metric needs **≥100 followers** | Convert the account to Business/Creator; some insights only populate past the follower threshold. |
| **Status shows "Reconnect Required"** | Token was revoked or expired at the provider | Click **Reconnect** and grant scopes again. |
| **Connect returns "Invalid purpose"** | Consent link missing `?purpose=analytics` | Ensure the UI links to `/api/auth/{platform}/authorize?purpose=analytics`. |
| **Callback fails / redirect mismatch** | Redirect URI in the provider console ≠ `Publishing:{Platform}:RedirectUri` | Make the two **byte-for-byte identical** (host, scheme, path). |
| **Reach / demographics / retention absent on TikTok** | Not a bug — **API ceiling** (§4.5) | Expected. Those come from PBA's own snapshot deltas over time, never from TikTok. |
