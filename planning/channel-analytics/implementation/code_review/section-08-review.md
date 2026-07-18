# Section 08 — Review

Documentation-only deliverable (`planning/channel-analytics/runbook.md`). No code, no tests, no build.
Reviewed by verifying every runbook fact **against the actual source** (stronger than the plan text):

| Runbook claim | Verified against |
|---|---|
| Callback `/api/auth/{platform}/callback`, authorize `?purpose=analytics` | `OAuthEndpoints.cs` (`MapGroup("/api/auth")`, `/{platform}/callback`, `TryParsePurpose`) |
| Lowercase platform tokens (`youtube`/`instagram`/`tiktok`) | `Enum.TryParse<Platform>(platform, ignoreCase: true)` + FE `authUrl()` |
| Secret keys `Publishing:{YouTube,Instagram,TikTok}:{ClientId,ClientSecret,RedirectUri}` (+ YouTube `ApiKey`) | `YouTubeOAuthOptions.cs`, `InstagramOAuthOptions.cs`, `TikTokOAuthOptions.cs` (`SectionName = "Publishing:…"`) |
| Gates `ChannelAnalytics:{YouTube,Instagram,TikTok}Enabled` | `ChannelAnalyticsOptions.cs` (`SectionName = "ChannelAnalytics"`, `*Enabled` props) |
| Mac Mini host `matthews-mac-mini.tail2800e3.ts.net` | project memory `reference_macmini_access.md` |
| `Encryption:Key` reused for token encryption | plan §; left untouched |

Research-sourced platform facts (scopes, the 7-day Google "Testing" refresh-token trap, Instagram
own-account = no App Review, TikTok `user.info.stats` migration + Display-API data ceiling) reproduced
exactly from the plan's verified facts (`claude-research.md` Part B).

All 7 required subsections present: Overview/order, YouTube, Instagram, TikTok, Secrets, Verification,
Troubleshooting. No findings.
