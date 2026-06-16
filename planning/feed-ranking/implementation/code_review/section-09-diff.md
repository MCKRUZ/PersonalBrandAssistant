diff --git a/src/PBA.Api/Endpoints/BrandRankingProfileEndpoints.cs b/src/PBA.Api/Endpoints/BrandRankingProfileEndpoints.cs
new file mode 100644
index 0000000..d1b88b8
--- /dev/null
+++ b/src/PBA.Api/Endpoints/BrandRankingProfileEndpoints.cs
@@ -0,0 +1,22 @@
+using MediatR;
+using PBA.Api.Extensions;
+using PBA.Application.Features.BrandRankingProfile.Commands;
+using PBA.Application.Features.BrandRankingProfile.Queries;
+
+namespace PBA.Api.Endpoints;
+
+public static class BrandRankingProfileEndpoints
+{
+    public static void MapBrandRankingProfileEndpoints(this IEndpointRouteBuilder app)
+    {
+        // Distinct route from the existing voice BrandProfile; no endpoint auth in v2.
+        var group = app.MapGroup("/api/brand-ranking-profile").WithTags("BrandRankingProfile");
+
+        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
+            (await sender.Send(new GetActiveBrandRankingProfile.Query(), ct)).ToApiResult());
+
+        // The command is the request body; validation + the server-enforced write mode run in the handler.
+        group.MapPut("/", async (UpdateBrandRankingProfile.Command body, ISender sender, CancellationToken ct) =>
+            (await sender.Send(body, ct)).ToApiResult());
+    }
+}
diff --git a/src/PBA.Api/Program.cs b/src/PBA.Api/Program.cs
index d84ef61..00e7aab 100644
--- a/src/PBA.Api/Program.cs
+++ b/src/PBA.Api/Program.cs
@@ -67,6 +67,7 @@ app.MapPlatformEndpoints();
 app.MapFeedEndpoints();
 app.MapAnalyticsEndpoints();
 app.MapDigestEndpoints();
+app.MapBrandRankingProfileEndpoints();
 
 app.MapHub<ContentHub>("/hubs/content");
 app.MapHub<FeedHub>("/hubs/feed");
diff --git a/src/PBA.Application/Common/Interfaces/IAppDbContext.cs b/src/PBA.Application/Common/Interfaces/IAppDbContext.cs
index ba8f00c..dfd8c99 100644
--- a/src/PBA.Application/Common/Interfaces/IAppDbContext.cs
+++ b/src/PBA.Application/Common/Interfaces/IAppDbContext.cs
@@ -1,4 +1,5 @@
 using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.ChangeTracking;
 using PBA.Domain.Entities;
 
 namespace PBA.Application.Common.Interfaces;
@@ -17,4 +18,8 @@ public interface IAppDbContext
     DbSet<Digest> Digests { get; }
     DbSet<DigestItem> DigestItems { get; }
     Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
+
+    /// <summary>Change-tracker entry for an attached entity. Used to set an optimistic-concurrency token's
+    /// original value (e.g. the xmin token, whose setter is private). Satisfied by <c>DbContext.Entry</c>.</summary>
+    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
 }
