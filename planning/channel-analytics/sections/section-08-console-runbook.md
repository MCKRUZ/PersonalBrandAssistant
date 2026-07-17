# section-08-console-runbook

## Goal

Produce a standalone, step-by-step console runbook the user actions **in parallel** with the build. This is the critical-path external work: OAuth apps, scopes, and (where required) app review. The code goes green against mocks without any of this; each platform "lights up" once its steps here are done and the per-platform gate is flipped.

Deliverable: **`planning/channel-analytics/runbook.md`** — pure documentation, no code, no tests. This section's job is to write that file.

## Dependencies

- **None for authoring.** The runbook references scopes/redirect URIs that sections 03/05 consume, but writing it depends on nothing. It is parallelizable with everything.
- Reuses the existing **ai-video-producer OAuth apps** (user decision): the runbook adds PBA's redirect URIs + analytics scopes to those apps rather than creating new apps.

## Facts the runbook must encode (verified, 2026)

These come from the research pass (`claude-research.md` Part B). Get them exactly right — the runbook is the user's only guide.

### Google Cloud (YouTube)
- Enable **YouTube Data API v3** and **YouTube Analytics API v2** in the Google Cloud project backing the existing app.
- On the OAuth client, add PBA's **Mac Mini redirect URI** (the Tailscale callback URL PBA serves, e.g. `https://matthews-mac-mini.tail2800e3.ts.net/api/auth/youtube/callback` — confirm the exact host/path PBA uses).
- Add scopes **`yt-analytics.readonly`** and **`youtube.readonly`**.
- Create / confirm an **API key** for public Data API reads.
- **CRITICAL: move the OAuth consent screen to "In Production" (Publishing status).** If left in "Testing", Google refresh tokens **expire after 7 days** — the classic "worked for a week then broke" failure. For these read-only scopes at single-user scale, publishing does not require Google verification, but the status must not be "Testing".
- Secrets go to: `Publishing:YouTube:ClientId`, `Publishing:YouTube:ClientSecret`, `Publishing:YouTube:ApiKey`, `Publishing:YouTube:RedirectUri` (user-secrets in dev / env in prod; never committed).

### Meta (Instagram)
- Confirm the Instagram account is a **Business or Creator** account (personal accounts have no insights).
- Use the **"Instagram API with Instagram Login"** model (`graph.instagram.com`) — this removes the old Facebook-Page requirement.
- Add scopes **`instagram_business_basic`** + **`instagram_business_manage_insights`**.
- Add PBA's redirect URI (`.../api/auth/instagram/callback`).
- **App Review is NOT required for the user's own account:** "My app is only for a business I own or manage" = **Standard Access**, no review. Review + Business Verification are only for tools serving *other* businesses (Advanced Access). State this clearly so the user doesn't wait on a review that isn't needed.
- Secrets: `Publishing:Instagram:ClientId/ClientSecret/RedirectUri`.

### TikTok
- In the existing TikTok developer app, add the products/scopes **`user.info.stats`** + **`video.list`** (the app currently has `video.publish` for the other project).
- Note the **scope migration**: account stats (`follower_count` etc.) moved out of `user.info.basic` into the dedicated `user.info.stats` scope — it must be requested explicitly and re-authorized.
- Add PBA's redirect URI (`.../api/auth/tiktok/callback`).
- **Submit for review**: TikTok reviews app scope additions before granting production access. Meanwhile the app works in sandbox for the developer's own test account.
- Be explicit about the **ceiling**: the Display API gives only cumulative counts (followers, per-video views/likes/comments/shares). Reach, demographics, profile views, retention are **not** available via any self-service creator API — those TikTok trends come from PBA's own daily snapshot deltas, not from TikTok.
- Secrets: `Publishing:TikTok:ClientId/ClientSecret/RedirectUri`.

### Where secrets live
- Dev: `dotnet user-secrets` on `src/PBA.Api`.
- Prod (Mac Mini): environment variables in the Docker compose env (per repo security rules — never in code or committed config). `Encryption:Key` already exists and is reused for token encryption.

### End-to-end verification (per platform)
1. Complete the console steps + set the per-platform gate `ChannelAnalytics:{Platform}Enabled=true`.
2. In the PBA UI, open the platform's analytics tab and click **Connect** (redirects to the provider's consent screen; grant the analytics scopes).
3. Confirm the connection status flips to **Connected**.
4. Wait for the next daily poll (or trigger a run) and confirm a first `ChannelMetricSnapshot` appears.
5. Confirm KPIs render; trend charts populate over subsequent days as snapshots accrue.

## Runbook structure to write (`runbook.md`)

Author `planning/channel-analytics/runbook.md` with these sections, each as an explicit numbered checklist the user can follow without prior context:

1. **Overview & order of operations** — do these in parallel with the build; Google is fastest (no review), TikTok needs review, Instagram needs neither for the own-account case.
2. **Google Cloud / YouTube** — enable APIs, redirect URI, scopes, API key, **consent screen In Production** (call out the 7-day trap), where secrets go.
3. **Meta / Instagram** — Business/Creator confirmation, Instagram-Login app, scopes, redirect URI, "no App Review for own account", where secrets go.
4. **TikTok** — add scopes, scope-migration note, redirect URI, submit for review, the data-ceiling caveat, where secrets go.
5. **Secrets placement** — the exact config keys and the dev vs prod stores.
6. **Verification** — the per-platform end-to-end checklist above.
7. **Troubleshooting** — the common failure modes: Google refresh token dies in 7 days (consent screen still in Testing); TikTok stats null (scope not added / not re-authorized); Instagram insights empty (account not Business/Creator, or <100 followers for some metrics); token revoked → status shows Reconnect Required.

## Definition of done

- `planning/channel-analytics/runbook.md` exists and covers all seven sections above with concrete, copy-followable steps.
- Every scope, endpoint, and the 7-day consent-screen trap are stated exactly as in the research.
- No code, no tests — this is a documentation deliverable.
