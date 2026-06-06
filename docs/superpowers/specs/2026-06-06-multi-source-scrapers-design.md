# Multi-Source Scrapers (Hacker News + GitHub) — Design

**Date:** 2026-06-06
**Status:** Approved (design), pending implementation plan
**Inspiration:** [Thysrael/Horizon](https://github.com/Thysrael/Horizon) scrapers (Python). Concepts reimplemented in .NET; no code copied.
**Scope:** Backend (`PBA.Domain`/`PBA.Application`/`PBA.Infrastructure`) + new IdeaSource enum values. No new frontend pages.

## 1. Goal

Add Hacker News and GitHub as idea sources alongside RSS, feeding the existing
Idea → dedup → AI scoring → clustering → daily digest → alerts pipeline. New-source ideas get scored
and can fire high-score alerts exactly like RSS ideas, with zero changes to that downstream pipeline.

Out of scope (and why): Reddit (the u/MCKRUZ account is banned — dead on arrival), Twitter/X (paid API
tier + fragile auth), Horizon's Telegram/OpenBB/OSSInsight/DuckDuckGo sources (low value here).

## 2. Approach

Generalize ingestion behind one keyed interface instead of a parallel pipeline.

- `ISourceScraper`, registered with keyed DI by `IdeaSourceType` (the pattern `IPlatformConnector`
  already uses: `AddKeyedScoped<…>(Platform.X)`).
- RSS becomes one scraper (`RssScraper`, wrapping the existing `IRssFeedReader`) — no behavior change.
- `RssPollingService` is renamed/generalized to `SourcePollingService`, which dispatches per source
  by type and runs the SAME dedup + Idea-creation + failure-tracking logic that exists today.

*Rejected:* a separate `ScraperPollingService` for only HN/GitHub — duplicates the dedup, failure
auto-disable, and Idea-creation logic. Generalizing is more maintainable.

## 3. Components

| Component | Project | Responsibility |
|---|---|---|
| `ScrapedItem` (record) | `PBA.Application/Common/Interfaces` | Neutral ingestion DTO: `Title, Url, Description?, ThumbnailUrl?, PublishedAt` |
| `ISourceScraper` | `PBA.Application/Common/Interfaces` | `Task<IReadOnlyList<ScrapedItem>> FetchAsync(IdeaSource source, DateTimeOffset since, CancellationToken ct)` |
| `RssScraper` | `PBA.Infrastructure/Services/Scrapers` | Wraps `IRssFeedReader`, maps `RssFeedItem`→`ScrapedItem`. Keyed `RSS`. **Ignores `since`** (reads the whole feed, relies on dedup) to preserve exact current RSS behavior. |
| `HackerNewsScraper` | `PBA.Infrastructure/Services/Scrapers` | Firebase API, top stories ≥ MinScore + top comments. Keyed `HackerNews`. |
| `GitHubScraper` | `PBA.Infrastructure/Services/Scrapers` | Repo releases + user events from `source.ApiUrl`. Keyed `GitHub`. |
| `HackerNewsOptions` | `PBA.Infrastructure/Configuration` | `MinScore=100`, `FetchTopStories=30`, `TopComments=5` |
| `GitHubScraperOptions` | `PBA.Infrastructure/Configuration` | `Token` (optional; bound from `GITHUB_TOKEN` env / config), `MaxEventsPerSource=30` |
| `SourcePollingService` | `PBA.Infrastructure/Services` | Generalized poller; dispatches to keyed scrapers, dedups, creates Ideas |
| `IdeaSourceType` (+2) | `PBA.Domain/Enums` | Add `HackerNews`, `GitHub` |

## 4. Source modeling

- **Hacker News:** one `IdeaSource` row, `Type = HackerNews`, no URL needed. `MinScore`/`FetchTopStories`
  come from `HackerNewsOptions` (global). An HN idea = one qualifying story; up to `TopComments` top
  comments are folded into the idea `Description` for content-angle context. `Url` = the story's target
  URL (or the HN discussion URL when it is a self/Ask post). Dedup key = `Generate(url, title)`.
- **GitHub:** each watched repo/user = one `IdeaSource` row, `Type = GitHub`, with `ApiUrl` encoding the
  target: `github:repo:owner/name` (→ latest releases) or `github:user:username` (→ public push/PR/release
  events). One idea per release or notable event. `Url` = the release/event HTML URL. Dedup key =
  `Generate(url, title)`; for events lacking a stable URL, fall back to `Generate(nativeEventId, title)`.

## 5. Data flow

`SourcePollingService` runs on the existing poll interval:
1. Load all enabled `IdeaSource` rows (any type).
2. For each: resolve `ISourceScraper` via `IKeyedServiceProvider` keyed by `source.Type`. If no scraper
   is registered for that type, skip + log (defensive).
3. `since = source.LastSuccessAt ?? now - LookbackHours`. Call `scraper.FetchAsync(source, since, ct)`.
4. Dedup each `ScrapedItem` against existing `DeduplicationKey`s, create `Idea` rows
   (`Status=New`, `IdeaSourceId`, `SourceName`, `Category` from the source), exactly as today.
5. Update `LastPolledAt`/`LastSuccessAt`/failure counters (existing auto-disable on N failures).
6. `SaveChangesAsync`. Downstream scoring/clustering/digest/alerts is untouched.

## 6. Configuration

`appsettings.json`:
```jsonc
"HackerNews": { "MinScore": 100, "FetchTopStories": 30, "TopComments": 5 },
"GitHubScraper": { "MaxEventsPerSource": 30 }   // Token from GITHUB_TOKEN env / user-secrets
```
GitHub token is a secret: `GITHUB_TOKEN` via env (compose) / user-secrets (dev) / Key Vault (prod).
Optional — without it, GitHub API allows 60 req/hr (enough for a few sources); with it, 5000/hr.

## 7. Security

- `GITHUB_TOKEN` is a secret — never hardcoded; bound from env/secrets. Used only as a Bearer/token header.
- Fixed external hosts (`hacker-news.firebaseio.com`, `api.github.com`) → no SSRF surface from user input.
  `GitHubScraper` parses `ApiUrl` only into `owner/name`/`username` path segments against `api.github.com`;
  it never uses `ApiUrl` as a raw request URL, so a malicious `ApiUrl` cannot redirect requests elsewhere.
- Treat all scraped text as untrusted (it already flows into the LLM scoring layer, which is prompt-injection
  hardened per existing radar design). No raw HTML execution; descriptions stored as text.
- Respect rate limits via `FetchTopStories` / `MaxEventsPerSource` caps.

## 8. Testing

- `HackerNewsScraper`: mocked `HttpClient` returning Firebase `topstories`/`item` JSON — asserts score
  filter, `since` filter, top-comment folding, `ScrapedItem` mapping, graceful HTTP-error handling (returns []).
- `GitHubScraper`: mocked `HttpClient` — repo-releases path and user-events path; `ApiUrl` parsing
  (`github:repo:owner/name`, `github:user:username`, and malformed → []); auth header present only when
  token configured; `since` filter; mapping.
- `RssScraper`: maps `RssFeedItem`→`ScrapedItem` correctly (wraps existing reader, mocked).
- `SourcePollingService`: in-memory DB + mock keyed scrapers — dispatches the right scraper per source
  type, dedups across sources, creates Ideas, increments failures + auto-disables at the threshold,
  skips types with no registered scraper. Migrate the existing `RssPollingService` tests to the new name.
- ≥80% coverage on new code.

## 9. Frontend touchpoints (minimal)

The Manage Sources form's Type dropdown is enum-driven, so `HackerNews`/`GitHub` surface automatically.
Add light per-type hint text (GitHub needs `github:repo:owner/name` or `github:user:username` in the
ApiUrl field; HN needs no URL). Full per-type form UX is a follow-up, not part of this spec.

## 10. Migration / rollout

- No DB schema change (new enum values are stored as existing int/string column; confirm the
  `IdeaSourceType` persistence is string or that new ints append at the end — `HackerNews`/`GitHub`
  appended after `API` keep existing values stable).
- Ships inert: no HN/GitHub source rows exist until created via the API/UI, so nothing scrapes until
  the user adds a source. RSS continues unchanged.
