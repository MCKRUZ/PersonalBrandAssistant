# Research — Feed Ranking Redesign

Two streams: (1) PBA codebase, (2) web best-practices. Findings that change the design are flagged **[DESIGN IMPACT]**.

---

## Part 1 — Codebase (current state)

### Database — **PostgreSQL / Npgsql** (was "TBD" in CLAUDE.md; now confirmed)
- Connection: `src/PBA.Api/appsettings.json:3` (`Host=localhost;Port=5432;Database=pba`).
- Context: `src/PBA.Infrastructure/Data/ApplicationDbContext.cs:30-33` — `UseNpgsql()` via DataSourceBuilder, `EnableDynamicJson()` (JSONB used for Tags).
- DI: `src/PBA.Infrastructure/DependencyInjection.cs:26-33`. Migrations: `src/PBA.Infrastructure/Data/Migrations/` (latest `20260606004418_AddIdeaAlertedAt`).
- **[DESIGN IMPACT]** PostgreSQL is fixed → vector storage = **pgvector** (`Pgvector.EntityFrameworkCore`). No SQL-Server-2025 requirement.

### Idea entity — `src/PBA.Domain/Entities/Idea.cs`
Fields: Id(init), Title(req), Description?, Url?, SourceName(req), IdeaSourceId?, ThumbnailUrl?, Category?, Summary?, AIConnections?, Status, Tags(List<string>, jsonb), DetectedAt, DeduplicationKey(req), **Score(int?)**, **ScoreReason?**, **ScoredAt?**, DuplicateOfId?, ClusteredAt?, AlertedAt?.
- Config: `src/PBA.Infrastructure/Data/Configurations/IdeaConfiguration.cs`. Indexes on IdeaSourceId, DeduplicationKey(unique), ScoredAt, Score, DuplicateOfId, AlertedAt.
- Null score sorts to bottom via `i.Score ?? -1`.

### Radar services — `src/PBA.Infrastructure/Services/Radar/`
- **IdeaAnalyzer** (sealed, `IIdeaAnalyzer`): `AnalyzeAsync(title, description, url, sourceName, ct) → IdeaAnalysis?` (score 0-10, reason, summary, category, tags). Hardcoded system prompt. Calls `sidecar.SendPromptAsync(system, user, model, ct)`. Strips fences, clamps 0-10.
- **IdeaScoringService** (`BackgroundService`): every `IntervalMinutes`, fetches `BatchSize` unscored, throttles `ThrottleMs`. `BackfillEnabled` gates all-history vs since-start.
- **IdeaClusteringService** (`BackgroundService`): every 30 min, candidates Score≥MinScore(6), last LookbackHours(48), top MaxItemsPerSweep(40); marks DuplicateOfId.
- **IdeaClusterer** (`IIdeaClusterer`): `ClusterAsync(ClusterInput[] {Index,Title,Summary}) → groups of indices`.
- DI: `DependencyInjection.cs:78-88` — options bound, IIdeaAnalyzer/IIdeaClusterer scoped, two hosted services.

### ISidecarClient — `src/PBA.Application/Common/Interfaces/ISidecarClient.cs`
- `Task<string> SendPromptAsync(system, user, model?, ct)` and `IAsyncEnumerable<string> StreamPromptAsync(...)`. **No embeddings method today.**
- Impl: `OpenRouterClient`, `AddHttpClient<ISidecarClient, OpenRouterClient>()` (`DependencyInjection.cs:74`). Honors `model` param. `SidecarOptions`/`OpenRouterOptions` from appsettings.
- **[DESIGN IMPACT]** Add `EmbedAsync` here (see Part 2 Topic 1).

### ListIdeas — `src/PBA.Application/Features/Ideas/Queries/ListIdeas.cs`
- `Query` record: Page, PageSize, Status?, IdeaSourceId?, Category?, Tags?, DateFrom/To?, SearchText?, **SortBy="DetectedAt"**, SortDirection="desc", MinScore?, IncludeDuplicates=false. Returns `Result<PagedResult<IdeaDto>>`.
- `ApplySort` switch (lines 110-127): title/sourcename/category/status/score/_→detectedat. **This is where composite rank sort gets added.**
- `IdeaDto`: Id,Title,Description,Url,SourceName,Category,Summary,ThumbnailUrl,Status,Tags,DetectedAt,HasSavedDetails,Score,ScoreReason,IsDuplicate. **Extend with rank + per-pillar breakdown.**
- Endpoint: `src/PBA.Api/Endpoints/IdeaEndpoints.cs:17-40`, `[AsParameters] ListIdeasQueryParams`, `result.ToApiResult()`.

