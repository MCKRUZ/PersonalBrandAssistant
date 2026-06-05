# AI News Radar — Phase 2: External Delivery (Email + Discord)

**Date:** 2026-06-05
**Status:** Approved (design), pending implementation plan
**Builds on:** [Phase 1 design](2026-06-05-ai-news-radar-design.md) — scoring → clustering → daily digest

## 1. Goal

Push radar output out of the app to where Matt actually sees it, via two triggers:

1. **Daily digest** — the existing Stage 3 ranked brief, pushed to email + Discord once per day.
2. **Instant high-score alerts** — an immediate push when a single idea scores at/above a
   threshold, so breaking content opportunities surface fast.

Channels: **email (generic SMTP via MailKit)** and **Discord (incoming webhook)**. No Slack.

## 2. Decisions (with rejected alternatives)

**D1. Unified delivery payload, not per-trigger methods.**
One interface, `IDigestDeliverySender.SendAsync(DeliveryNotification, ct)`. Both the daily digest
(8 items) and an instant alert (1 item) collapse to the same shape: a titled message with N ranked
items and links. Senders stay dumb and medium-specific.
*Rejected:* separate `SendDigest`/`SendAlert` methods — forces every sender to maintain two
formatters for what is structurally one message.

**D2. Instant alerts live in their own `HighScoreAlertService`, not inside `IdeaScoringService`.**
Every radar stage in this codebase is its own `BackgroundService`. A dedicated alert service keeps
scoring focused, makes cap/dedupe state independent, and is testable in isolation.
*Rejected:* inlining alert dispatch into `IdeaScoringService.ScoreBatchAsync` — couples two
responsibilities and bloats the most cost-sensitive loop.

**D3. `Idea.AlertedAt` (nullable) for both dedupe and the daily cap.**
`null` = never alerted (dedupe guard); count of `AlertedAt >= start-of-day` = today's alert usage
for the cap. One timestamp serves both needs.
*Rejected:* a separate `SentAlerts` table — an extra entity for state two timestamps already express.

**D4. Shared global daily cap.**
The `MaxPerDay` cap counts alerts, not channel sends. When an alert fires it goes to every enabled
channel and counts once. *Rejected:* per-channel caps — extra state, no real value for a personal feed.

**D5. Delivery failures never break persistence.**
The digest/alert is already saved before fan-out. Each sender call is wrapped; a failure is logged
and returns `Result.Fail`, never throws. One dead channel must not block the other or corrupt state.

**D6. SSRF defense by host allowlist, not a generic filter.**
Discord webhooks always live on a known host set. `DiscordDigestSender` validates the webhook host
against `{discord.com, discordapp.com, canary.discord.com, ptb.discord.com}` and refuses anything
else. Narrow, simple, sufficient — no general SSRF library needed for a fixed-host integration.

## 3. Architecture

```
Stage 3  DigestService ─────────┐
                                  ├──► IEnumerable<IDigestDeliverySender>
Stage 5  HighScoreAlertService ──┘            ├─► EmailDigestSender   (MailKit SMTP, HTML body)
                                               └─► DiscordDigestSender (HttpClient → webhook embed)
```

### Components

| Component | Project | Responsibility |
|---|---|---|
| `IDigestDeliverySender` | `PBA.Application/Common/Interfaces` | `Task<Result> SendAsync(DeliveryNotification, ct)` |
| `DeliveryNotification` / `DeliveryItem` | `PBA.Application/Common/Models` | Transport-neutral payload |
| `EmailDigestSender` | `PBA.Infrastructure/Services/Radar/Delivery` | MailKit SMTP; pure `BuildMessage` + thin send |
| `DiscordDigestSender` | `PBA.Infrastructure/Services/Radar/Delivery` | HttpClient POST of a Discord embed; host allowlist |
| `HighScoreAlertService` | `PBA.Infrastructure/Services/Radar` | `BackgroundService` sweep → alert fan-out |
| `DigestDeliveryOptions` | `PBA.Infrastructure/Configuration` | Email / Discord / Alerts config |
| `Idea.AlertedAt` | `PBA.Domain/Entities` | Dedupe + daily-cap state |

### Data flow

**Digest push** — after `DigestService.GenerateDigestAsync` persists the `Digest` (`DigestService.cs:108`):
build a `DeliveryNotification` (Kind=Digest, Title/Intro from the digest, Items from `DigestItem` joined
to `Idea` for headline+url), fan out to all enabled senders, log per-sender result.

**Instant alert** — `HighScoreAlertService` sweeps every `Alerts.SweepIntervalMinutes`:
1. Query `Ideas` where `Score >= ScoreThreshold && AlertedAt == null && DuplicateOfId == null`,
   ordered by `Score` desc, `ScoredAt` desc.
2. Compute remaining budget = `MaxPerDay − count(Ideas where AlertedAt >= localStartOfDay)`.
3. Take up to remaining-budget ideas. For each: build a single-item `DeliveryNotification`
   (Kind=Alert), fan out, set `AlertedAt = now`.
4. `SaveChangesAsync` once.

## 4. Configuration

Section `DigestDelivery` in `appsettings.json` (structure + safe defaults only; secrets elsewhere):

```jsonc
"DigestDelivery": {
  "Email": {
    "Enabled": false,
    "SmtpHost": "", "SmtpPort": 587, "UseStartTls": true,
    "SmtpUser": "", "SmtpPassword": "",
    "FromAddress": "", "FromName": "PBA Radar", "ToAddress": ""
  },
  "Discord": { "Enabled": false, "WebhookUrl": "" },
  "Alerts": { "Enabled": false, "ScoreThreshold": 9, "MaxPerDay": 5, "SweepIntervalMinutes": 5 }
}
```

**Secrets** (`SmtpPassword`, `WebhookUrl`) via `dotnet user-secrets` (dev) / Azure Key Vault (prod) —
never in `appsettings.json`. All three blocks default `Enabled=false`: Phase 2 ships dormant and is
switched on per-channel once creds are provisioned.

## 5. Dependencies

- **MailKit** (NuGet) — well-tested SMTP; no hand-rolled SMTP. Run `dotnet list package --vulnerable`
  before adding, per security rules.
- **Discord** — reuses `IHttpClientFactory` (already registered), no new package.

## 6. Security

- SMTP password + webhook URL are secrets (§4). Validate `ToAddress`/`FromAddress` are well-formed.
- Discord webhook host allowlist (D6) blocks SSRF to internal hosts via a tampered URL.
- StartTls on by default for SMTP. No credentials logged.

## 7. Testing

- `EmailDigestSender.BuildMessage` — renders subject + HTML body from a notification (digest and
  single-item alert variants); links present; no secrets in output.
- `DiscordDigestSender` — `HttpClient` with mock handler: asserts embed payload shape; **host
  allowlist rejects a non-Discord webhook URL** (returns `Result.Fail`, no HTTP call).
- `HighScoreAlertService` — in-memory DB + mock senders: threshold filter, `DuplicateOfId` exclusion,
  dedupe (sets `AlertedAt`, never re-alerts), shared cap (6th idea in a day does not send), fan-out to
  all enabled senders.
- `DigestService` — fan-out occurs after persistence; a throwing/failing sender does not break the
  digest write or block the other sender.
- Coverage ≥ 80% on new code.

## 8. Out of scope

- Slack delivery.
- Per-channel caps (D4).
- A UI for editing delivery config (config-file driven for now).
- Retry/queue for failed sends (log-and-drop is acceptable for a personal feed; revisit if needed).
