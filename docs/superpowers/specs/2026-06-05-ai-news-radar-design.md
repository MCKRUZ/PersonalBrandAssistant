# AI News Radar for the Idea Bank (Horizon-inspired)

**Date:** 2026-06-05
**Status:** Design approved (pending spec review)
**Branch:** v2-rebuild
**Source inspiration:** [Thysrael/Horizon](https://github.com/Thysrael/Horizon) — an AI news radar (Python). Concepts reimplemented in .NET; no code copied.

---

## 1. Problem & Goal

The Idea Bank ingests RSS feeds into `Idea` entities, but does nothing intelligent with them:
no importance scoring, no AI summary, dedup only by exact URL+title hash, no daily digest.
Tellingly, the `Idea` entity **already has** `Summary`, `Category`, `Tags`, and `AIConnections`
fields that RSS ingest never populates — the schema was built for a brain that was never wired up.

Horizon is exactly that brain: it fetches, scores, semantically dedupes, enriches, and renders a
ranked daily briefing. We are porting four of its concepts into our pipeline:

1. **AI scoring + ranking** — every idea gets a 0-10 *brand content-worthiness* score.
2. **AI summary + tags on ingest** — populate the existing empty `Summary`/`Category`/`Tags` fields.
3. **Semantic dedup / clustering** — merge ideas covering the same real-world event across feeds.
4. **Daily AI digest brief** — a ranked executive brief, surfaced in-app and pushed externally.

Explicitly **out of scope**: Horizon's multi-source scrapers (HN/Reddit/GitHub/Twitter) and its
DuckDuckGo web-search enrichment. RSS stays the only source for this work.

## 2. Constraints & Reuse

Built entirely on existing infrastructure — no new architectural patterns:

| Concern | Existing asset reused |
|---|---|
| LLM gateway | `ISidecarClient` -> `OpenRouterClient` (OpenRouter, OpenAI-compatible) |
| Background work | `BackgroundService` pattern, as in `RssPollingService`, `AiConnectionsService` |
| Persistence | `ApplicationDbContext` (EF Core), `IAppDbContext` |
| App layer | MediatR + `Result<T>` (`PBA.Domain.Common`), FluentValidation |
| Frontend | Angular 19 standalone + NgRx signals, `features/ideas/`, `features/feed/` |

Fixed prior decisions honored:
- All LLM calls route through `ISidecarClient` (never direct Anthropic/OpenRouter from app code).
- Generated brand-facing copy uses the humanizer rules and contains **no em-dashes**.
- Clean Architecture, type-organized layers, files under ~400 lines, 80% coverage on new code.

## 3. Architecture

A four-stage AI-radar layer. Each stage is a focused background service that **orchestrates only**;
all prompt construction and JSON parsing lives in separately testable analyzer classes.

```
RSS poll ──> Idea (ScoredAt == null)
                 │
   Stage 1  IdeaScoringService      (every N min, throttled batches)
            └─ IIdeaAnalyzer        → 1 LLM call/idea → Score, ScoreReason, Summary, Category, Tags
               (draining unscored ideas IS the 3,831-item backfill, gated by a toggle)
                 │
   Stage 2  IdeaClusteringService   (periodic, batched over recent high-scored ideas)
            └─ IIdeaClusterer        → 1 LLM call → groups same-event ideas → sets DuplicateOfId
                 │
   Stage 3  DigestService           (1×/day at configured time)
            └─ IDigestWriter         → 1 LLM call over top-N primaries → Digest + DigestItems
                                      → creates FeedItem alert (in-app surfacing)
                 │
   Stage 4  IDigestDeliverySender[] (Phase 2) → WebhookDigestSender + EmailDigestSender
```

### Design decisions (with rejected alternatives)

**D1. Analyzer classes, not inline service logic.**
`IIdeaAnalyzer`, `IIdeaClusterer`, `IDigestWriter` (interfaces in `PBA.Application/Common/Interfaces`,
implementations in `PBA.Infrastructure`). Background services just loop, throttle, and persist.
*Rejected:* inline prompt/parse in the service (as `AiConnectionsService` does today) — untestable,
violates the 80% coverage rule for the most logic-heavy part of the feature.

**D2. Cheap model by default for the radar — without degrading drafting.**
`ISidecarClient.SendPromptAsync` gains an optional `string? model = null` parameter; `null` preserves
today's configured default model, so the **drafting engine keeps running gemini-2.5-pro unchanged**.
The radar's own options (`IdeaScoringOptions.Model`, `ClusteringOptions.Model`) **default to a cheap,
fast model** (gemini-flash / haiku tier). The digest opts back up to the standard model for quality.
> **FLAGGED:** This deliberately does NOT change the global sidecar default to cheap, because that
> would silently degrade the drafting engine (which intentionally uses gemini-2.5-pro). The user
> asked for "cheap by default for the sidecar"; this satisfies the intent (cheap scoring with no
> manual config) while protecting drafting. Veto here if a true global cheap default is wanted.

**D3. `DuplicateOfId` FK for clustering.**
`Idea.DuplicateOfId` (nullable self-FK): `null` = primary/standalone; set = merged into the primary.
Queries default to `DuplicateOfId == null`. *Rejected:* `ClusterId` + `IsClusterPrimary` pair —
two fields to keep consistent, more invariants to enforce.

**D4. `Digest` + `DigestItem` child table.**
Relational over a JSON blob: queryable history, joinable to `Idea`, matches clean-architecture norms.
*Rejected:* serialized JSON column (as `AIConnections` uses) — opaque, hard to query/report on.

## 4. Data Model

### Changes to `Idea` (single EF migration)
| Field | Type | Meaning |
|---|---|---|
| `Score` | `int?` | 0-10 brand content-worthiness; null = unscored |
| `ScoreReason` | `string?` | brief LLM justification for the score |
| `ScoredAt` | `DateTimeOffset?` | when scored; **null drives the backfill/scoring queue** |
| `DuplicateOfId` | `Guid?` | self-FK; null = primary, set = merged into that idea |
| `ClusteredAt` | `DateTimeOffset?` | when clustering last evaluated this idea |

Existing `Summary`, `Category`, `Tags` are populated by Stage 1 (no schema change).

### New entities
**`Digest`** — `Id`, `Date` (date-only), `Title`, `Intro` (brand-voice, humanized, no em-dash),
`ItemCount`, `CreatedAt`. One row per day.

**`DigestItem`** — `Id`, `DigestId` (FK), `IdeaId` (FK), `Rank`, `Score`, `WhyItMatters`.
Ordered child rows of a `Digest`.

New `DbSet<Digest>` and `DbSet<DigestItem>` on `ApplicationDbContext`.

## 5. Prompts (the real IP — brand-specific)

All prompts return strict JSON; analyzers parse defensively and degrade gracefully on parse failure
(log + skip, mirroring Horizon's "fall back silently" behavior so one bad item never stalls a batch).

**Scoring / summarize (`IIdeaAnalyzer`)** — cheap model.
System prompt encodes Matt's brand: enterprise AI, agentic development, AI thought leadership for a
developer-to-executive audience. Rubric is **content-worthiness, not generic newsworthiness**:
- 9-10: a strong, ownable thought-leadership angle Matt could publish a great LinkedIn/blog/Twitter piece on
- 7-8: clearly relevant, postable with a good take
- 5-6: tangentially relevant, needs an angle
- 3-4: weak fit
- 0-2: off-brand / not worth covering

Input: title, source, description, url. Output JSON: `{score, reason, summary, category, tags[]}`.

**Clustering (`IIdeaClusterer`)** — cheap model.
Horizon's same-event grouping. Input: indexed list of recent high-scored ideas (title + summary).
Output JSON: `{groups: [[primaryIdx, dupIdx, ...]]}`. Conservative — keep separate when unsure.

**Digest (`IDigestWriter`)** — standard model, humanized, no em-dashes.
Input: top-N primaries `{title, summary, score, url}`. Output: an intro paragraph plus a
per-item "why it matters" line. This is Matt-facing brand copy, so it runs the humanizer rules.

## 6. API

- `GET /api/digests` — list digests (paginated, newest first).
- `GET /api/digests/{id}` — digest detail with ranked `DigestItem`s (joined to idea title/url).
- Extend `ListIdeas` query: `sortBy=score`, `minScore`, and default `DuplicateOfId == null`
  (exclude merged duplicates unless explicitly requested).

All endpoints follow the existing minimal-API + MediatR + `Result<T>` pattern; no endpoint auth
(consistent with current v2 convention).

## 7. Frontend (`features/ideas` + new `features/digest`)

- `idea-card.component`: score badge (color-scaled) + `ScoreReason` tooltip; render `Summary`/`Tags`.
- `idea-grid` / `idea-list`: "sort by score" control; duplicates collapsed under their primary.
- `idea-filter-sidebar`: min-score filter, "unscored only" toggle.
- **New `features/digest`**: a **Daily Brief** page rendering the latest `Digest` (intro + ranked items
  linking back to ideas), with a date picker for history. New `digest.service.ts` + `digest.store.ts`.
- Feed: no work — the digest `FeedItem` alert renders through the existing feed automatically.

## 8. Configuration & Cost

New options classes (Options pattern, `IOptionsMonitor<T>`):
- `IdeaScoringOptions` — `IntervalMinutes`, `BatchSize`, `ThrottleMs`, `Model` (cheap default), `BackfillEnabled` (toggle).
- `ClusteringOptions` — `IntervalMinutes`, `MinScore`, `LookbackHours`, `Model` (cheap default).
- `DigestOptions` — `RunAtLocalTime`, `TopN`, `LookbackHours`.
- `DigestDeliveryOptions` (Phase 2) — `WebhookUrl`, SMTP host/port/user/pass, recipient.

**Cost is real and flagged:** backfilling 3,831 existing ideas = 3,831 one-shot LLM calls. Mitigations:
cheap model (D2), throttled batches, and `BackfillEnabled=false` by default. Start new-items-only,
watch the first batch's token spend, then flip the toggle to drain the backlog. Ongoing per-poll cost
is one cheap call per genuinely new idea.

**Secrets (Phase 2):** webhook URL and SMTP credentials via user-secrets (dev) / Key Vault (prod).
Never in code. Email via MailKit (well-tested library, not hand-rolled SMTP). Validate the webhook
URL host to avoid SSRF.

## 9. Phasing

**Phase 1 — in-app radar (independently shippable):**
data model + Stage 1 scoring/summary + Stage 2 clustering + Stage 3 digest (entity + FeedItem alert)
+ API + frontend score/sort/digest view. Delivers the full value loop in-app.

**Phase 2 — external push:** `WebhookDigestSender` (Slack/Discord) + `EmailDigestSender` (MailKit/SMTP)
+ `DigestDeliveryOptions` + secrets. Bolts onto Stage 3 via `IDigestDeliverySender[]`.

## 10. Testing

- **Analyzers** (`IIdeaAnalyzer`/`IIdeaClusterer`/`IDigestWriter`): unit tests with a mocked
  `ISidecarClient` returning canned JSON, including malformed-JSON graceful-degradation cases.
- **Background services**: in-memory DB; assert state transitions (unscored -> scored, dup -> merged,
  digest created + FeedItem emitted); assert throttle/batch bounds and backfill toggle gating.
- **Handlers/queries** (`ListIdeas` score sort/filter, digest queries): `WebApplicationFactory` +
  in-memory DB, per the existing test conventions.
- **Frontend**: component specs for score badge / sort / digest page; `HttpTestingController` for
  `digest.service`. 80% coverage minimum on all new code.

## 11. Risks & Unknowns

- **LLM JSON reliability** — cheap models drift from strict JSON. Mitigation: defensive parsing,
  skip-on-failure, low temperature, explicit "JSON only" instruction.
- **Scoring rubric tuning** — first-pass scores may not match Matt's judgment. The rubric is config-
  adjacent (the prompt); expect one or two tuning iterations after seeing real scores.
- **Backfill token spend** — bounded by the toggle + cheap model; verify on a small batch first.
- **Clustering false-merges** — conservative prompt + `DuplicateOfId` is reversible (un-merge by
  nulling the FK), so mistakes are recoverable.
