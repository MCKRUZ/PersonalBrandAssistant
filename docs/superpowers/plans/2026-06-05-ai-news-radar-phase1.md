# AI News Radar (Phase 1: In-App Radar) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an AI-radar layer to the Idea Bank that scores each idea for brand content-worthiness, populates AI summary/tags, semantically clusters duplicate stories, and produces a ranked daily digest surfaced in-app.

**Architecture:** Three focused `BackgroundService`s (scoring, clustering, digest) orchestrate testable analyzer classes (`IIdeaAnalyzer`, `IIdeaClusterer`, `IDigestWriter`) that call the existing `ISidecarClient`/OpenRouter gateway. New radar fields on `Idea`, new `Digest`/`DigestItem` tables, extended `ListIdeas` query, new digest API + Angular Daily Brief page. The radar runs on a cheap model by default; the drafting engine's model is untouched.

**Tech Stack:** .NET 10, C#, EF Core (PostgreSQL/Npgsql), MediatR + `Result<T>`, xUnit + in-memory DB, Angular 19 standalone + NgRx signals, Jasmine/Karma.

**Spec:** `docs/superpowers/specs/2026-06-05-ai-news-radar-design.md`

**Phase 2 (separate plan):** external push (Slack/Discord webhook + email via MailKit). Not in this plan.

---

## Conventions for every task

- Tests-first. Backend tests live under `tests/PBA.Application.Tests`, `tests/PBA.Infrastructure.Tests`, `tests/PBA.Api.Tests`.
- Build: `dotnet build`. Backend test run examples use `dotnet test --filter`.
- Frontend tests: `cd src/PersonalBrandAssistant.Web && npm test -- --watch=false --browsers=ChromeHeadless`.
- Commit after each task with the shown message. Stage specific files only.
- Immutability/records for DTOs, `Result<T>` for handler failures, no em-dashes in any Matt-facing generated copy.

---

## Task 1: Add optional `model` parameter to the sidecar gateway (the cost lever)

**Files:**
- Modify: `src/PBA.Application/Common/Interfaces/ISidecarClient.cs`
- Modify: `src/PBA.Infrastructure/Services/OpenRouterClient.cs:29-37`
- Test: `tests/PBA.Infrastructure.Tests/Services/OpenRouterClientTests.cs` (create if absent)

- [ ] **Step 1: Write the failing test**

Create/extend `tests/PBA.Infrastructure.Tests/Services/OpenRouterClientTests.cs`. Use a stub `HttpMessageHandler` that captures the request body.

```csharp
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services;

namespace PBA.Infrastructure.Tests.Services;

public class OpenRouterClientTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? CapturedBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            CapturedBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"content":"ok"}}]}""")
            };
        }
    }

    [Fact]
    public async Task SendPromptAsync_WithModelOverride_UsesOverrideModel()
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler);
        var options = Options.Create(new OpenRouterOptions
        {
            ApiKey = "test-key",
            Model = "google/gemini-2.5-pro"
        });
        var client = new OpenRouterClient(http, options, NullLogger<OpenRouterClient>.Instance);

        await client.SendPromptAsync("sys", "user", model: "google/gemini-2.5-flash");

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        Assert.Equal("google/gemini-2.5-flash", doc.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task SendPromptAsync_WithoutModelOverride_UsesConfiguredModel()
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler);
        var options = Options.Create(new OpenRouterOptions
        {
            ApiKey = "test-key",
            Model = "google/gemini-2.5-pro"
        });
        var client = new OpenRouterClient(http, options, NullLogger<OpenRouterClient>.Instance);

        await client.SendPromptAsync("sys", "user");

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        Assert.Equal("google/gemini-2.5-pro", doc.RootElement.GetProperty("model").GetString());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~OpenRouterClientTests`
Expected: FAIL to compile (no `model` parameter).

- [ ] **Step 3: Implement**

In `ISidecarClient.cs`, change the signature:

```csharp
Task<string> SendPromptAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default);
```

In `OpenRouterClient.cs`, update the method signature and the payload model line:

```csharp
public async Task<string> SendPromptAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(_options.ApiKey))
        throw new InvalidOperationException("OpenRouter API key is not configured.");

    var payload = new ChatRequest(
        model ?? _options.Model,
        [new ChatMessage("system", systemPrompt), new ChatMessage("user", userPrompt)],
        _options.MaxTokens);
```

Also update the empty-response message to use the effective model:

```csharp
    if (string.IsNullOrWhiteSpace(content))
        throw new InvalidOperationException($"OpenRouter returned an empty response from {model ?? _options.Model}.");
```

And in `StreamPromptAsync`, update the inner call to pass `model: null`:

```csharp
        var result = await SendPromptAsync(systemPrompt, userPrompt, model: null, ct);
```

Then fix the existing caller in `AiConnectionsService.cs:76`:

```csharp
        var response = await _sidecarClient.SendPromptAsync(systemPrompt, userPrompt, model: null, ct);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~OpenRouterClientTests`
Expected: PASS. Then `dotnet build` to confirm no broken callers.

- [ ] **Step 5: Commit**

```bash
git add src/PBA.Application/Common/Interfaces/ISidecarClient.cs src/PBA.Infrastructure/Services/OpenRouterClient.cs src/PBA.Infrastructure/Services/AiConnectionsService.cs tests/PBA.Infrastructure.Tests/Services/OpenRouterClientTests.cs
git commit -m "feat: add optional model override to ISidecarClient"
```

---

## Task 2: Add AI-radar fields to the `Idea` entity + migration

**Files:**
- Modify: `src/PBA.Domain/Entities/Idea.cs`
- Modify: `src/PBA.Infrastructure/Data/Configurations/IdeaConfiguration.cs`
- Create (generated): `src/PBA.Infrastructure/Data/Migrations/<timestamp>_AddIdeaRadarFields.cs`

- [ ] **Step 1: Add fields to the entity**

Add to `Idea.cs` after `DeduplicationKey`:

```csharp
    public int? Score { get; set; }
    public string? ScoreReason { get; set; }
    public DateTimeOffset? ScoredAt { get; set; }
    public Guid? DuplicateOfId { get; set; }
    public DateTimeOffset? ClusteredAt { get; set; }
```

- [ ] **Step 2: Configure columns + indexes**

In `IdeaConfiguration.cs`, add inside `Configure` before the closing brace:

```csharp
        builder.Property(i => i.ScoreReason).HasColumnType("text");

        builder.HasIndex(i => i.ScoredAt);
        builder.HasIndex(i => i.Score);
        builder.HasIndex(i => i.DuplicateOfId);

        builder.HasOne<Idea>()
            .WithMany()
            .HasForeignKey(i => i.DuplicateOfId)
            .OnDelete(DeleteBehavior.SetNull);
```

- [ ] **Step 3: Generate the migration**

Run:
```bash
dotnet ef migrations add AddIdeaRadarFields --project src/PBA.Infrastructure --startup-project src/PBA.Api --output-dir Data/Migrations
```
Expected: a new migration file appears under `src/PBA.Infrastructure/Data/Migrations/`. Open it and confirm it adds `Score`, `ScoreReason`, `ScoredAt`, `DuplicateOfId`, `ClusteredAt` and the indexes/FK. No data loss operations.

- [ ] **Step 4: Verify build**

Run: `dotnet build`
Expected: success.

- [ ] **Step 5: Commit**

```bash
git add src/PBA.Domain/Entities/Idea.cs src/PBA.Infrastructure/Data/Configurations/IdeaConfiguration.cs src/PBA.Infrastructure/Data/Migrations/
git commit -m "feat: add AI radar fields to Idea entity"
```

---

## Task 3: `IdeaScoringOptions` configuration

**Files:**
- Create: `src/PBA.Infrastructure/Configuration/IdeaScoringOptions.cs`

- [ ] **Step 1: Create the options class**

```csharp
namespace PBA.Infrastructure.Configuration;

public sealed class IdeaScoringOptions
{
    public const string SectionName = "IdeaScoring";

    /// <summary>How often the scoring sweep runs.</summary>
    public int IntervalMinutes { get; init; } = 10;

    /// <summary>Ideas scored per sweep.</summary>
    public int BatchSize { get; init; } = 20;

    /// <summary>Delay between per-idea LLM calls, to respect rate limits.</summary>
    public int ThrottleMs { get; init; } = 1000;

    /// <summary>Cheap, fast model for per-idea scoring. Defaults independent of the drafting model.</summary>
    public string Model { get; init; } = "google/gemini-2.5-flash";

    /// <summary>When false, only ideas detected after service start are scored (no 3,831-item backfill).</summary>
    public bool BackfillEnabled { get; init; } = false;
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add src/PBA.Infrastructure/Configuration/IdeaScoringOptions.cs
git commit -m "feat: add IdeaScoringOptions"
```

---

## Task 4: `IIdeaAnalyzer` interface + result model

**Files:**
- Create: `src/PBA.Application/Common/Interfaces/IIdeaAnalyzer.cs`
- Create: `src/PBA.Application/Common/Models/IdeaAnalysis.cs`

- [ ] **Step 1: Create the result record**

`IdeaAnalysis.cs`:

```csharp
namespace PBA.Application.Common.Models;

public sealed record IdeaAnalysis(
    int Score,
    string Reason,
    string Summary,
    string? Category,
    IReadOnlyList<string> Tags);
```

- [ ] **Step 2: Create the interface**

`IIdeaAnalyzer.cs`:

```csharp
using PBA.Application.Common.Models;

namespace PBA.Application.Common.Interfaces;

public interface IIdeaAnalyzer
{
    /// <summary>
    /// Scores a single idea for brand content-worthiness (0-10) and returns an
    /// AI summary, category, and tags. Returns null if the model output cannot be parsed.
    /// </summary>
    Task<IdeaAnalysis?> AnalyzeAsync(
        string title,
        string? description,
        string? url,
        string sourceName,
        CancellationToken ct = default);
}
```

- [ ] **Step 3: Verify build & commit**

Run: `dotnet build` (Expected: success).
```bash
git add src/PBA.Application/Common/Interfaces/IIdeaAnalyzer.cs src/PBA.Application/Common/Models/IdeaAnalysis.cs
git commit -m "feat: add IIdeaAnalyzer interface and IdeaAnalysis model"
```