### Conventions
- `Result`/`Result<T>` in `src/PBA.Domain/Common/Result.cs` — factories Success/Fail/ValidationFailure/NotFound/etc.; `ResultFailureType` enum.
- Handlers inject `IAppDbContext`, implement `IRequestHandler<Q, Result<T>>`, `AsNoTracking()` for reads.
- FluentValidation via `ValidationBehavior<,>` open behavior + assembly scan (`src/PBA.Application/DependencyInjection.cs:14-18`).
- Options: `IdeaScoringOptions`/`ClusteringOptions` in `src/PBA.Infrastructure/Configuration/`, `const SectionName`, `services.Configure<T>(GetSection(...))`, consumed as `IOptions<T>.Value`. (Use `IOptionsMonitor<T>` for the new runtime-tunable bits.)

### Frontend — `src/PersonalBrandAssistant.Web/src/app/features/ideas/`
- **Store** `store/idea.store.ts` (NgRx signal store): state {ideas,totalCount,page,pageSize,filter,sort,**viewMode:'grid'|'list'**,selectedIdeaId,loading,error}; computed totalPages/hasNext/hasPrev; methods loadIdeas(rxMethod→ideaService.list), setFilter/setSort(reset page→loadIdeas), setPage, **toggleView**, saveIdea, dismissIdea, selectIdea.
- **Service** `core/services/idea.service.ts`: `list(filter,page,pageSize,sort)` builds HttpParams (sortBy/sortDirection, repeated tags). Also getById/create/save/dismiss/createContent/getConnections + source CRUD.
- **Component** `ideas.component.ts`: 3-col grid (240/1fr/280); search (300ms debounce), **sort dropdown {Newest, Highest score, Source}**, view-toggle; `@if viewMode()==='grid'` grid else list; PrimeNG paginator; ScoreDistribution + SmartSuggestions sidebars.
- **view-toggle** `components/view-toggle/view-toggle.component.ts`: two p-buttons (th-large / list) calling toggleView.
- **Routing**: `ideas.routes.ts` ('' → IdeasComponent, 'sources' → IdeaSourcesPage); lazy-loaded in `app.routes.ts`.
- **[DESIGN IMPACT]** Third view mode = extend `viewMode` union to add `'ranked'`, add `@else if` branch + toggle button. Brand Profile editor = new route in `ideas.routes.ts` (e.g. `'brand-profile'`) or a top-level settings route.

### Tests
- Backend xUnit: `tests/PBA.Application.Tests/...ListIdeasHandlerTests.cs` (InMemory DB `UseInMemoryDatabase(Guid)`, `CreateIdea()` helper, direct handler call). Radar: `tests/PBA.Infrastructure.Tests/Services/Radar/IdeaAnalyzerTests.cs` (Moq ISidecarClient `.Setup().ReturnsAsync()`, `Options.Create()`, `NullLogger<T>.Instance`).
  - **Note:** InMemory provider will NOT execute pgvector SQL — vector-similarity query tests need either a unit-level cosine helper tested in isolation, or a Postgres test container. Plan must address this.
- Frontend Jasmine/Karma: `idea.service.spec.ts` (TestBed, `provideHttpClient()`+`provideHttpClientTesting()`, `httpMock.expectOne()`, `req.flush()`).

---

## Part 2 — Web best-practices

