# Channel Analytics — Interview

Decisions captured across the scoping + interview rounds. All confirmed by the user.

## Q1. Depth of social analytics
**A:** Everything possible, all platforms — pull every metric each API exposes, accepting Meta/TikTok
app-review delays where they apply.

## Q2. Trend charts over time
**A:** Yes — build a PBA-side daily snapshot store + scheduled poller so we accumulate history and render
trends. (The platform APIs don't give deep history; we accumulate our own.)

## Q3. App-review handling
**A:** Include a step-by-step console runbook (Google Cloud, Meta, TikTok) for the user to action the
critical path in parallel with the build. NOTE from research: Instagram does NOT require App Review for the
user's OWN account (Standard Access via "Instagram API with Instagram Login"); TikTok scopes still need app
approval; Google needs the consent screen in "In Production" (not Testing) or refresh tokens die in 7 days.

## Q4. Plan review mode
**A:** Use a Claude Opus subagent for the plan review (no external Gemini/OpenAI configured).

## Q5. Web research topics
**A:** All four — YouTube Data v3 + Analytics v2; Instagram Graph Insights; TikTok Display + Business API;
polling rate limits & token refresh. (Findings in `claude-research.md`.)

## Q6. Analytics token storage model
**A:** Add a `Purpose` discriminator (Publishing | Analytics) to `PlatformCredential` with a composite unique
index `(Platform, Purpose, IsActive)`. Analytics and publishing tokens for the same platform coexist without
collision; reuse `TokenEncryptor` + existing `RefreshTokenAsync`. (Migration required.)

## Q7. OAuth provider code structure
**A:** Refactor the hardcoded `switch(Platform)` in `OAuthService` into an `IOAuthProvider` map (one class per
provider — LinkedIn, Twitter, YouTube, Instagram, TikTok — via keyed DI). Retire the switch. Existing
LinkedIn/Twitter behavior must be preserved exactly (regression-tested).

## Q8. Snapshot scope
**A:** Account-level snapshot + per-video snapshots for the most recent N videos per platform (N configurable,
default 50). Bounds row growth while enabling per-post trends and TikTok per-video deltas (the only TikTok
trend source).

## Q9. OAuth client credentials source
**A:** Reuse the existing ai-video-producer OAuth apps. The runbook adds PBA's Mac Mini redirect URIs and the
analytics scopes to those apps (rather than creating new PBA-specific apps). Client id/secret fetched at
runtime from secrets, never embedded.

## Q10. Analytics page organization
**A:** Overview landing (cross-platform: total audience across all channels, combined engagement, per-platform
sparklines side by side) + a tab per source (Website, YouTube, Instagram, TikTok) for the deep view.

## Q11. Per-platform view layout
**A:** Mirror the existing Website layout — KPI row up top (followers, views, engagement rate), trend line
charts built from daily snapshots, and a table of recent/top posts with per-post metrics.

## Defaulted (stated, not asked)
- Snapshot retention: keep full history (no cap); revisit only if table size becomes a concern.
- Recent-video window N default = 50, configurable via `ChannelAnalytics` options.
- Connection status surfaced per platform (connected / reconnect-required / not-connected), reusing the
  existing `/api/platforms` + OAuth `status` pattern.
- Whole feature buildable + testable against mocked API responses; lights up per-platform as tokens/approvals
  land (gated-but-code-complete, like the blog pipeline).