---

## Task 5: `IdeaAnalyzer` implementation (brand scoring prompt + parsing)

**Files:**
- Create: `src/PBA.Infrastructure/Services/Radar/IdeaAnalyzer.cs`
- Test: `tests/PBA.Infrastructure.Tests/Services/Radar/IdeaAnalyzerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services.Radar;

namespace PBA.Infrastructure.Tests.Services.Radar;

public class IdeaAnalyzerTests
{
    private static IdeaAnalyzer Build(string llmResponse, out Mock<ISidecarClient> sidecar)
    {
        sidecar = new Mock<ISidecarClient>();
        sidecar.Setup(s => s.SendPromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);
        var options = Options.Create(new IdeaScoringOptions { Model = "cheap-model" });
        return new IdeaAnalyzer(sidecar.Object, options, NullLogger<IdeaAnalyzer>.Instance);
    }

    [Fact]
    public async Task AnalyzeAsync_ValidJson_ReturnsParsedAnalysis()
    {
        var analyzer = Build(
            """{"score":8,"reason":"Strong angle","summary":"One line","category":"AI","tags":["agents","enterprise"]}""",
            out _);

        var result = await analyzer.AnalyzeAsync("Title", "Desc", "http://x", "Source");

        Assert.NotNull(result);
        Assert.Equal(8, result!.Score);
        Assert.Equal("Strong angle", result.Reason);
        Assert.Equal("One line", result.Summary);
        Assert.Equal("AI", result.Category);
        Assert.Equal(new[] { "agents", "enterprise" }, result.Tags);
    }

    [Fact]
    public async Task AnalyzeAsync_PassesConfiguredCheapModel()
    {
        var analyzer = Build("""{"score":5,"reason":"r","summary":"s","category":null,"tags":[]}""", out var sidecar);

        await analyzer.AnalyzeAsync("T", null, null, "S");

        sidecar.Verify(s => s.SendPromptAsync(
            It.IsAny<string>(), It.IsAny<string>(), "cheap-model", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnalyzeAsync_MalformedJson_ReturnsNull()
    {
        var analyzer = Build("not json at all", out _);
        var result = await analyzer.AnalyzeAsync("T", null, null, "S");
        Assert.Null(result);
    }

    [Fact]
    public async Task AnalyzeAsync_FencedJson_StripsFencesAndParses()
    {
        var analyzer = Build("```json\n{\"score\":3,\"reason\":\"r\",\"summary\":\"s\",\"category\":null,\"tags\":[]}\n```", out _);
        var result = await analyzer.AnalyzeAsync("T", null, null, "S");
        Assert.NotNull(result);
        Assert.Equal(3, result!.Score);
    }

    [Fact]
    public async Task AnalyzeAsync_ScoreOutOfRange_ClampsTo0To10()
    {
        var analyzer = Build("""{"score":15,"reason":"r","summary":"s","category":null,"tags":[]}""", out _);
        var result = await analyzer.AnalyzeAsync("T", null, null, "S");
        Assert.Equal(10, result!.Score);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter FullyQualifiedName~IdeaAnalyzerTests`
Expected: FAIL to compile (no `IdeaAnalyzer`).

- [ ] **Step 3: Implement**

`src/PBA.Infrastructure/Services/Radar/IdeaAnalyzer.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services.Radar;

public sealed class IdeaAnalyzer(
    ISidecarClient sidecar,
    IOptions<IdeaScoringOptions> options,
    ILogger<IdeaAnalyzer> logger) : IIdeaAnalyzer
{
    private readonly IdeaScoringOptions _options = options.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<IdeaAnalysis?> AnalyzeAsync(
        string title, string? description, string? url, string sourceName, CancellationToken ct = default)
    {
        var system = BuildSystemPrompt();
        var user = BuildUserPrompt(title, description, url, sourceName);

        var response = await sidecar.SendPromptAsync(system, user, _options.Model, ct);

        var raw = Parse(response);
        if (raw is null) return null;

        var score = Math.Clamp(raw.Score, 0, 10);
        return new IdeaAnalysis(score, raw.Reason ?? "", raw.Summary ?? "", raw.Category,
            raw.Tags ?? []);
    }

    private static string BuildSystemPrompt() =>
        """
        You are a content strategist for Matt Kruczek, an enterprise AI thought leader. His brand
        covers enterprise AI adoption, agentic development, and AI strategy for a developer-to-executive
        audience. Score how strong a CONTENT OPPORTUNITY a news item is for his brand, 0 to 10.

        This is content-worthiness, NOT generic newsworthiness. A huge story he would never write about
        scores low. A smaller story with an ownable, opinionated angle scores high.

        Rubric:
        9-10: a strong, ownable thought-leadership angle he could publish a great post on
        7-8: clearly relevant, postable with a good take
        5-6: tangentially relevant, would need an angle
        3-4: weak fit
        0-2: off-brand or not worth covering

        Respond with ONLY a JSON object, no markdown fences, no extra text:
        {"score": 0-10, "reason": "one short sentence", "summary": "one-sentence summary of the item",
         "category": "short category or null", "tags": ["3-5", "keywords"]}
        """;

    private static string BuildUserPrompt(string title, string? description, string? url, string sourceName)
    {
        var lines = new List<string>
        {
            $"Title: {title}",
            $"Source: {sourceName}"
        };
        if (!string.IsNullOrWhiteSpace(url)) lines.Add($"URL: {url}");
        if (!string.IsNullOrWhiteSpace(description))
            lines.Add($"Content: {description[..Math.Min(1000, description.Length)]}");
        return string.Join('\n', lines);
    }

    private Raw? Parse(string response)
    {
        var json = StripFences(response);
        try
        {
            return JsonSerializer.Deserialize<Raw>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse idea analysis JSON: {Snippet}",
                response[..Math.Min(200, response.Length)]);
            return null;
        }
    }

    private static string StripFences(string response)
    {
        var json = response.Trim();
        if (!json.StartsWith("```")) return json;
        var firstNewline = json.IndexOf('\n');
        if (firstNewline >= 0) json = json[(firstNewline + 1)..];
        var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence >= 0) json = json[..lastFence];
        return json.Trim();
    }

    private sealed record Raw(
        int Score,
        [property: JsonPropertyName("reason")] string? Reason,
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("category")] string? Category,
        [property: JsonPropertyName("tags")] List<string>? Tags);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~IdeaAnalyzerTests`
Expected: PASS (all 5).

- [ ] **Step 5: Commit**

```bash
git add src/PBA.Infrastructure/Services/Radar/IdeaAnalyzer.cs tests/PBA.Infrastructure.Tests/Services/Radar/IdeaAnalyzerTests.cs
git commit -m "feat: add IdeaAnalyzer with brand content-worthiness scoring"
```

---

## Task 6: `IdeaScoringService` background service (batch, throttle, backfill toggle)

**Files:**
- Create: `src/PBA.Infrastructure/Services/Radar/IdeaScoringService.cs`
- Test: `tests/PBA.Infrastructure.Tests/Services/Radar/IdeaScoringServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Use the in-memory EF provider already used by other infra tests. (If the test project uses `Microsoft.EntityFrameworkCore.InMemory`, follow that; check an existing infra test like `RssPollingServiceTests.cs` for the `ApplicationDbContext` construction helper and reuse it.)

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Services.Radar;

namespace PBA.Infrastructure.Tests.Services.Radar;

public class IdeaScoringServiceTests
{
    private static (IdeaScoringService svc, ApplicationDbContext db, Mock<IIdeaAnalyzer> analyzer)
        Build(IdeaScoringOptions options)
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(dbOptions);

