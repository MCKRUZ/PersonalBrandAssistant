using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PBA.Infrastructure.Data;
using BrandPillar = PBA.Domain.Entities.BrandPillar;
using BrandRankingProfileEntity = PBA.Domain.Entities.BrandRankingProfile;
using Xunit;

namespace PBA.Api.Tests.Endpoints;

public class BrandRankingProfileEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BrandRankingProfileEndpointsTests(TestWebApplicationFactory factory) => _factory = factory;

    // Fresh pillar id per seed: the factory's InMemory DB is shared across the class and does not
    // cascade-delete child pillars when the profile is removed, so a fixed id would collide on re-seed.
    private async Task<(HttpClient client, Guid pillarId)> SeedClientAsync()
    {
        var client = _factory.CreateClient();
        var pillarId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Set<BrandPillar>().RemoveRange(db.Set<BrandPillar>());
        db.BrandRankingProfiles.RemoveRange(db.BrandRankingProfiles);
        await db.SaveChangesAsync();
        db.BrandRankingProfiles.Add(new BrandRankingProfileEntity
        {
            Version = 1, IsActive = true, UpdatedAt = DateTimeOffset.UtcNow,
            Positioning = "Pos", AudiencePrimary = "Aud",
            HalfLifeDays = 7, DecayFloor = 0.075, AntiTopicMultiplier = 0.1, AuthorityBoost = 1.2,
            AuthorityTopics = ["auth1"], AntiTopics = ["anti1"], VoiceMarkers = ["vm1"],
            Pillars = [new BrandPillar { Id = pillarId, Name = "P1", Description = "D1", Weight = 0.6, Order = 0 }]
        });
        await db.SaveChangesAsync();
        return (client, pillarId);
    }

    private static object WeightsOnlyBody(Guid pillarId, double weight) => new
    {
        positioning = "Pos", audiencePrimary = "Aud",
        halfLifeDays = 7.0, decayFloor = 0.075, antiTopicMultiplier = 0.1, authorityBoost = 1.2,
        pillars = new[] { new { id = pillarId, name = "P1", description = "D1", weight, order = 0 } },
        authorityTopics = new[] { "auth1" }, antiTopics = new[] { "anti1" }, voiceMarkers = new[] { "vm1" },
        concurrencyToken = "0"
    };

    [Fact]
    public async Task Get_ReturnsActiveProfile()
    {
        var (client, _) = await SeedClientAsync();

        var response = await client.GetAsync("/api/brand-ranking-profile");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("version").GetInt32());
        Assert.Equal(1, json.GetProperty("pillars").GetArrayLength());
    }

    [Fact]
    public async Task Put_WeightsOnly_Returns200_VersionUnchanged()
    {
        var (client, pillarId) = await SeedClientAsync();

        var response = await client.PutAsJsonAsync("/api/brand-ranking-profile", WeightsOnlyBody(pillarId, 0.9));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("version").GetInt32()); // no bump
        Assert.Equal(0.9, json.GetProperty("pillars")[0].GetProperty("weight").GetDouble(), 6);
    }

    [Fact]
    public async Task Put_InvalidWeight_Returns400()
    {
        var (client, pillarId) = await SeedClientAsync();

        var response = await client.PutAsJsonAsync("/api/brand-ranking-profile", WeightsOnlyBody(pillarId, 1.5));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_MissingConcurrencyToken_Returns400()
    {
        var (client, pillarId) = await SeedClientAsync();
        var body = WeightsOnlyBody(pillarId, 0.5);
        var withBlankToken = new
        {
            positioning = "Pos", audiencePrimary = "Aud",
            halfLifeDays = 7.0, decayFloor = 0.075, antiTopicMultiplier = 0.1, authorityBoost = 1.2,
            pillars = new[] { new { id = pillarId, name = "P1", description = "D1", weight = 0.5, order = 0 } },
            authorityTopics = new[] { "auth1" }, antiTopics = new[] { "anti1" }, voiceMarkers = new[] { "vm1" },
            concurrencyToken = ""
        };

        var response = await client.PutAsJsonAsync("/api/brand-ranking-profile", withBlankToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); // fail closed (R-H3)
    }
}