diff --git a/src/PBA.Application/Features/BrandRankingProfile/Commands/UpdateBrandRankingProfile.cs b/src/PBA.Application/Features/BrandRankingProfile/Commands/UpdateBrandRankingProfile.cs
new file mode 100644
index 0000000..26026d7
--- /dev/null
+++ b/src/PBA.Application/Features/BrandRankingProfile/Commands/UpdateBrandRankingProfile.cs
@@ -0,0 +1,149 @@
+using MediatR;
+using Microsoft.EntityFrameworkCore;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Features.BrandRankingProfile.Dtos;
+using PBA.Domain.Common;
+using DomainBrandPillar = PBA.Domain.Entities.BrandPillar;
+using DomainBrandRankingProfile = PBA.Domain.Entities.BrandRankingProfile;
+
+namespace PBA.Application.Features.BrandRankingProfile.Commands;
+
+/// <summary>
+/// Updates the active brand ranking profile. The write mode is decided SERVER-SIDE (R-H4): query-time
+/// knobs (weights, half-life, floor, multipliers, pillar order) always apply and never bump
+/// <c>Version</c>; a definition change (positioning, audience, topics, voice markers, or any pillar
+/// add/remove/rename/description edit — detected via the entity's own <c>RequiresVersionBump</c>) bumps
+/// <c>Version</c> so the scoring sweep (section-07) re-scores stale ideas. There is no client "mode" flag,
+/// so a definition change can never be smuggled in as a non-bumping weights update.
+/// </summary>
+public static class UpdateBrandRankingProfile
+{
+    public sealed record Command : IRequest<Result<BrandRankingProfileDto>>
+    {
+        public string Positioning { get; init; } = "";
+        public string AudiencePrimary { get; init; } = "";
+        public string? AudienceSecondary { get; init; }
+        public double HalfLifeDays { get; init; }
+        public double DecayFloor { get; init; }
+        public double AntiTopicMultiplier { get; init; }
+        public double AuthorityBoost { get; init; }
+        public IReadOnlyList<PillarInput> Pillars { get; init; } = [];
+        public IReadOnlyList<string> AuthorityTopics { get; init; } = [];
+        public IReadOnlyList<string> AntiTopics { get; init; } = [];
+        public IReadOnlyList<string> VoiceMarkers { get; init; } = [];
+        public string ConcurrencyToken { get; init; } = "";
+    }
+
+    public sealed record PillarInput(Guid Id, string Name, string Description, double Weight, int Order);
+
+    public sealed class Handler(IAppDbContext db) : IRequestHandler<Command, Result<BrandRankingProfileDto>>
+    {
+        public async Task<Result<BrandRankingProfileDto>> Handle(Command request, CancellationToken cancellationToken)
+        {
+            var profile = await db.BrandRankingProfiles
+                .Include(p => p.Pillars)
+                .FirstOrDefaultAsync(p => p.IsActive, cancellationToken);
+
+            if (profile is null)
+                return Result<BrandRankingProfileDto>.NotFound("No active brand ranking profile");
+
+            // Force EF to compare against the client's token: a stale token -> 0 rows updated ->
+            // DbUpdateConcurrencyException (R-H3). The Xmin setter is private, so set the ORIGINAL value
+            // through the change tracker rather than the property.
+            if (uint.TryParse(request.ConcurrencyToken, out var token))
+                db.Entry(profile).Property(p => p.Xmin).OriginalValue = token;
+
+            // Decide the mode from the actual diff BEFORE mutating anything (consume section-02's logic).
+            var requiresBump = profile.RequiresVersionBump(BuildProposed(request));
+
+            // Query-time knobs always apply, no bump.
+            profile.HalfLifeDays = request.HalfLifeDays;
+            profile.DecayFloor = request.DecayFloor;
+            profile.AntiTopicMultiplier = request.AntiTopicMultiplier;
+            profile.AuthorityBoost = request.AuthorityBoost;
+
+            if (requiresBump)
+            {
+                profile.Positioning = request.Positioning;
+                profile.AudiencePrimary = request.AudiencePrimary;
+                profile.AudienceSecondary = request.AudienceSecondary;
+                profile.AuthorityTopics = request.AuthorityTopics.ToList();
+                profile.AntiTopics = request.AntiTopics.ToList();
+                profile.VoiceMarkers = request.VoiceMarkers.ToList();
+                ApplyPillarDefinitions(profile, request.Pillars);
+                profile.Version += 1;
+            }
+            else
+            {
+                // Weights-only: touch ONLY pillar weight/order, never name/description/topics (R-H4).
+                foreach (var input in request.Pillars)
+                {
+                    var existing = profile.Pillars.FirstOrDefault(p => p.Id == input.Id);
+                    if (existing is null) continue;
+                    existing.Weight = input.Weight;
+                    existing.Order = input.Order;
+                }
+            }
+
+            profile.UpdatedAt = DateTimeOffset.UtcNow;
+
+            try
+            {
+                await db.SaveChangesAsync(cancellationToken);
+            }
+            catch (DbUpdateConcurrencyException)
+            {
+                return Result<BrandRankingProfileDto>.Conflict(
+                    "The brand ranking profile was modified by someone else. Reload and retry.");
+            }
+
+            return BrandRankingProfileMapping.ToDto(profile);
+        }
+
+        // A detached projection of the request used only to ask the entity whether a definition changed.
+        // Weight/Order are query-time and don't affect the decision, so they are left at defaults here.
+        private static DomainBrandRankingProfile BuildProposed(Command request) => new()
+        {
+            Positioning = request.Positioning,
+            AudiencePrimary = request.AudiencePrimary,
+            AudienceSecondary = request.AudienceSecondary,
+            AuthorityTopics = request.AuthorityTopics.ToList(),
+            AntiTopics = request.AntiTopics.ToList(),
+            VoiceMarkers = request.VoiceMarkers.ToList(),
+            Pillars = request.Pillars
+                .Select(p => new DomainBrandPillar { Id = p.Id, Name = p.Name, Description = p.Description })
+                .ToList()
+        };
+
+        private static void ApplyPillarDefinitions(DomainBrandRankingProfile profile, IReadOnlyList<PillarInput> incoming)
+        {
+            var incomingIds = incoming.Select(p => p.Id).ToHashSet();
+            profile.Pillars.RemoveAll(p => !incomingIds.Contains(p.Id));
+
+            foreach (var input in incoming)
+            {
+                var existing = profile.Pillars.FirstOrDefault(p => p.Id == input.Id);
+                if (existing is null)
+                {
+                    profile.Pillars.Add(new DomainBrandPillar
+                    {
+                        Id = input.Id == Guid.Empty ? Guid.NewGuid() : input.Id,
+                        Name = input.Name, Description = input.Description,
+                        Weight = input.Weight, Order = input.Order
+                        // DescriptionEmbedding stays null -> the embedding service embeds it next sweep.
+                    });
+                    continue;
+                }
+
+                // A description edit invalidates the stored pillar vector; null it so section-06 re-embeds.
+                if (existing.Description != input.Description)
+                    existing.DescriptionEmbedding = null;
+
+                existing.Name = input.Name;
+                existing.Description = input.Description;
+                existing.Weight = input.Weight;
+                existing.Order = input.Order;
+            }
+        }
+    }
+}
diff --git a/src/PBA.Application/Features/BrandRankingProfile/Commands/UpdateBrandRankingProfileValidator.cs b/src/PBA.Application/Features/BrandRankingProfile/Commands/UpdateBrandRankingProfileValidator.cs
new file mode 100644
index 0000000..766b911
--- /dev/null
+++ b/src/PBA.Application/Features/BrandRankingProfile/Commands/UpdateBrandRankingProfileValidator.cs
@@ -0,0 +1,26 @@
+using FluentValidation;
+
+namespace PBA.Application.Features.BrandRankingProfile.Commands;
+
+public class UpdateBrandRankingProfileValidator : AbstractValidator<UpdateBrandRankingProfile.Command>
+{
+    public UpdateBrandRankingProfileValidator()
+    {
+        RuleFor(x => x.Positioning).NotEmpty();
+        RuleFor(x => x.AudiencePrimary).NotEmpty();
+        RuleFor(x => x.HalfLifeDays).GreaterThan(0);
+        RuleFor(x => x.DecayFloor).InclusiveBetween(0, 1);
+        RuleFor(x => x.AntiTopicMultiplier).GreaterThan(0);
+        RuleFor(x => x.AuthorityBoost).GreaterThan(0);
+
+        RuleFor(x => x.Pillars).NotEmpty().WithMessage("At least one pillar is required.");
+        RuleForEach(x => x.Pillars).ChildRules(p =>
+        {
+            p.RuleFor(pillar => pillar.Weight).InclusiveBetween(0, 1);
+            p.RuleFor(pillar => pillar.Name).NotEmpty();
+        });
+
+        // Deliberately NOT validating that weights sum to ~1.0: read-time renormalization (section-08,
+        // R-C2b) makes an exact-sum constraint unnecessary and brittle.
+    }
+}
diff --git a/src/PBA.Application/Features/BrandRankingProfile/Dtos/BrandRankingProfileDto.cs b/src/PBA.Application/Features/BrandRankingProfile/Dtos/BrandRankingProfileDto.cs
new file mode 100644
index 0000000..9876421
--- /dev/null
+++ b/src/PBA.Application/Features/BrandRankingProfile/Dtos/BrandRankingProfileDto.cs
@@ -0,0 +1,63 @@
+using DomainBrandRankingProfile = PBA.Domain.Entities.BrandRankingProfile;
+
+namespace PBA.Application.Features.BrandRankingProfile.Dtos;
+
+public sealed record BrandRankingProfileDto
+{
+    public Guid Id { get; init; }
+    public int Version { get; init; }
+    public string Positioning { get; init; } = "";
+    public string AudiencePrimary { get; init; } = "";
+    public string? AudienceSecondary { get; init; }
+    public double HalfLifeDays { get; init; }
+    public double DecayFloor { get; init; }
+    public double AntiTopicMultiplier { get; init; }
+    public double AuthorityBoost { get; init; }
+    public IReadOnlyList<BrandRankingPillarDto> Pillars { get; init; } = [];
+    public IReadOnlyList<string> AuthorityTopics { get; init; } = [];
+    public IReadOnlyList<string> AntiTopics { get; init; } = [];
+    public IReadOnlyList<string> VoiceMarkers { get; init; } = [];
+    public DateTimeOffset UpdatedAt { get; init; }
+
+    // Optimistic-concurrency token (Npgsql xmin, a uint) round-tripped to PUT; serialized as a string.
+    public string ConcurrencyToken { get; init; } = "";
+}
+
+public sealed record BrandRankingPillarDto
+{
+    public Guid Id { get; init; }   // identity (R-C3); survives renames. DescriptionEmbedding never exposed.
+    public string Name { get; init; } = "";
+    public string Description { get; init; } = "";
+    public double Weight { get; init; }
+    public int Order { get; init; }
+}
+
+public static class BrandRankingProfileMapping
+{
+    /// <summary>Projects the aggregate to its DTO, pillars ordered by <c>Order</c>, with the xmin token
+    /// stringified into <see cref="BrandRankingProfileDto.ConcurrencyToken"/>. Never exposes pillar embeddings.</summary>
+    public static BrandRankingProfileDto ToDto(DomainBrandRankingProfile profile) => new()
+    {
+        Id = profile.Id,
+        Version = profile.Version,
+        Positioning = profile.Positioning,
+        AudiencePrimary = profile.AudiencePrimary,
+        AudienceSecondary = profile.AudienceSecondary,
+        HalfLifeDays = profile.HalfLifeDays,
+        DecayFloor = profile.DecayFloor,
+        AntiTopicMultiplier = profile.AntiTopicMultiplier,
+        AuthorityBoost = profile.AuthorityBoost,
+        Pillars = profile.Pillars
+            .OrderBy(p => p.Order)
+            .Select(p => new BrandRankingPillarDto
+            {
+                Id = p.Id, Name = p.Name, Description = p.Description, Weight = p.Weight, Order = p.Order
+            })
+            .ToList(),
+        AuthorityTopics = profile.AuthorityTopics.ToList(),
+        AntiTopics = profile.AntiTopics.ToList(),
+        VoiceMarkers = profile.VoiceMarkers.ToList(),
+        UpdatedAt = profile.UpdatedAt,
+        ConcurrencyToken = profile.Xmin.ToString()
+    };
+}
diff --git a/src/PBA.Application/Features/BrandRankingProfile/Queries/GetActiveBrandRankingProfile.cs b/src/PBA.Application/Features/BrandRankingProfile/Queries/GetActiveBrandRankingProfile.cs
new file mode 100644
index 0000000..5a7f820
--- /dev/null
+++ b/src/PBA.Application/Features/BrandRankingProfile/Queries/GetActiveBrandRankingProfile.cs
@@ -0,0 +1,28 @@
+using MediatR;
+using Microsoft.EntityFrameworkCore;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Features.BrandRankingProfile.Dtos;
+using PBA.Domain.Common;
+
+namespace PBA.Application.Features.BrandRankingProfile.Queries;
+
+public static class GetActiveBrandRankingProfile
+{
+    public record Query : IRequest<Result<BrandRankingProfileDto>>;
+
+    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<BrandRankingProfileDto>>
+    {
+        public async Task<Result<BrandRankingProfileDto>> Handle(Query request, CancellationToken cancellationToken)
+        {
+            var profile = await db.BrandRankingProfiles
+                .AsNoTracking()
+                .Include(p => p.Pillars)
+                .FirstOrDefaultAsync(p => p.IsActive, cancellationToken);
+
+            if (profile is null)
+                return Result<BrandRankingProfileDto>.NotFound("No active brand ranking profile");
+
+            return BrandRankingProfileMapping.ToDto(profile);
+        }
+    }
+}
diff --git a/tests/PBA.Api.Tests/Endpoints/BrandRankingProfileEndpointsTests.cs b/tests/PBA.Api.Tests/Endpoints/BrandRankingProfileEndpointsTests.cs
new file mode 100644
index 0000000..57c038d
--- /dev/null
+++ b/tests/PBA.Api.Tests/Endpoints/BrandRankingProfileEndpointsTests.cs
@@ -0,0 +1,85 @@
+using System.Net;
+using System.Net.Http.Json;
+using System.Text.Json;
+using Microsoft.Extensions.DependencyInjection;
+using PBA.Infrastructure.Data;
+using BrandPillar = PBA.Domain.Entities.BrandPillar;
+using BrandRankingProfileEntity = PBA.Domain.Entities.BrandRankingProfile;
+using Xunit;
+
+namespace PBA.Api.Tests.Endpoints;
+
+public class BrandRankingProfileEndpointsTests : IClassFixture<TestWebApplicationFactory>
+{
+    private readonly TestWebApplicationFactory _factory;
+
+    public BrandRankingProfileEndpointsTests(TestWebApplicationFactory factory) => _factory = factory;
+
+    // Fresh pillar id per seed: the factory's InMemory DB is shared across the class and does not
+    // cascade-delete child pillars when the profile is removed, so a fixed id would collide on re-seed.
+    private async Task<(HttpClient client, Guid pillarId)> SeedClientAsync()
+    {
+        var client = _factory.CreateClient();
+        var pillarId = Guid.NewGuid();
+        using var scope = _factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
+        db.Set<BrandPillar>().RemoveRange(db.Set<BrandPillar>());
+        db.BrandRankingProfiles.RemoveRange(db.BrandRankingProfiles);
+        await db.SaveChangesAsync();
+        db.BrandRankingProfiles.Add(new BrandRankingProfileEntity
+        {
+            Version = 1, IsActive = true, UpdatedAt = DateTimeOffset.UtcNow,
+            Positioning = "Pos", AudiencePrimary = "Aud",
+            HalfLifeDays = 7, DecayFloor = 0.075, AntiTopicMultiplier = 0.1, AuthorityBoost = 1.2,
+            AuthorityTopics = ["auth1"], AntiTopics = ["anti1"], VoiceMarkers = ["vm1"],
+            Pillars = [new BrandPillar { Id = pillarId, Name = "P1", Description = "D1", Weight = 0.6, Order = 0 }]
+        });
+        await db.SaveChangesAsync();
+        return (client, pillarId);
+    }
+
+    private static object WeightsOnlyBody(Guid pillarId, double weight) => new
+    {
+        positioning = "Pos", audiencePrimary = "Aud",
+        halfLifeDays = 7.0, decayFloor = 0.075, antiTopicMultiplier = 0.1, authorityBoost = 1.2,
+        pillars = new[] { new { id = pillarId, name = "P1", description = "D1", weight, order = 0 } },
+        authorityTopics = new[] { "auth1" }, antiTopics = new[] { "anti1" }, voiceMarkers = new[] { "vm1" },
+        concurrencyToken = "0"
+    };
+
+    [Fact]
+    public async Task Get_ReturnsActiveProfile()
+    {
+        var (client, _) = await SeedClientAsync();
+
+        var response = await client.GetAsync("/api/brand-ranking-profile");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
+        Assert.Equal(1, json.GetProperty("version").GetInt32());
+        Assert.Equal(1, json.GetProperty("pillars").GetArrayLength());
+    }
+
+    [Fact]
+    public async Task Put_WeightsOnly_Returns200_VersionUnchanged()
+    {
+        var (client, pillarId) = await SeedClientAsync();
+
+        var response = await client.PutAsJsonAsync("/api/brand-ranking-profile", WeightsOnlyBody(pillarId, 0.9));
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
+        Assert.Equal(1, json.GetProperty("version").GetInt32()); // no bump
+        Assert.Equal(0.9, json.GetProperty("pillars")[0].GetProperty("weight").GetDouble(), 6);
+    }
+
+    [Fact]
+    public async Task Put_InvalidWeight_Returns400()
+    {
+        var (client, pillarId) = await SeedClientAsync();
+
+        var response = await client.PutAsJsonAsync("/api/brand-ranking-profile", WeightsOnlyBody(pillarId, 1.5));
+
+        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
+    }
+}
diff --git a/tests/PBA.Application.Tests/Features/BrandRankingProfile/GetActiveBrandRankingProfileHandlerTests.cs b/tests/PBA.Application.Tests/Features/BrandRankingProfile/GetActiveBrandRankingProfileHandlerTests.cs
new file mode 100644
index 0000000..dcceac0
--- /dev/null
+++ b/tests/PBA.Application.Tests/Features/BrandRankingProfile/GetActiveBrandRankingProfileHandlerTests.cs
@@ -0,0 +1,73 @@
+using Microsoft.EntityFrameworkCore;
+using PBA.Application.Features.BrandRankingProfile.Queries;
+using PBA.Domain.Common;
+using PBA.Infrastructure.Data;
+using BrandPillar = PBA.Domain.Entities.BrandPillar;
+using BrandRankingProfileEntity = PBA.Domain.Entities.BrandRankingProfile;
+using Xunit;
+
+namespace PBA.Application.Tests.Features.BrandRankingProfileApi;
+
+public class GetActiveBrandRankingProfileHandlerTests
+{
+    private static ApplicationDbContext CreateContext() =>
+        new(new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
+
+    private static BrandRankingProfileEntity ActiveProfile() => new()
+    {
+        Version = 3, IsActive = true, UpdatedAt = DateTimeOffset.UtcNow,
+        Positioning = "Pos", AudiencePrimary = "Aud", AudienceSecondary = "Sec",
+        HalfLifeDays = 7, DecayFloor = 0.075, AntiTopicMultiplier = 0.1, AuthorityBoost = 1.2,
+        AuthorityTopics = ["auth1"], AntiTopics = ["anti1"], VoiceMarkers = ["vm1"],
+        Pillars =
+        [
+            new BrandPillar { Name = "Second", Description = "d2", Weight = 0.4, Order = 1 },
+            new BrandPillar { Name = "First", Description = "d1", Weight = 0.6, Order = 0 },
+        ]
+    };
+
+    [Fact]
+    public async Task Handle_ActiveProfilePresent_ReturnsDtoWithPillarsOrdered()
+    {
+        await using var db = CreateContext();
+        db.BrandRankingProfiles.Add(ActiveProfile());
+        await db.SaveChangesAsync();
+
+        var result = await new GetActiveBrandRankingProfile.Handler(db)
+            .Handle(new GetActiveBrandRankingProfile.Query(), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        var dto = result.Value!;
+        Assert.Equal(3, dto.Version);
+        Assert.Equal(["First", "Second"], dto.Pillars.Select(p => p.Name)); // ordered by Order
+        Assert.Equal(["auth1"], dto.AuthorityTopics);
+        Assert.Equal(["anti1"], dto.AntiTopics);
+        Assert.Equal(["vm1"], dto.VoiceMarkers);
+    }
+
+    [Fact]
+    public async Task Handle_NoActiveProfile_ReturnsNotFound()
+    {
+        await using var db = CreateContext();
+
+        var result = await new GetActiveBrandRankingProfile.Handler(db)
+            .Handle(new GetActiveBrandRankingProfile.Query(), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
+    }
+
+    [Fact]
+    public async Task Handle_ReturnsConcurrencyToken()
+    {
+        await using var db = CreateContext();
+        db.BrandRankingProfiles.Add(ActiveProfile());
+        await db.SaveChangesAsync();
+
+        var result = await new GetActiveBrandRankingProfile.Handler(db)
+            .Handle(new GetActiveBrandRankingProfile.Query(), CancellationToken.None);
+
+        Assert.NotNull(result.Value!.ConcurrencyToken); // xmin stringified (0 under InMemory)
+    }
+}
diff --git a/tests/PBA.Application.Tests/Features/BrandRankingProfile/UpdateBrandRankingProfileHandlerTests.cs b/tests/PBA.Application.Tests/Features/BrandRankingProfile/UpdateBrandRankingProfileHandlerTests.cs
new file mode 100644
index 0000000..f7f41c7
--- /dev/null
+++ b/tests/PBA.Application.Tests/Features/BrandRankingProfile/UpdateBrandRankingProfileHandlerTests.cs
@@ -0,0 +1,180 @@
+using Microsoft.EntityFrameworkCore;
+using PBA.Application.Features.BrandRankingProfile.Commands;
+using PBA.Domain.Common;
+using PBA.Infrastructure.Data;
+using BrandPillar = PBA.Domain.Entities.BrandPillar;
+using BrandRankingProfileEntity = PBA.Domain.Entities.BrandRankingProfile;
+using Xunit;
+
+namespace PBA.Application.Tests.Features.BrandRankingProfileApi;
+
+public class UpdateBrandRankingProfileHandlerTests
+{
+    private static readonly Guid P1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
+
+    private static ApplicationDbContext CreateContext(string name) =>
+        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options);
+
+    // SaveChangesAsync always throws a concurrency exception — to exercise the handler's catch->Conflict
+    // path, which InMemory cannot trigger via the real xmin token.
+    private sealed class ConcurrencyThrowingContext(DbContextOptions<ApplicationDbContext> o)
+        : ApplicationDbContext(o)
+    {
+        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
+            => throw new DbUpdateConcurrencyException("stale token");
+    }
+
+    private static BrandRankingProfileEntity Profile(int version = 1) => new()
+    {
+        Version = version, IsActive = true, UpdatedAt = DateTimeOffset.UtcNow,
+        Positioning = "Pos", AudiencePrimary = "Aud", AudienceSecondary = "Sec",
+        HalfLifeDays = 7, DecayFloor = 0.075, AntiTopicMultiplier = 0.1, AuthorityBoost = 1.2,
+        AuthorityTopics = ["auth1"], AntiTopics = ["anti1"], VoiceMarkers = ["vm1"],
+        Pillars = [new BrandPillar { Id = P1, Name = "P1", Description = "D1", Weight = 0.6, Order = 0,
+            DescriptionEmbedding = new float[] { 1, 0 } }]
+    };
+
+    // A command that mirrors the profile (no definition change) unless mutated by the caller.
+    private static UpdateBrandRankingProfile.Command CommandFrom(
+        BrandRankingProfileEntity p,
+        IReadOnlyList<UpdateBrandRankingProfile.PillarInput>? pillars = null,
+        string? positioning = null,
+        IReadOnlyList<string>? authorityTopics = null,
+        string token = "0") => new()
+    {
+        Positioning = positioning ?? p.Positioning,
+        AudiencePrimary = p.AudiencePrimary,
+        AudienceSecondary = p.AudienceSecondary,
+        HalfLifeDays = p.HalfLifeDays, DecayFloor = p.DecayFloor,
+        AntiTopicMultiplier = p.AntiTopicMultiplier, AuthorityBoost = p.AuthorityBoost,
+        Pillars = pillars ?? p.Pillars
+            .Select(pl => new UpdateBrandRankingProfile.PillarInput(pl.Id, pl.Name, pl.Description, pl.Weight, pl.Order))
+            .ToList(),
+        AuthorityTopics = authorityTopics ?? p.AuthorityTopics.ToList(),
+        AntiTopics = p.AntiTopics.ToList(),
+        VoiceMarkers = p.VoiceMarkers.ToList(),
+        ConcurrencyToken = token
+    };
+
+    [Fact]
+    public async Task Handle_WeightsOnly_AppliesWeightsAndKnobs_NoVersionBump()
+    {
+        await using var db = CreateContext(Guid.NewGuid().ToString());
+        db.BrandRankingProfiles.Add(Profile());
+        await db.SaveChangesAsync();
+
+        var cmd = CommandFrom(Profile(),
+            pillars: [new UpdateBrandRankingProfile.PillarInput(P1, "P1", "D1", 0.9, 0)]) // weight 0.6 -> 0.9
+            with { HalfLifeDays = 14 };
+
+        var result = await new UpdateBrandRankingProfile.Handler(db).Handle(cmd, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        var saved = db.BrandRankingProfiles.Include(p => p.Pillars).Single();
+        Assert.Equal(1, saved.Version);          // no bump
+        Assert.Equal(0.9, saved.Pillars.Single().Weight);
+        Assert.Equal(14, saved.HalfLifeDays);
+    }
+
+    [Fact]
+    public async Task Handle_WeightsOnly_DoesNotAlterPillarNameOrDescription()
+    {
+        await using var db = CreateContext(Guid.NewGuid().ToString());
+        db.BrandRankingProfiles.Add(Profile());
+        await db.SaveChangesAsync();
+
+        // Same definition (name/description) but different weight -> weights-only path.
+        var cmd = CommandFrom(Profile(),
+            pillars: [new UpdateBrandRankingProfile.PillarInput(P1, "P1", "D1", 0.3, 0)]);
+
+        await new UpdateBrandRankingProfile.Handler(db).Handle(cmd, CancellationToken.None);
+
+        var pillar = db.BrandRankingProfiles.Include(p => p.Pillars).Single().Pillars.Single();
+        Assert.Equal("P1", pillar.Name);
+        Assert.Equal("D1", pillar.Description);
+        Assert.NotNull(pillar.DescriptionEmbedding); // untouched on the weights path
+    }
+
+    [Fact]
+    public async Task Handle_TopicChange_BumpsVersion()
+    {
+        await using var db = CreateContext(Guid.NewGuid().ToString());
+        db.BrandRankingProfiles.Add(Profile(version: 5));
+        await db.SaveChangesAsync();
+
+        var cmd = CommandFrom(Profile(version: 5), authorityTopics: ["auth1", "NEW"]);
+
+        var result = await new UpdateBrandRankingProfile.Handler(db).Handle(cmd, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(6, result.Value!.Version);
+        Assert.Equal(["auth1", "NEW"], db.BrandRankingProfiles.Single().AuthorityTopics);
+    }
+
+    [Fact]
+    public async Task Handle_PillarDescriptionChange_BumpsVersion_AndNullsEmbedding()
+    {
+        await using var db = CreateContext(Guid.NewGuid().ToString());
+        db.BrandRankingProfiles.Add(Profile());
+        await db.SaveChangesAsync();
+
+        var cmd = CommandFrom(Profile(),
+            pillars: [new UpdateBrandRankingProfile.PillarInput(P1, "P1", "D1-CHANGED", 0.6, 0)]);
+
+        var result = await new UpdateBrandRankingProfile.Handler(db).Handle(cmd, CancellationToken.None);
+
+        Assert.Equal(2, result.Value!.Version);
+        var pillar = db.BrandRankingProfiles.Include(p => p.Pillars).Single().Pillars.Single();
+        Assert.Equal("D1-CHANGED", pillar.Description);
+        Assert.Null(pillar.DescriptionEmbedding); // nulled so section-06 re-embeds
+    }
+
+    [Fact]
+    public async Task Handle_NameChangeMixedWithWeightChange_StillBumps_NoSmuggling()
+    {
+        await using var db = CreateContext(Guid.NewGuid().ToString());
+        db.BrandRankingProfiles.Add(Profile());
+        await db.SaveChangesAsync();
+
+        // A definition change (rename) cannot be slipped through as a non-bumping weights update (R-H4).
+        var cmd = CommandFrom(Profile(),
+            pillars: [new UpdateBrandRankingProfile.PillarInput(P1, "P1-RENAMED", "D1", 0.9, 0)]);
+
+        var result = await new UpdateBrandRankingProfile.Handler(db).Handle(cmd, CancellationToken.None);
+
+        Assert.Equal(2, result.Value!.Version);
+        var pillar = db.BrandRankingProfiles.Include(p => p.Pillars).Single().Pillars.Single();
+        Assert.Equal("P1-RENAMED", pillar.Name);
+        Assert.Equal(0.9, pillar.Weight);
+    }
+
+    [Fact]
+    public async Task Handle_NoActiveProfile_ReturnsNotFound()
+    {
+        await using var db = CreateContext(Guid.NewGuid().ToString());
+
+        var result = await new UpdateBrandRankingProfile.Handler(db)
+            .Handle(CommandFrom(Profile()), CancellationToken.None);
+
+        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
+    }
+
+    [Fact]
+    public async Task Handle_ConcurrencyConflict_ReturnsConflict()
+    {
+        var name = Guid.NewGuid().ToString();
+        await using (var seed = CreateContext(name))
+        {
+            seed.BrandRankingProfiles.Add(Profile());
+            await seed.SaveChangesAsync();
+        }
+
+        await using var throwing = new ConcurrencyThrowingContext(
+            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options);
+
+        var result = await new UpdateBrandRankingProfile.Handler(throwing)
+            .Handle(CommandFrom(Profile()), CancellationToken.None);
+
+        Assert.Equal(ResultFailureType.Conflict, result.FailureType);
+    }
+}
diff --git a/tests/PBA.Application.Tests/Features/BrandRankingProfile/UpdateBrandRankingProfileValidatorTests.cs b/tests/PBA.Application.Tests/Features/BrandRankingProfile/UpdateBrandRankingProfileValidatorTests.cs
new file mode 100644
index 0000000..bc57f1b
--- /dev/null
+++ b/tests/PBA.Application.Tests/Features/BrandRankingProfile/UpdateBrandRankingProfileValidatorTests.cs
@@ -0,0 +1,73 @@
+using PBA.Application.Features.BrandRankingProfile.Commands;
+using Xunit;
+
+namespace PBA.Application.Tests.Features.BrandRankingProfileApi;
+
+public class UpdateBrandRankingProfileValidatorTests
+{
+    private static readonly UpdateBrandRankingProfileValidator Validator = new();
+
+    private static UpdateBrandRankingProfile.Command Valid() => new()
+    {
+        Positioning = "Pos", AudiencePrimary = "Aud",
+        HalfLifeDays = 7, DecayFloor = 0.075, AntiTopicMultiplier = 0.1, AuthorityBoost = 1.2,
+        Pillars = [new UpdateBrandRankingProfile.PillarInput(Guid.NewGuid(), "P1", "D1", 0.5, 0)],
+        ConcurrencyToken = "0"
+    };
+
+    [Fact]
+    public void Valid_Command_Passes() => Assert.True(Validator.Validate(Valid()).IsValid);
+
+    [Fact]
+    public void PillarWeight_OutOfRange_Fails()
+    {
+        var cmd = Valid() with { Pillars = [new UpdateBrandRankingProfile.PillarInput(Guid.NewGuid(), "P", "D", 1.5, 0)] };
+        Assert.False(Validator.Validate(cmd).IsValid);
+    }
+
+    [Fact]
+    public void NoPillars_Fails()
+    {
+        var cmd = Valid() with { Pillars = [] };
+        Assert.False(Validator.Validate(cmd).IsValid);
+    }
+
+    [Fact]
+    public void EmptyPositioning_Fails()
+    {
+        var cmd = Valid() with { Positioning = "" };
+        Assert.False(Validator.Validate(cmd).IsValid);
+    }
+
+    [Fact]
+    public void EmptyAudiencePrimary_Fails()
+    {
+        var cmd = Valid() with { AudiencePrimary = "" };
+        Assert.False(Validator.Validate(cmd).IsValid);
+    }
+
+    [Theory]
+    [InlineData(0)]
+    [InlineData(-1)]
+    public void HalfLifeDays_NotPositive_Fails(double halfLife)
+    {
+        var cmd = Valid() with { HalfLifeDays = halfLife };
+        Assert.False(Validator.Validate(cmd).IsValid);
+    }
+
+    [Theory]
+    [InlineData(-0.1)]
+    [InlineData(1.1)]
+    public void DecayFloor_OutOfRange_Fails(double floor)
+    {
+        var cmd = Valid() with { DecayFloor = floor };
+        Assert.False(Validator.Validate(cmd).IsValid);
+    }
+
+    [Fact]
+    public void NonPositiveMultipliers_Fail()
+    {
+        Assert.False(Validator.Validate(Valid() with { AntiTopicMultiplier = 0 }).IsValid);
+        Assert.False(Validator.Validate(Valid() with { AuthorityBoost = 0 }).IsValid);
+    }
+}