        var analyzer = new Mock<IIdeaAnalyzer>();
        analyzer.Setup(a => a.AnalyzeAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdeaAnalysis(7, "reason", "summary", "AI", new[] { "tag1" }));

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddScoped<ApplicationDbContext>(_ => db);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var svc = new IdeaScoringService(scopeFactory, analyzer.Object, Options.Create(options),
            NullLogger<IdeaScoringService>.Instance);
        return (svc, db, analyzer);
    }

    private static Idea NewIdea(DateTimeOffset detectedAt) => new()
    {
        Title = "T", SourceName = "S", DeduplicationKey = Guid.NewGuid().ToString(),
        Status = IdeaStatus.New, DetectedAt = detectedAt, ScoredAt = null
    };

    [Fact]
    public async Task ScoreBatchAsync_UnscoredIdea_PopulatesScoreSummaryTags()
    {
        var (svc, db, _) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 });
        db.Ideas.Add(NewIdea(DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        await svc.ScoreBatchAsync(backfillCutoff: null, CancellationToken.None);

        var idea = db.Ideas.Single();
        Assert.Equal(7, idea.Score);
        Assert.Equal("summary", idea.Summary);
        Assert.Equal("AI", idea.Category);
        Assert.Equal(new[] { "tag1" }, idea.Tags);
        Assert.NotNull(idea.ScoredAt);
    }

    [Fact]
    public async Task ScoreBatchAsync_BackfillCutoffSet_SkipsOldIdeas()
    {
        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 });
        var cutoff = DateTimeOffset.UtcNow;
        db.Ideas.Add(NewIdea(cutoff.AddDays(-5)));   // old, should be skipped
        db.Ideas.Add(NewIdea(cutoff.AddMinutes(5))); // new, should be scored
        await db.SaveChangesAsync();

        await svc.ScoreBatchAsync(backfillCutoff: cutoff, CancellationToken.None);

        Assert.Equal(1, db.Ideas.Count(i => i.ScoredAt != null));
        analyzer.Verify(a => a.AnalyzeAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ScoreBatchAsync_RespectsBatchSize()
    {
        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 2, ThrottleMs = 0 });
        for (var i = 0; i < 5; i++) db.Ideas.Add(NewIdea(DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        await svc.ScoreBatchAsync(backfillCutoff: null, CancellationToken.None);

        Assert.Equal(2, db.Ideas.Count(i => i.ScoredAt != null));
    }

    [Fact]
    public async Task ScoreBatchAsync_AnalyzerReturnsNull_LeavesIdeaUnscored()
    {
        var (svc, db, analyzer) = Build(new IdeaScoringOptions { BatchSize = 10, ThrottleMs = 0 });
        analyzer.Setup(a => a.AnalyzeAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdeaAnalysis?)null);
        db.Ideas.Add(NewIdea(DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        await svc.ScoreBatchAsync(backfillCutoff: null, CancellationToken.None);

        Assert.Null(db.Ideas.Single().ScoredAt);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter FullyQualifiedName~IdeaScoringServiceTests`
Expected: FAIL to compile.

- [ ] **Step 3: Implement**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;

namespace PBA.Infrastructure.Services.Radar;

public sealed class IdeaScoringService(
    IServiceScopeFactory scopeFactory,
    IIdeaAnalyzer analyzer,
    IOptions<IdeaScoringOptions> options,
    ILogger<IdeaScoringService> logger) : BackgroundService
{
    private readonly IdeaScoringOptions _options = options.Value;
    private DateTimeOffset _startedAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _startedAt = DateTimeOffset.UtcNow;
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cutoff = _options.BackfillEnabled ? (DateTimeOffset?)null : _startedAt;
                await ScoreBatchAsync(cutoff, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Idea scoring sweep failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(_options.IntervalMinutes), stoppingToken);
        }
    }

    internal async Task ScoreBatchAsync(DateTimeOffset? backfillCutoff, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var query = db.Ideas.Where(i => i.ScoredAt == null);
        if (backfillCutoff is { } cutoff)
            query = query.Where(i => i.DetectedAt >= cutoff);

        var batch = await query
            .OrderByDescending(i => i.DetectedAt)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0) return;

        var scored = 0;
        foreach (var idea in batch)
        {
            var analysis = await analyzer.AnalyzeAsync(
                idea.Title, idea.Description, idea.Url, idea.SourceName, ct);

            if (analysis is not null)
            {
                idea.Score = analysis.Score;
                idea.ScoreReason = analysis.Reason;
                idea.Summary = analysis.Summary;
                idea.Category ??= analysis.Category;
                if (analysis.Tags.Count > 0) idea.Tags = analysis.Tags.ToList();
                idea.ScoredAt = DateTimeOffset.UtcNow;
                scored++;
            }

            if (_options.ThrottleMs > 0)
                await Task.Delay(_options.ThrottleMs, ct);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Scored {Scored}/{Total} ideas", scored, batch.Count);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~IdeaScoringServiceTests`
Expected: PASS (all 4).

- [ ] **Step 5: Commit**

```bash
git add src/PBA.Infrastructure/Services/Radar/IdeaScoringService.cs tests/PBA.Infrastructure.Tests/Services/Radar/IdeaScoringServiceTests.cs
git commit -m "feat: add IdeaScoringService background sweep with backfill toggle"
```

---

## Task 7: `ClusteringOptions` + `IIdeaClusterer` + result model

**Files:**
- Create: `src/PBA.Infrastructure/Configuration/ClusteringOptions.cs`
- Create: `src/PBA.Application/Common/Interfaces/IIdeaClusterer.cs`

- [ ] **Step 1: Create the options class**

```csharp
namespace PBA.Infrastructure.Configuration;

public sealed class ClusteringOptions
{
    public const string SectionName = "Clustering";

    public int IntervalMinutes { get; init; } = 30;
    public int MinScore { get; init; } = 6;
    public int LookbackHours { get; init; } = 48;
    public int MaxItemsPerSweep { get; init; } = 40;
    public string Model { get; init; } = "google/gemini-2.5-flash";
}
```

- [ ] **Step 2: Create the interface + input/result records**

`IIdeaClusterer.cs`:

```csharp
namespace PBA.Application.Common.Interfaces;

public sealed record ClusterInput(int Index, string Title, string? Summary);

public interface IIdeaClusterer
{
    /// <summary>
    /// Groups items that cover the same real-world event. Returns groups of input indices;
    /// the first index in each group is the primary. Returns an empty list on parse failure.
    /// </summary>
    Task<IReadOnlyList<IReadOnlyList<int>>> ClusterAsync(
        IReadOnlyList<ClusterInput> items, CancellationToken ct = default);
}
```

- [ ] **Step 3: Verify build & commit**

Run: `dotnet build` (Expected: success).
```bash
git add src/PBA.Infrastructure/Configuration/ClusteringOptions.cs src/PBA.Application/Common/Interfaces/IIdeaClusterer.cs
git commit -m "feat: add ClusteringOptions and IIdeaClusterer interface"
```

---

## Task 8: `IdeaClusterer` implementation

**Files:**
- Create: `src/PBA.Infrastructure/Services/Radar/IdeaClusterer.cs`
- Test: `tests/PBA.Infrastructure.Tests/Services/Radar/IdeaClustererTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services.Radar;

namespace PBA.Infrastructure.Tests.Services.Radar;

public class IdeaClustererTests
{
    private static IdeaClusterer Build(string llmResponse)
    {
        var sidecar = new Mock<ISidecarClient>();
        sidecar.Setup(s => s.SendPromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);
        return new IdeaClusterer(sidecar.Object, Options.Create(new ClusteringOptions()),
            NullLogger<IdeaClusterer>.Instance);
    }

    private static IReadOnlyList<ClusterInput> Items() =>
    [
        new(0, "GPT-5 released", "OpenAI ships GPT-5"),
        new(1, "OpenAI launches GPT-5", "New flagship model"),
        new(2, "Unrelated story", "Something else")
    ];

    [Fact]
    public async Task ClusterAsync_ValidGroups_ReturnsGroups()
    {
        var clusterer = Build("""{"groups":[[0,1]]}""");
        var groups = await clusterer.ClusterAsync(Items());
        Assert.Single(groups);
        Assert.Equal(new[] { 0, 1 }, groups[0]);
    }

    [Fact]
    public async Task ClusterAsync_MalformedJson_ReturnsEmpty()
    {
        var clusterer = Build("garbage");
        var groups = await clusterer.ClusterAsync(Items());
        Assert.Empty(groups);
    }

    [Fact]
    public async Task ClusterAsync_FewerThanTwoItems_DoesNotCallLlm()
    {
        var sidecar = new Mock<ISidecarClient>();
        var clusterer = new IdeaClusterer(sidecar.Object, Options.Create(new ClusteringOptions()),
            NullLogger<IdeaClusterer>.Instance);

        var groups = await clusterer.ClusterAsync([new(0, "only one", null)]);

        Assert.Empty(groups);
        sidecar.Verify(s => s.SendPromptAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter FullyQualifiedName~IdeaClustererTests`
Expected: FAIL to compile.

- [ ] **Step 3: Implement**

```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services.Radar;

public sealed class IdeaClusterer(
    ISidecarClient sidecar,
    IOptions<ClusteringOptions> options,
    ILogger<IdeaClusterer> logger) : IIdeaClusterer
{
    private readonly ClusteringOptions _options = options.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<IReadOnlyList<IReadOnlyList<int>>> ClusterAsync(
        IReadOnlyList<ClusterInput> items, CancellationToken ct = default)
    {
        if (items.Count < 2) return [];

        var response = await sidecar.SendPromptAsync(System, BuildUser(items), _options.Model, ct);

        try
        {
            var parsed = JsonSerializer.Deserialize<Result>(StripFences(response), JsonOptions);
            return parsed?.Groups?
                .Where(g => g.Count >= 2)
                .Select(g => (IReadOnlyList<int>)g)
                .ToList() ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse clustering JSON");
            return [];
        }
    }

    private const string System =
        """
        You are a news deduplication assistant. Group items that report the IDENTICAL real-world
        event (for example the same product release announced in different outlets). Do NOT group
        items that merely share a topic but cover different events. When unsure, keep them separate.

        Respond with ONLY JSON, no fences:
        {"groups": [[0, 3], [5, 7, 9]]}
        Each inner array lists the input indices of one event. The first index is the primary.
        Only include groups of 2 or more. Omit singletons.
        """;

    private static string BuildUser(IReadOnlyList<ClusterInput> items)
    {
        var sb = new StringBuilder("Items:\n");
        foreach (var item in items)
        {
            sb.Append('[').Append(item.Index).Append("] ").Append(item.Title);
            if (!string.IsNullOrWhiteSpace(item.Summary)) sb.Append(" -- ").Append(item.Summary);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string StripFences(string response)
    {
        var json = response.Trim();
        if (!json.StartsWith("```")) return json;
        var firstNewline = json.IndexOf('\n');
        if (firstNewline >= 0) json = json[(firstNewline + 1)..];
        var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence >= 0) json = json[..lastFence];
        return json.Trim();
    }

    private sealed record Result([property: JsonPropertyName("groups")] List<List<int>>? Groups);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~IdeaClustererTests`
Expected: PASS (all 3).

- [ ] **Step 5: Commit**

```bash
git add src/PBA.Infrastructure/Services/Radar/IdeaClusterer.cs tests/PBA.Infrastructure.Tests/Services/Radar/IdeaClustererTests.cs
git commit -m "feat: add IdeaClusterer for same-event grouping"
```

---

## Task 9: `IdeaClusteringService` background service

**Files:**
- Create: `src/PBA.Infrastructure/Services/Radar/IdeaClusteringService.cs`
- Test: `tests/PBA.Infrastructure.Tests/Services/Radar/IdeaClusteringServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Services.Radar;

namespace PBA.Infrastructure.Tests.Services.Radar;

public class IdeaClusteringServiceTests
{
    private static (IdeaClusteringService svc, ApplicationDbContext db, Mock<IIdeaClusterer> clusterer)
        Build(ClusteringOptions options)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var clusterer = new Mock<IIdeaClusterer>();

        var services = new ServiceCollection();
        services.AddScoped(_ => db);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return (new IdeaClusteringService(scopeFactory, clusterer.Object, Options.Create(options),
            NullLogger<IdeaClusteringService>.Instance), db, clusterer);
    }

    private static Idea Scored(int score) => new()
    {
        Title = Guid.NewGuid().ToString(), SourceName = "S", DeduplicationKey = Guid.NewGuid().ToString(),
        Status = IdeaStatus.New, DetectedAt = DateTimeOffset.UtcNow, Score = score,
        ScoredAt = DateTimeOffset.UtcNow, ClusteredAt = null, DuplicateOfId = null
    };

    [Fact]
    public async Task ClusterBatchAsync_GroupsReturned_SetsDuplicateOfIdOnSecondary()
    {
        var (svc, db, clusterer) = Build(new ClusteringOptions { MinScore = 6, LookbackHours = 48 });
        var a = Scored(8); var b = Scored(7);
        db.Ideas.AddRange(a, b);
        await db.SaveChangesAsync();

        clusterer.Setup(c => c.ClusterAsync(It.IsAny<IReadOnlyList<ClusterInput>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IReadOnlyList<int>> { new[] { 0, 1 } });

        await svc.ClusterBatchAsync(CancellationToken.None);

        var primary = db.Ideas.Single(i => i.DuplicateOfId == null);
        var dup = db.Ideas.Single(i => i.DuplicateOfId != null);
        Assert.Equal(primary.Id, dup.DuplicateOfId);
        Assert.All(db.Ideas, i => Assert.NotNull(i.ClusteredAt));
    }

    [Fact]
    public async Task ClusterBatchAsync_LowScoreIdeas_AreExcluded()
    {
        var (svc, db, clusterer) = Build(new ClusteringOptions { MinScore = 6 });
        db.Ideas.AddRange(Scored(3), Scored(2));
        await db.SaveChangesAsync();

        await svc.ClusterBatchAsync(CancellationToken.None);

        clusterer.Verify(c => c.ClusterAsync(It.IsAny<IReadOnlyList<ClusterInput>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter FullyQualifiedName~IdeaClusteringServiceTests`
Expected: FAIL to compile.

- [ ] **Step 3: Implement**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;

namespace PBA.Infrastructure.Services.Radar;

public sealed class IdeaClusteringService(
    IServiceScopeFactory scopeFactory,
    IIdeaClusterer clusterer,
    IOptions<ClusteringOptions> options,
    ILogger<IdeaClusteringService> logger) : BackgroundService
{
    private readonly ClusteringOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ClusterBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Idea clustering sweep failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(_options.IntervalMinutes), stoppingToken);
        }
    }

    internal async Task ClusterBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var since = DateTimeOffset.UtcNow.AddHours(-_options.LookbackHours);
        var candidates = await db.Ideas
            .Where(i => i.ClusteredAt == null
                && i.ScoredAt != null
                && i.Score >= _options.MinScore
                && i.DuplicateOfId == null
                && i.DetectedAt >= since)
            .OrderByDescending(i => i.Score)
            .Take(_options.MaxItemsPerSweep)
            .ToListAsync(ct);

        if (candidates.Count < 2) return;

        var inputs = candidates
            .Select((idea, idx) => new ClusterInput(idx, idea.Title, idea.Summary))
            .ToList();

        var groups = await clusterer.ClusterAsync(inputs, ct);

        foreach (var group in groups)
        {
            if (group.Count < 2) continue;
            var primary = candidates[group[0]];
            foreach (var dupIdx in group.Skip(1))
            {
                if (dupIdx < 0 || dupIdx >= candidates.Count) continue;
                candidates[dupIdx].DuplicateOfId = primary.Id;
            }
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var idea in candidates) idea.ClusteredAt = now;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Clustered {Count} ideas into {Groups} groups", candidates.Count, groups.Count);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~IdeaClusteringServiceTests`
Expected: PASS (both).

- [ ] **Step 5: Commit**

```bash
git add src/PBA.Infrastructure/Services/Radar/IdeaClusteringService.cs tests/PBA.Infrastructure.Tests/Services/Radar/IdeaClusteringServiceTests.cs
git commit -m "feat: add IdeaClusteringService to merge same-event ideas"
```

---

## Task 10: `Digest` + `DigestItem` entities, configs, DbSets, migration

**Files:**
- Create: `src/PBA.Domain/Entities/Digest.cs`
- Create: `src/PBA.Domain/Entities/DigestItem.cs`
- Create: `src/PBA.Infrastructure/Data/Configurations/DigestConfiguration.cs`
- Create: `src/PBA.Infrastructure/Data/Configurations/DigestItemConfiguration.cs`
- Modify: `src/PBA.Infrastructure/Data/ApplicationDbContext.cs`
- Modify: `src/PBA.Application/Common/Interfaces/IAppDbContext.cs` (add the new DbSets if it declares them)
- Create (generated): migration `AddDigestTables`

- [ ] **Step 1: Create the entities**

`Digest.cs`:

```csharp
namespace PBA.Domain.Entities;

public class Digest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public required string Title { get; set; }
    public required string Intro { get; set; }
    public int ItemCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<DigestItem> Items { get; set; } = [];
}
```

`DigestItem.cs`:

```csharp
namespace PBA.Domain.Entities;

public class DigestItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid DigestId { get; set; }
    public Guid IdeaId { get; set; }
    public int Rank { get; set; }
    public int Score { get; set; }
    public required string WhyItMatters { get; set; }

    public Digest? Digest { get; set; }
    public Idea? Idea { get; set; }
}
```

- [ ] **Step 2: Create EF configurations**

`DigestConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data.Configurations;

public class DigestConfiguration : IEntityTypeConfiguration<Digest>
{
    public void Configure(EntityTypeBuilder<Digest> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Title).IsRequired().HasMaxLength(300);
        builder.Property(d => d.Intro).HasColumnType("text").IsRequired();
        builder.HasIndex(d => d.Date).IsUnique();

        builder.HasMany(d => d.Items)
            .WithOne(i => i.Digest!)
            .HasForeignKey(i => i.DigestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

`DigestItemConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data.Configurations;

public class DigestItemConfiguration : IEntityTypeConfiguration<DigestItem>
{
    public void Configure(EntityTypeBuilder<DigestItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.WhyItMatters).HasColumnType("text").IsRequired();

        builder.HasOne(i => i.Idea)
            .WithMany()
            .HasForeignKey(i => i.IdeaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.DigestId);
    }
}
```

- [ ] **Step 3: Register DbSets**

In `ApplicationDbContext.cs` add after `FeedItems`:

```csharp
    public DbSet<Digest> Digests => Set<Digest>();
    public DbSet<DigestItem> DigestItems => Set<DigestItem>();
```

Check `src/PBA.Application/Common/Interfaces/IAppDbContext.cs`: if it declares the other DbSets (`Ideas`, `FeedItems`, etc.), add `Digests` and `DigestItems` there too with matching `DbSet<T>` signatures. If it does not declare DbSets, skip.

- [ ] **Step 4: Generate the migration & build**

Run:
```bash
dotnet ef migrations add AddDigestTables --project src/PBA.Infrastructure --startup-project src/PBA.Api --output-dir Data/Migrations
dotnet build
```
Expected: migration creates `Digests` and `DigestItems` tables; build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/PBA.Domain/Entities/Digest.cs src/PBA.Domain/Entities/DigestItem.cs src/PBA.Infrastructure/Data/Configurations/Digest*.cs src/PBA.Infrastructure/Data/ApplicationDbContext.cs src/PBA.Application/Common/Interfaces/IAppDbContext.cs src/PBA.Infrastructure/Data/Migrations/
git commit -m "feat: add Digest and DigestItem entities + migration"
```

---

## Task 11: `DigestOptions` + `IDigestWriter` + result model

**Files:**
- Create: `src/PBA.Infrastructure/Configuration/DigestOptions.cs`
- Create: `src/PBA.Application/Common/Interfaces/IDigestWriter.cs`

- [ ] **Step 1: Create the options class**

```csharp
namespace PBA.Infrastructure.Configuration;

public sealed class DigestOptions
{
    public const string SectionName = "Digest";

    /// <summary>Local time of day (24h) to generate the digest, e.g. "07:00".</summary>
    public string RunAtLocalTime { get; init; } = "07:00";
    public int TopN { get; init; } = 8;
    public int LookbackHours { get; init; } = 24;

    /// <summary>Digest copy is Matt-facing brand prose; use the quality model.</summary>
    public string Model { get; init; } = "google/gemini-2.5-pro";
}
```

- [ ] **Step 2: Create the interface + records**

`IDigestWriter.cs`:

```csharp
namespace PBA.Application.Common.Interfaces;

public sealed record DigestInput(int Index, string Title, string Summary, int Score, string? Url);

public sealed record DigestItemCopy(int Index, string WhyItMatters);

public sealed record DigestCopy(string Title, string Intro, IReadOnlyList<DigestItemCopy> Items);

public interface IDigestWriter
{
    /// <summary>
    /// Writes a brand-voice daily brief (no em-dashes) over the top items.
    /// Returns null if the model output cannot be parsed.
    /// </summary>
    Task<DigestCopy?> WriteAsync(IReadOnlyList<DigestInput> items, CancellationToken ct = default);
}
```

- [ ] **Step 3: Verify build & commit**

Run: `dotnet build` (Expected: success).
```bash
git add src/PBA.Infrastructure/Configuration/DigestOptions.cs src/PBA.Application/Common/Interfaces/IDigestWriter.cs
git commit -m "feat: add DigestOptions and IDigestWriter interface"
```

---

## Task 12: `DigestWriter` implementation (humanized, no em-dashes)

**Files:**
- Create: `src/PBA.Infrastructure/Services/Radar/DigestWriter.cs`
- Test: `tests/PBA.Infrastructure.Tests/Services/Radar/DigestWriterTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services.Radar;

namespace PBA.Infrastructure.Tests.Services.Radar;

public class DigestWriterTests
{
    private static DigestWriter Build(string response, out Mock<ISidecarClient> sidecar)
    {
        sidecar = new Mock<ISidecarClient>();
        sidecar.Setup(s => s.SendPromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        return new DigestWriter(sidecar.Object, Options.Create(new DigestOptions { Model = "quality-model" }),
            NullLogger<DigestWriter>.Instance);
    }

    private static IReadOnlyList<DigestInput> Items() =>
        [new(0, "Story A", "Summary A", 9, "http://a"), new(1, "Story B", "Summary B", 7, null)];

    [Fact]
    public async Task WriteAsync_ValidJson_ReturnsCopy()
    {
        var writer = Build(
            """{"title":"Daily Brief","intro":"Today in AI.","items":[{"index":0,"whyItMatters":"Big deal."},{"index":1,"whyItMatters":"Notable."}]}""",
            out _);

        var copy = await writer.WriteAsync(Items());

        Assert.NotNull(copy);
        Assert.Equal("Daily Brief", copy!.Title);
        Assert.Equal(2, copy.Items.Count);
        Assert.Equal("Big deal.", copy.Items[0].WhyItMatters);
    }

    [Fact]
    public async Task WriteAsync_UsesQualityModel()
    {
        var writer = Build("""{"title":"t","intro":"i","items":[]}""", out var sidecar);
        await writer.WriteAsync(Items());
        sidecar.Verify(s => s.SendPromptAsync(
            It.IsAny<string>(), It.IsAny<string>(), "quality-model", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WriteAsync_StripsEmDashesFromOutput()
    {
        var writer = Build(
            """{"title":"Brief — Today","intro":"AI moves fast — keep up.","items":[{"index":0,"whyItMatters":"A — B"}]}""",
            out _);

        var copy = await writer.WriteAsync(Items());

        Assert.DoesNotContain('—', copy!.Title);
        Assert.DoesNotContain('—', copy.Intro);
        Assert.DoesNotContain('—', copy.Items[0].WhyItMatters);
    }

    [Fact]
    public async Task WriteAsync_MalformedJson_ReturnsNull()
    {
        var writer = Build("nope", out _);
        Assert.Null(await writer.WriteAsync(Items()));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter FullyQualifiedName~DigestWriterTests`
Expected: FAIL to compile.

- [ ] **Step 3: Implement**

```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services.Radar;

public sealed class DigestWriter(
    ISidecarClient sidecar,
    IOptions<DigestOptions> options,
    ILogger<DigestWriter> logger) : IDigestWriter
{
    private readonly DigestOptions _options = options.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<DigestCopy?> WriteAsync(IReadOnlyList<DigestInput> items, CancellationToken ct = default)
    {
        if (items.Count == 0) return null;

        var response = await sidecar.SendPromptAsync(System, BuildUser(items), _options.Model, ct);

        try
        {
            var raw = JsonSerializer.Deserialize<Raw>(StripFences(response), JsonOptions);
            if (raw is null) return null;

            var itemCopies = (raw.Items ?? [])
                .Select(i => new DigestItemCopy(i.Index, Clean(i.WhyItMatters ?? "")))
                .ToList();

            return new DigestCopy(Clean(raw.Title ?? "Daily Brief"), Clean(raw.Intro ?? ""), itemCopies);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse digest JSON");
            return null;
        }
    }

    // Honors the brand rule: no em-dashes in Matt-facing copy.
    private static string Clean(string s) => s.Replace('—', '-').Replace('–', '-');

    private const string System =
        """
        You write a daily AI news brief for Matt Kruczek, an enterprise AI thought leader, in his voice:
        direct, developer-to-executive, no hype, no filler. Given the day's top items, write a short intro
        (2-3 sentences) and a one-sentence "why it matters" for each item, framed around the content angle
        Matt could take. Never use em-dashes or en-dashes. Plain language only.

        Respond with ONLY JSON, no fences:
        {"title": "short title", "intro": "2-3 sentences",
         "items": [{"index": 0, "whyItMatters": "one sentence"}]}
        Include one items entry per input index.
        """;

    private static string BuildUser(IReadOnlyList<DigestInput> items)
    {
        var sb = new StringBuilder("Top items:\n");
        foreach (var item in items)
        {
            sb.Append('[').Append(item.Index).Append("] (score ").Append(item.Score).Append(") ")
              .Append(item.Title).Append(" -- ").Append(item.Summary).Append('\n');
        }
        return sb.ToString();
    }

    private static string StripFences(string response)
    {
        var json = response.Trim();
        if (!json.StartsWith("```")) return json;
        var firstNewline = json.IndexOf('\n');
        if (firstNewline >= 0) json = json[(firstNewline + 1)..];
        var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence >= 0) json = json[..lastFence];
        return json.Trim();
    }

    private sealed record Raw(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("intro")] string? Intro,
        [property: JsonPropertyName("items")] List<RawItem>? Items);

    private sealed record RawItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("whyItMatters")] string? WhyItMatters);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~DigestWriterTests`
Expected: PASS (all 4).

- [ ] **Step 5: Commit**

```bash
git add src/PBA.Infrastructure/Services/Radar/DigestWriter.cs tests/PBA.Infrastructure.Tests/Services/Radar/DigestWriterTests.cs
git commit -m "feat: add DigestWriter for brand-voice daily brief"
```

---

## Task 13: `DigestService` background service (Digest + DigestItems + FeedItem alert)

**Files:**
- Create: `src/PBA.Infrastructure/Services/Radar/DigestService.cs`
- Test: `tests/PBA.Infrastructure.Tests/Services/Radar/DigestServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Services.Radar;