### Topic 1 — Embeddings via OpenRouter **[DESIGN IMPACT — assumption overturned]**
- OpenRouter **now has a native embeddings endpoint**: `POST /api/v1/embeddings`, `GET /api/v1/embeddings/models`. OpenAI-compatible schema. (openrouter.ai/docs/api/reference/embeddings)
- Request: `{model, input: string|string[], encoding_format:"float"}` → `{data:[{index, embedding:[...]}], usage}`. `input` accepts arrays → batch 100–500 items/request, not 3,800 singles.
- **Recommendation:** extend `ISidecarClient` with `Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, string model, CancellationToken ct)`; POST to `/embeddings`, order by `index`. Stays inside the sidecar abstraction (honors the route-through-sidecar rule).
- **Model:** default `openai/text-embedding-3-small` (1,536-dim, L2-normalized → cosine==dot, supports Matryoshka `dimensions` shrink to 512). At this corpus size embedding cost is cents; optimize for simplicity/quality not per-token price. Don't self-host. Verify the slug via `/embeddings/models` (OpenRouter's embedding catalog is smaller than chat and shifts).
- **Caveat:** if a desired model isn't proxied, fallback is a direct provider call (a second client path — the one wrinkle).

### Topic 2 — Vector storage in EF Core **[DESIGN IMPACT]**
- **At ~3,800 rows, do NOT build a vector index.** Brute-force exact cosine is sub-ms; HNSW/ANN matters at 100K–1M+ rows and only returns approximate results. Premature here.
- **Recommendation: pgvector, exact scan, no index.** `Pgvector.EntityFrameworkCore` (0.3.0, EF Core 9/10). `[Column(TypeName="vector(1536)")] public Vector? Embedding`, `modelBuilder.HasPostgresExtension("vector")`, `o.UseVector()` on UseNpgsql. Query: `.OrderBy(i => i.Embedding!.CosineDistance(query)).Take(n)` — distance computed in-DB (don't ship 23 MB/query). `dotnet add package Pgvector.EntityFrameworkCore`.
- Fallback (DB-agnostic, worse): store `float[]`, in-memory cosine — only if pgvector blocked.

### Topic 3 — Recency decay **[DESIGN IMPACT — add a floor]**
- Exponential half-life: `decay(age)=exp(-λ·age)`, `λ=ln2/half_life`. 7-day → 1.0 / 0.5 / 0.25 / 0.125 at 0/7/14/21d. Matches the agreed design.
- **Pitfall:** pure multiplicative decay permanently buries evergreen high-quality items (60-day, q=0.95 → ~0.009). 
- **Recommendation:** keep multiplicative 7-day half-life **+ a decay floor (~0.05–0.1)**: `decay = max(exp(-λ·age), floor)`. Make `halfLifeDays` and `floor` config (IOptionsMonitor) — will be tuned against real output. (HN gravity / Reddit log-decay considered and rejected: HN is power-law not half-life; Reddit is for vote streams, we have quality scores.)

### Topic 4 — LLM per-pillar rubric **[DESIGN IMPACT — calibration additions]**
- Pattern: G-Eval form-filling — decompose into per-pillar criteria, score each independently in ONE structured response, **rationale before score** per pillar.
- **Anchor every level with concrete rubric text** (what 0 / 0.25 / 0.5 / 0.75 / 1.0 look like per pillar) — without exemplars judges collapse to central scores.
- **Cross-corpus calibration (main risk of independent scoring):** include **fixed few-shot anchor examples** (2–4 exemplars w/ known scores, identical every call, fixed order); use **discrete anchored scale {0,.25,.5,.75,1}** not free 0–1; **low temperature (0–0.2)**; **JSON-schema constrained decoding** (gemini-2.5-flash supports structured output — don't regex free text).
- Confirms the design: query-time weighted sum over stored sub-scores is the standard multi-criteria pattern; re-score is the expensive part, weighting is free.

---

## Net changes to fold into the plan
1. DB is PostgreSQL → **pgvector, exact cosine, no index**.
2. **`ISidecarClient.EmbedAsync`** via OpenRouter `/embeddings`, model `openai/text-embedding-3-small`, batched.
3. Recency = multiplicative 7-day half-life **+ decay floor ~0.05–0.1**, both config.
4. New analyzer emits per-pillar sub-scores via **JSON-schema structured output, fixed few-shot anchors, per-level rubric, low temp**.
5. New runtime-tunable config → **IOptionsMonitor**; brand profile + weights in DB (version-stamped) per design.
6. **Test gap:** InMemory EF provider can't run pgvector SQL — plan a cosine unit test + Postgres-testcontainer (or sliced) integration test.