namespace PBA.Infrastructure.Tests.Services.Radar;

public class DigestServiceTests
{
    private static (DigestService svc, ApplicationDbContext db, Mock<IDigestWriter> writer) Build(DigestOptions options)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var writer = new Mock<IDigestWriter>();
        var services = new ServiceCollection();
        services.AddScoped(_ => db);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return (new DigestService(scopeFactory, writer.Object, Options.Create(options),
            NullLogger<DigestService>.Instance), db, writer);
    }

    private static Idea Scored(int score, Guid? dupOf = null) => new()
    {
        Title = Guid.NewGuid().ToString(), SourceName = "S", DeduplicationKey = Guid.NewGuid().ToString(),
        Status = IdeaStatus.New, DetectedAt = DateTimeOffset.UtcNow, Score = score,
        ScoredAt = DateTimeOffset.UtcNow, Summary = "summary", DuplicateOfId = dupOf
    };

    [Fact]
    public async Task GenerateDigestAsync_TopScoredPrimaries_CreatesDigestItemsAndFeedAlert()
    {
        var (svc, db, writer) = Build(new DigestOptions { TopN = 8, LookbackHours = 24 });
        var a = Scored(9); var b = Scored(7);
        db.Ideas.AddRange(a, b);
        await db.SaveChangesAsync();

        writer.Setup(w => w.WriteAsync(It.IsAny<IReadOnlyList<DigestInput>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DigestCopy("Brief", "Intro", new List<DigestItemCopy>
            {
                new(0, "Why A"), new(1, "Why B")
            }));

        await svc.GenerateDigestAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        var digest = db.Digests.Include(d => d.Items).Single();
        Assert.Equal("Brief", digest.Title);
        Assert.Equal(2, digest.Items.Count);
        Assert.Equal(1, digest.Items.Single(i => i.IdeaId == a.Id).Rank); // highest score ranked 1
        Assert.Single(db.FeedItems.Where(f => f.Type == FeedItemType.SystemNotification));
    }

    [Fact]
    public async Task GenerateDigestAsync_ExcludesDuplicates()
    {
        var (svc, db, writer) = Build(new DigestOptions { TopN = 8 });
        var primary = Scored(9);
        db.Ideas.Add(primary);
        await db.SaveChangesAsync();
        db.Ideas.Add(Scored(10, dupOf: primary.Id)); // duplicate, must be excluded despite higher score
        await db.SaveChangesAsync();

        DigestInput[]? captured = null;
        writer.Setup(w => w.WriteAsync(It.IsAny<IReadOnlyList<DigestInput>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<DigestInput>, CancellationToken>((inp, _) => captured = inp.ToArray())
            .ReturnsAsync(new DigestCopy("t", "i", new List<DigestItemCopy> { new(0, "w") }));

        await svc.GenerateDigestAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Single(captured!);
    }

    [Fact]
    public async Task GenerateDigestAsync_DigestForDateExists_SkipsAndDoesNotCallWriter()
    {
        var (svc, db, writer) = Build(new DigestOptions());
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        db.Digests.Add(new Digest { Date = today, Title = "x", Intro = "y", CreatedAt = DateTimeOffset.UtcNow });
        db.Ideas.Add(Scored(9));
        await db.SaveChangesAsync();

        await svc.GenerateDigestAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        writer.Verify(w => w.WriteAsync(It.IsAny<IReadOnlyList<DigestInput>>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Single(db.Digests);
    }

    [Fact]
    public async Task GenerateDigestAsync_NoScoredIdeas_DoesNothing()
    {
        var (svc, db, writer) = Build(new DigestOptions());
        await svc.GenerateDigestAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Empty(db.Digests);
        writer.Verify(w => w.WriteAsync(It.IsAny<IReadOnlyList<DigestInput>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter FullyQualifiedName~DigestServiceTests`
Expected: FAIL to compile.

- [ ] **Step 3: Implement**

The `BackgroundService` loop wakes hourly and calls `GenerateDigestAsync` only when the local hour/minute matches `RunAtLocalTime`; the once-per-day guard is the unique `Digest.Date`.

```csharp
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;

namespace PBA.Infrastructure.Services.Radar;

public sealed class DigestService(
    IServiceScopeFactory scopeFactory,
    IDigestWriter writer,
    IOptions<DigestOptions> options,
    ILogger<DigestService> logger) : BackgroundService
{
    private readonly DigestOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var runAt = TimeOnly.ParseExact(_options.RunAtLocalTime, "HH:mm", CultureInfo.InvariantCulture);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.Now;
                if (TimeOnly.FromDateTime(now.DateTime) >= runAt)
                    await GenerateDigestAsync(now, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Digest generation failed");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    internal async Task GenerateDigestAsync(DateTimeOffset now, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var date = DateOnly.FromDateTime(now.UtcDateTime);
        if (await db.Digests.AnyAsync(d => d.Date == date, ct))
            return;

        var since = now.AddHours(-_options.LookbackHours);
        var top = await db.Ideas
            .Where(i => i.ScoredAt != null && i.DuplicateOfId == null && i.DetectedAt >= since)
            .OrderByDescending(i => i.Score)
            .Take(_options.TopN)
            .ToListAsync(ct);

        if (top.Count == 0) return;

        var inputs = top
            .Select((idea, idx) => new DigestInput(idx, idea.Title, idea.Summary ?? "", idea.Score ?? 0, idea.Url))
            .ToList();

        var copy = await writer.WriteAsync(inputs, ct);
        if (copy is null) return;

        var digest = new Digest
        {
            Date = date,
            Title = copy.Title,
            Intro = copy.Intro,
            ItemCount = top.Count,
            CreatedAt = now
        };

        var whyByIndex = copy.Items.ToDictionary(i => i.Index, i => i.WhyItMatters);
        for (var idx = 0; idx < top.Count; idx++)
        {
            digest.Items.Add(new DigestItem
            {
                DigestId = digest.Id,
                IdeaId = top[idx].Id,
                Rank = idx + 1,
                Score = top[idx].Score ?? 0,
                WhyItMatters = whyByIndex.TryGetValue(idx, out var why) ? why : ""
            });
        }

        db.Digests.Add(digest);

        db.FeedItems.Add(new FeedItem
        {
            Type = FeedItemType.SystemNotification,
            Title = copy.Title,
            Summary = copy.Intro.Length > 280 ? copy.Intro[..280] : copy.Intro,
            Data = JsonSerializer.Serialize(new { digestId = digest.Id }),
            Priority = FeedItemPriority.Normal,
            CreatedAt = now
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Generated digest {Date} with {Count} items", date, top.Count);
    }
}
```

> **Note:** confirm `FeedItemPriority.Normal` exists by reading `src/PBA.Domain/Enums/FeedItemPriority.cs`. If the member is named differently (e.g. `Medium`), use that value.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~DigestServiceTests`
Expected: PASS (all 4).

- [ ] **Step 5: Commit**

```bash
git add src/PBA.Infrastructure/Services/Radar/DigestService.cs tests/PBA.Infrastructure.Tests/Services/Radar/DigestServiceTests.cs
git commit -m "feat: add DigestService to generate daily brief and feed alert"
```

---

## Task 14: Register radar services & options in DI

**Files:**
- Modify: `src/PBA.Infrastructure/DependencyInjection.cs:55-57`

- [ ] **Step 1: Add registrations**

In `AddInfrastructureDependencies`, just after the `AiConnectionsService` registration (line 57), add:

```csharp
        // AI News Radar (Horizon-inspired): scoring -> clustering -> daily digest.
        services.Configure<IdeaScoringOptions>(configuration.GetSection(IdeaScoringOptions.SectionName));
        services.Configure<ClusteringOptions>(configuration.GetSection(ClusteringOptions.SectionName));
        services.Configure<DigestOptions>(configuration.GetSection(DigestOptions.SectionName));

        services.AddScoped<IIdeaAnalyzer, PBA.Infrastructure.Services.Radar.IdeaAnalyzer>();
        services.AddScoped<IIdeaClusterer, PBA.Infrastructure.Services.Radar.IdeaClusterer>();
        services.AddScoped<IDigestWriter, PBA.Infrastructure.Services.Radar.DigestWriter>();

        services.AddHostedService<PBA.Infrastructure.Services.Radar.IdeaScoringService>();
        services.AddHostedService<PBA.Infrastructure.Services.Radar.IdeaClusteringService>();
        services.AddHostedService<PBA.Infrastructure.Services.Radar.DigestService>();
```

Add `using PBA.Application.Common.Interfaces;` if not already present (it is, via existing usings).

- [ ] **Step 2: Verify the whole solution builds & all tests pass**

Run: `dotnet build && dotnet test`
Expected: build success; all existing + new tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/PBA.Infrastructure/DependencyInjection.cs
git commit -m "feat: register AI news radar services and options"
```

---

## Task 15: Extend `IdeaDto` + `ListIdeas` (score field, sort, min-score filter, exclude duplicates)

**Files:**
- Modify: `src/PBA.Application/Features/Ideas/Dtos/IdeaDto.cs`
- Modify: `src/PBA.Application/Features/Ideas/Queries/ListIdeas.cs`
- Test: `tests/PBA.Application.Tests/Features/Ideas/Queries/ListIdeasHandlerTests.cs` (extend)

- [ ] **Step 1: Write the failing tests**

Add to the existing `ListIdeasHandlerTests.cs` (reuse its in-memory `IAppDbContext`/seed helpers). If helper names differ, mirror the existing tests' setup.

```csharp
    [Fact]
    public async Task Handle_SortByScore_OrdersByScoreDescending()
    {
        // Arrange: seed three ideas with scores 3, 9, 6 (reuse this file's seeding helper)
        // Act
        var result = await _handler.Handle(new ListIdeas.Query { SortBy = "score", SortDirection = "desc" }, default);
        // Assert
        var scores = result.Value.Items.Select(i => i.Score).ToList();
        Assert.Equal(new int?[] { 9, 6, 3 }, scores);
    }

    [Fact]
    public async Task Handle_MinScoreFilter_ExcludesLowerScores()
    {
        var result = await _handler.Handle(new ListIdeas.Query { MinScore = 6 }, default);
        Assert.All(result.Value.Items, i => Assert.True(i.Score >= 6));
    }

    [Fact]
    public async Task Handle_ByDefault_ExcludesDuplicates()
    {
        // Seed one primary + one with DuplicateOfId set to the primary
        var result = await _handler.Handle(new ListIdeas.Query(), default);
        Assert.DoesNotContain(result.Value.Items, i => i.IsDuplicate);
    }

    [Fact]
    public async Task Handle_IncludeDuplicatesTrue_IncludesThem()
    {
        var result = await _handler.Handle(new ListIdeas.Query { IncludeDuplicates = true }, default);
        Assert.Contains(result.Value.Items, i => i.IsDuplicate);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter FullyQualifiedName~ListIdeasHandlerTests`
Expected: FAIL to compile (no `Score`/`IsDuplicate`/`MinScore`/`IncludeDuplicates`).

- [ ] **Step 3: Implement**

Add to `IdeaDto.cs`:

```csharp
    public int? Score { get; init; }
    public string? ScoreReason { get; init; }
    public bool IsDuplicate { get; init; }
```

In `ListIdeas.cs` `Query`, add:

```csharp
        public int? MinScore { get; init; }
        public bool IncludeDuplicates { get; init; }
```

In `Handle`, after the status filter block, add duplicate + min-score filters:

```csharp
            if (!request.IncludeDuplicates)
                query = query.Where(i => i.DuplicateOfId == null);

            if (request.MinScore.HasValue)
                query = query.Where(i => i.Score >= request.MinScore.Value);
```

In the `.Select(...)` projection add the three new fields:

```csharp
                    Score = i.Score,
                    ScoreReason = i.ScoreReason,
                    IsDuplicate = i.DuplicateOfId != null,
```

In `ApplySort`, add a `score` case (nulls sort last naturally in EF for desc as nullable):

```csharp
                "score" => i => i.Score!,
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ListIdeasHandlerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PBA.Application/Features/Ideas/Dtos/IdeaDto.cs src/PBA.Application/Features/Ideas/Queries/ListIdeas.cs tests/PBA.Application.Tests/Features/Ideas/Queries/ListIdeasHandlerTests.cs
git commit -m "feat: add score sort, min-score filter, duplicate exclusion to ListIdeas"
```

---

## Task 16: Digest DTOs + queries (`ListDigests`, `GetLatestDigest`, `GetDigest`)

**Files:**
- Create: `src/PBA.Application/Features/Digests/Dtos/DigestDto.cs`
- Create: `src/PBA.Application/Features/Digests/Dtos/DigestSummaryDto.cs`
- Create: `src/PBA.Application/Features/Digests/Queries/ListDigests.cs`
- Create: `src/PBA.Application/Features/Digests/Queries/GetDigest.cs`
- Create: `src/PBA.Application/Features/Digests/Queries/GetLatestDigest.cs`
- Test: `tests/PBA.Application.Tests/Features/Digests/DigestQueriesTests.cs`

- [ ] **Step 1: Create DTOs**

`DigestDto.cs`:

```csharp
namespace PBA.Application.Features.Digests.Dtos;

public record DigestItemDto
{
    public Guid IdeaId { get; init; }
    public int Rank { get; init; }
    public int Score { get; init; }
    public string WhyItMatters { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Url { get; init; }
}

public record DigestDto
{
    public Guid Id { get; init; }
    public DateOnly Date { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Intro { get; init; } = string.Empty;
    public int ItemCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<DigestItemDto> Items { get; init; } = [];
}
```

`DigestSummaryDto.cs`:

```csharp
namespace PBA.Application.Features.Digests.Dtos;

public record DigestSummaryDto
{
    public Guid Id { get; init; }
    public DateOnly Date { get; init; }
    public string Title { get; init; } = string.Empty;
    public int ItemCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
```

- [ ] **Step 2: Write the failing query tests**

```csharp
using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Digests.Queries;
using PBA.Domain.Entities;
using PBA.Infrastructure.Data;

namespace PBA.Application.Tests.Features.Digests;

public class DigestQueriesTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task GetLatestDigest_ReturnsMostRecentWithItems()
    {
        using var db = NewDb();
        var idea = new Idea { Title = "Story", SourceName = "S", DeduplicationKey = "k" };
        db.Ideas.Add(idea);
        var older = new Digest { Date = new DateOnly(2026, 6, 1), Title = "Old", Intro = "i", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        var newer = new Digest { Date = new DateOnly(2026, 6, 4), Title = "New", Intro = "i", CreatedAt = DateTimeOffset.UtcNow };
        newer.Items.Add(new DigestItem { IdeaId = idea.Id, Rank = 1, Score = 9, WhyItMatters = "w" });
        db.Digests.AddRange(older, newer);
        await db.SaveChangesAsync();

        var handler = new GetLatestDigest.Handler(db);
        var result = await handler.Handle(new GetLatestDigest.Query(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("New", result.Value!.Title);
        Assert.Single(result.Value.Items);
        Assert.Equal("Story", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task ListDigests_ReturnsSummariesNewestFirst()
    {
        using var db = NewDb();
        db.Digests.AddRange(
            new Digest { Date = new DateOnly(2026, 6, 1), Title = "A", Intro = "i", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) },
            new Digest { Date = new DateOnly(2026, 6, 2), Title = "B", Intro = "i", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var handler = new ListDigests.Handler(db);
        var result = await handler.Handle(new ListDigests.Query(), default);

        Assert.Equal("B", result.Value![0].Title);
    }

    [Fact]
    public async Task GetDigest_UnknownId_ReturnsFailure()
    {
        using var db = NewDb();
        var handler = new GetDigest.Handler(db);
        var result = await handler.Handle(new GetDigest.Query(Guid.NewGuid()), default);
        Assert.False(result.IsSuccess);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test --filter FullyQualifiedName~DigestQueriesTests`
Expected: FAIL to compile.

- [ ] **Step 4: Implement the three queries**

Use `IAppDbContext` (confirm it exposes `Digests`/`DigestItems`; added in Task 10). Pattern mirrors `ListIdeas`/`GetIdea`. `GetLatestDigest.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Digests.Dtos;
using PBA.Domain.Common;

namespace PBA.Application.Features.Digests.Queries;

public static class GetLatestDigest
{
    public record Query : IRequest<Result<DigestDto>>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<DigestDto>>
    {
        public async Task<Result<DigestDto>> Handle(Query request, CancellationToken ct)
        {
            var digest = await db.Digests
                .AsNoTracking()
                .OrderByDescending(d => d.Date)
                .Select(Project())
                .FirstOrDefaultAsync(ct);

            return digest is null
                ? Result<DigestDto>.Fail("No digest available yet.")
                : digest;
        }

        internal static System.Linq.Expressions.Expression<Func<Domain.Entities.Digest, DigestDto>> Project() =>
            d => new DigestDto
            {
                Id = d.Id,
                Date = d.Date,
                Title = d.Title,
                Intro = d.Intro,
                ItemCount = d.ItemCount,
                CreatedAt = d.CreatedAt,
                Items = d.Items
                    .OrderBy(i => i.Rank)
                    .Select(i => new DigestItemDto
                    {
                        IdeaId = i.IdeaId,
                        Rank = i.Rank,
                        Score = i.Score,
                        WhyItMatters = i.WhyItMatters,
                        Title = i.Idea!.Title,
                        Url = i.Idea.Url
                    })
                    .ToList()
            };
    }
}
```

`GetDigest.cs` (same projection, by id):

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Digests.Dtos;
using PBA.Domain.Common;

namespace PBA.Application.Features.Digests.Queries;

public static class GetDigest
{
    public record Query(Guid Id) : IRequest<Result<DigestDto>>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<DigestDto>>
    {
        public async Task<Result<DigestDto>> Handle(Query request, CancellationToken ct)
        {
            var digest = await db.Digests
                .AsNoTracking()
                .Where(d => d.Id == request.Id)
                .Select(GetLatestDigest.Handler.Project())
                .FirstOrDefaultAsync(ct);

            return digest is null ? Result<DigestDto>.Fail("Digest not found.") : digest;
        }
    }
}
```

`ListDigests.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Digests.Dtos;
using PBA.Domain.Common;

namespace PBA.Application.Features.Digests.Queries;

public static class ListDigests
{
    public record Query : IRequest<Result<IReadOnlyList<DigestSummaryDto>>>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<IReadOnlyList<DigestSummaryDto>>>
    {
        public async Task<Result<IReadOnlyList<DigestSummaryDto>>> Handle(Query request, CancellationToken ct)
        {
            var items = await db.Digests
                .AsNoTracking()
                .OrderByDescending(d => d.Date)
                .Select(d => new DigestSummaryDto
                {
                    Id = d.Id,
                    Date = d.Date,
                    Title = d.Title,
                    ItemCount = d.ItemCount,
                    CreatedAt = d.CreatedAt
                })
                .ToListAsync(ct);

            return Result<IReadOnlyList<DigestSummaryDto>>.Success(items);
        }
    }
}
```

> **Note:** confirm the exact `Result<T>` factory names by reading an existing query (`ListIdeas` returns the DTO directly via implicit conversion; `GetIdea` shows the failure pattern). Match those names (e.g. `Result<T>.Fail` / implicit success) rather than guessing.

- [ ] **Step 5: Run tests, then commit**

Run: `dotnet test --filter FullyQualifiedName~DigestQueriesTests` (Expected: PASS).
```bash
git add src/PBA.Application/Features/Digests/ tests/PBA.Application.Tests/Features/Digests/DigestQueriesTests.cs
git commit -m "feat: add digest DTOs and query handlers"
```

---

## Task 17: Digest API endpoints + wire `ListIdeas` new params

**Files:**
- Create: `src/PBA.Api/Endpoints/DigestEndpoints.cs`
- Modify: `src/PBA.Api/Program.cs` (register `MapDigestEndpoints`)
- Modify: `src/PBA.Api/Endpoints/IdeaEndpoints.cs` (add `MinScore`/`IncludeDuplicates` query params)
- Test: `tests/PBA.Api.Tests/Endpoints/DigestEndpointsTests.cs`

- [ ] **Step 1: Write the failing endpoint test**

Mirror `IdeaEndpointsTests.cs` (uses `TestWebApplicationFactory`).

```csharp
using System.Net;
using System.Net.Http.Json;
using PBA.Application.Features.Digests.Dtos;

namespace PBA.Api.Tests.Endpoints;

public class DigestEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetLatest_NoDigest_Returns404OrProblem()
    {
        var response = await _client.GetAsync("/api/digests/latest");
        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListDigests_ReturnsOkArray()
    {
        var response = await _client.GetAsync("/api/digests");
        response.EnsureSuccessStatusCode();
        var items = await response.Content.ReadFromJsonAsync<List<DigestSummaryDto>>();
        Assert.NotNull(items);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter FullyQualifiedName~DigestEndpointsTests`
Expected: FAIL (404 route missing -> assertion or compile fail).

- [ ] **Step 3: Implement endpoints**

`DigestEndpoints.cs`:

```csharp
using MediatR;
using PBA.Api.Extensions;
using PBA.Application.Features.Digests.Queries;

namespace PBA.Api.Endpoints;

public static class DigestEndpoints
{
    public static void MapDigestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/digests").WithTags("Digests");

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
            (await sender.Send(new ListDigests.Query(), ct)).ToApiResult());

        group.MapGet("/latest", async (ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetLatestDigest.Query(), ct)).ToApiResult());

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetDigest.Query(id), ct)).ToApiResult());
    }
}
```

In `Program.cs`, next to the existing `app.MapIdeaEndpoints();` call, add:

```csharp
app.MapDigestEndpoints();
```

In `IdeaEndpoints.cs`, add to `ListIdeasQueryParams`:

```csharp
    public int? MinScore { get; init; }
    public bool? IncludeDuplicates { get; init; }
```

And in the query construction inside `MapGet("/")`:

```csharp
                MinScore = p.MinScore,
                IncludeDuplicates = p.IncludeDuplicates ?? false,
```

- [ ] **Step 4: Run tests; full backend verify**

Run: `dotnet test` (Expected: all PASS, including the new endpoint tests).

- [ ] **Step 5: Commit**

```bash
git add src/PBA.Api/Endpoints/DigestEndpoints.cs src/PBA.Api/Program.cs src/PBA.Api/Endpoints/IdeaEndpoints.cs tests/PBA.Api.Tests/Endpoints/DigestEndpointsTests.cs
git commit -m "feat: add digest API endpoints and ListIdeas score params"
```

---

## Task 18: Frontend - score fields on the Idea model + score badge on the card

**Files:**
- Modify: `src/PersonalBrandAssistant.Web/src/app/models/idea.model.ts`
- Modify: `src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-card/idea-card.component.ts`
- Modify: `src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-card/idea-card.component.spec.ts`

- [ ] **Step 1: Write the failing spec**

Add to `idea-card.component.spec.ts` (follow the file's existing harness for setting the `idea` input):

```typescript
  it('renders a score badge when score is present', () => {
    component.idea = { ...baseIdea, score: 8, scoreReason: 'Strong angle' };
    fixture.detectChanges();
    const badge = fixture.nativeElement.querySelector('[data-testid="idea-score-badge"]');
    expect(badge?.textContent).toContain('8');
  });

  it('does not render a score badge when score is null', () => {
    component.idea = { ...baseIdea, score: null };
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="idea-score-badge"]')).toBeNull();
  });
```

(`baseIdea` = whatever valid `Idea` the existing spec builds; add `score: null, scoreReason: null, isDuplicate: false` to it.)

- [ ] **Step 2: Run to verify failure**

Run: `cd src/PersonalBrandAssistant.Web && npm test -- --watch=false --browsers=ChromeHeadless --include='**/idea-card.component.spec.ts'`
Expected: FAIL (no badge element; type errors on `score`).

- [ ] **Step 3: Implement**

In `idea.model.ts`, add to the `Idea` interface:

```typescript
  score: number | null;
  scoreReason: string | null;
  isDuplicate: boolean;
```

In `idea-card.component.ts` template, add the badge near the title (match existing class/styling conventions in the file):

```html
@if (idea.score !== null) {
  <span class="idea-score-badge" data-testid="idea-score-badge" [title]="idea.scoreReason ?? ''">
    {{ idea.score }}/10
  </span>
}
```

Add a minimal style for `.idea-score-badge` consistent with the component's existing styles (small pill, color scaled by score is optional polish; a single accent color is fine for v1).

- [ ] **Step 4: Run tests to verify pass**

Run: `npm test -- --watch=false --browsers=ChromeHeadless --include='**/idea-card.component.spec.ts'`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PersonalBrandAssistant.Web/src/app/models/idea.model.ts src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-card/idea-card.component.ts src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-card/idea-card.component.spec.ts
git commit -m "feat: show AI score badge on idea cards"
```

---

## Task 19: Frontend - sort & filter by score

**Files:**
- Modify: `src/PersonalBrandAssistant.Web/src/app/core/services/idea.service.ts:22-47`
- Modify: `src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-filter-sidebar/idea-filter-sidebar.component.ts`
- Modify the matching `.spec.ts` for the sidebar
- Modify: `src/PersonalBrandAssistant.Web/src/app/models/idea.model.ts` (`IdeaFilterState`)

- [ ] **Step 1: Write the failing service spec**

In `idea.service.spec.ts`, add a test asserting `minScore` is sent as a query param:

```typescript
  it('includes minScore in list params when set', () => {
    service.list({ minScore: 6 } as any, 1, 20, { field: 'score', direction: 'desc' }).subscribe();
    const req = httpMock.expectOne((r) => r.url === '/api/ideas');
    expect(req.request.params.get('minScore')).toBe('6');
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  });
```

- [ ] **Step 2: Run to verify failure**

Run: `npm test -- --watch=false --browsers=ChromeHeadless --include='**/idea.service.spec.ts'`
Expected: FAIL.

- [ ] **Step 3: Implement**

In `idea.model.ts` add `minScore: number | null;` to `IdeaFilterState`.

In `idea.service.ts` `list(...)`, after the existing filter param assignments:

```typescript
    if (filter.minScore != null) params = params.set('minScore', filter.minScore.toString());
```

Add a "Sort by score" option to the sort control and a min-score selector to `idea-filter-sidebar.component.ts` (follow the existing controls' patterns; emit through the same output the sidebar already uses). Update the sidebar spec to assert the new control emits `minScore`.

- [ ] **Step 4: Run tests to verify pass**

Run: `npm test -- --watch=false --browsers=ChromeHeadless --include='**/idea.service.spec.ts' --include='**/idea-filter-sidebar.component.spec.ts'`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PersonalBrandAssistant.Web/src/app/core/services/idea.service.ts src/PersonalBrandAssistant.Web/src/app/models/idea.model.ts src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-filter-sidebar/
git commit -m "feat: sort and filter ideas by AI score"
```

---

## Task 20: Frontend - digest model + service

**Files:**
- Create: `src/PersonalBrandAssistant.Web/src/app/features/digest/models/digest.model.ts`
- Create: `src/PersonalBrandAssistant.Web/src/app/features/digest/services/digest.service.ts`
- Create: `src/PersonalBrandAssistant.Web/src/app/features/digest/services/digest.service.spec.ts`

- [ ] **Step 1: Create the model**

```typescript
export interface DigestItem {
  ideaId: string;
  rank: number;
  score: number;
  whyItMatters: string;
  title: string;
  url: string | null;
}

export interface Digest {
  id: string;
  date: string;
  title: string;
  intro: string;
  itemCount: number;
  createdAt: string;
  items: DigestItem[];
}

export interface DigestSummary {
  id: string;
  date: string;
  title: string;
  itemCount: number;
  createdAt: string;
}
```

- [ ] **Step 2: Write the failing service spec**

```typescript
import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { DigestService } from './digest.service';

describe('DigestService', () => {
  let service: DigestService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [DigestService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(DigestService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('fetches the latest digest', () => {
    service.getLatest().subscribe();
    const req = httpMock.expectOne('/api/digests/latest');
    expect(req.request.method).toBe('GET');
    req.flush({ id: '1', date: '2026-06-05', title: 't', intro: 'i', itemCount: 0, createdAt: '', items: [] });
  });

  it('lists digests', () => {
    service.list().subscribe();
    const req = httpMock.expectOne('/api/digests');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });
});
```

- [ ] **Step 3: Run to verify failure**

Run: `npm test -- --watch=false --browsers=ChromeHeadless --include='**/digest.service.spec.ts'`
Expected: FAIL (no service).

- [ ] **Step 4: Implement the service**

```typescript
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Digest, DigestSummary } from '../models/digest.model';

@Injectable({ providedIn: 'root' })
export class DigestService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/digests';

  getLatest(): Observable<Digest> {
    return this.http.get<Digest>(`${this.baseUrl}/latest`);
  }

  getById(id: string): Observable<Digest> {
    return this.http.get<Digest>(`${this.baseUrl}/${id}`);
  }

  list(): Observable<DigestSummary[]> {
    return this.http.get<DigestSummary[]>(this.baseUrl);
  }
}
```

- [ ] **Step 5: Run tests, then commit**

Run: `npm test -- --watch=false --browsers=ChromeHeadless --include='**/digest.service.spec.ts'` (Expected: PASS).
```bash
git add src/PersonalBrandAssistant.Web/src/app/features/digest/
git commit -m "feat: add digest model and service"
```

---

## Task 21: Frontend - Daily Brief page + route

**Files:**
- Create: `src/PersonalBrandAssistant.Web/src/app/features/digest/pages/daily-brief/daily-brief.component.ts`
- Create: `src/PersonalBrandAssistant.Web/src/app/features/digest/pages/daily-brief/daily-brief.component.spec.ts`
- Create: `src/PersonalBrandAssistant.Web/src/app/features/digest/digest.routes.ts`
- Modify: `src/PersonalBrandAssistant.Web/src/app/app.routes.ts` (lazy-load `digest.routes`, mirror the existing `ideas`/`news` route entries)
- Modify: `src/PersonalBrandAssistant.Web/src/app/shell/sidebar/sidebar.component.ts` (add a "Daily Brief" nav link next to the Ideas/Feed links)

- [ ] **Step 1: Write the failing component spec**

```typescript
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { DailyBriefComponent } from './daily-brief.component';
import { DigestService } from '../../services/digest.service';

describe('DailyBriefComponent', () => {
  let fixture: ComponentFixture<DailyBriefComponent>;
  const digest = {
    id: '1', date: '2026-06-05', title: 'Daily Brief', intro: 'Today in AI.',
    itemCount: 1, createdAt: '',
    items: [{ ideaId: 'a', rank: 1, score: 9, whyItMatters: 'Big.', title: 'Story A', url: 'http://a' }],
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DailyBriefComponent],
      providers: [{ provide: DigestService, useValue: { getLatest: () => of(digest) } }],
    }).compileComponents();
    fixture = TestBed.createComponent(DailyBriefComponent);
    fixture.detectChanges();
  });

  it('renders the digest intro and ranked items', () => {
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Today in AI.');
    expect(text).toContain('Story A');
    expect(text).toContain('Big.');
  });
});
```

- [ ] **Step 2: Run to verify failure**

Run: `npm test -- --watch=false --browsers=ChromeHeadless --include='**/daily-brief.component.spec.ts'`
Expected: FAIL (no component).

- [ ] **Step 3: Implement the component**

```typescript
import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DigestService } from '../../services/digest.service';
import { Digest } from '../../models/digest.model';

@Component({
  selector: 'app-daily-brief',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (digest(); as d) {
      <article class="daily-brief">
        <header>
          <h1>{{ d.title }}</h1>
          <p class="intro">{{ d.intro }}</p>
        </header>
        <ol class="brief-items">
          @for (item of d.items; track item.ideaId) {
            <li>
              <span class="rank">{{ item.rank }}</span>
              <span class="score">{{ item.score }}/10</span>
              @if (item.url) {
                <a [href]="item.url" target="_blank" rel="noopener">{{ item.title }}</a>
              } @else {
                <span>{{ item.title }}</span>
              }
              <p class="why">{{ item.whyItMatters }}</p>
            </li>
          }
        </ol>
      </article>
    } @else {
      <p class="empty">No daily brief yet. Check back after the next run.</p>
    }
  `,
})
export class DailyBriefComponent implements OnInit {
  private readonly service = inject(DigestService);
  readonly digest = signal<Digest | null>(null);

  ngOnInit(): void {
    this.service.getLatest().subscribe({
      next: (d) => this.digest.set(d),
      error: () => this.digest.set(null),
    });
  }
}
```

- [ ] **Step 4: Add the route + nav link**

`digest.routes.ts`:

```typescript
import { Routes } from '@angular/router';
import { DailyBriefComponent } from './pages/daily-brief/daily-brief.component';

export const digestRoutes: Routes = [{ path: '', component: DailyBriefComponent }];
```

In `src/PersonalBrandAssistant.Web/src/app/app.routes.ts` (mirror how `ideas`/`news` routes are lazy-loaded), add:

```typescript
  { path: 'daily-brief', loadChildren: () => import('./features/digest/digest.routes').then((m) => m.digestRoutes) },
```

In `src/PersonalBrandAssistant.Web/src/app/shell/sidebar/sidebar.component.ts`, add a "Daily Brief" nav link pointing to `/daily-brief` next to the Ideas/Feed links (match the existing link markup in that component).

- [ ] **Step 5: Run tests; full frontend verify; commit**

Run: `npm test -- --watch=false --browsers=ChromeHeadless` (Expected: all PASS).
Run: `npm run build` (Expected: build succeeds).
```bash
git add src/PersonalBrandAssistant.Web/src/app/features/digest/
git add <the modified app routes file> <the modified nav component>
git commit -m "feat: add Daily Brief page and route"
```

---

## Final verification (after all tasks)

- [ ] `dotnet build` succeeds.
- [ ] `dotnet test` all green (new radar + digest tests included).
- [ ] `cd src/PersonalBrandAssistant.Web && npm test -- --watch=false --browsers=ChromeHeadless` all green.
- [ ] `npm run build` succeeds.
- [ ] EF migrations apply cleanly to a fresh DB: `dotnet ef database update --project src/PBA.Infrastructure --startup-project src/PBA.Api`.
- [ ] Manual smoke (per the verify-before-done rule): with `IdeaScoring:BackfillEnabled=false`, run the API, confirm new RSS ideas get a `Score` within a sweep, confirm a digest row + `/api/digests/latest` returns it, confirm the Daily Brief page renders.
- [ ] Only after confirming cost on a small batch: set `IdeaScoring:BackfillEnabled=true` to drain the 3,831 historical ideas.

## Configuration reference (appsettings)

```json
{
  "IdeaScoring": { "IntervalMinutes": 10, "BatchSize": 20, "ThrottleMs": 1000, "Model": "google/gemini-2.5-flash", "BackfillEnabled": false },
  "Clustering": { "IntervalMinutes": 30, "MinScore": 6, "LookbackHours": 48, "MaxItemsPerSweep": 40, "Model": "google/gemini-2.5-flash" },
  "Digest": { "RunAtLocalTime": "07:00", "TopN": 8, "LookbackHours": 24, "Model": "google/gemini-2.5-pro" }
}
```
