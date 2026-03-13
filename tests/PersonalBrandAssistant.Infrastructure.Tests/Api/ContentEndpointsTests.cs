using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Api;

public class ContentEndpointsTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private CustomWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private string _connectionString = null!;

    public ContentEndpointsTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = _fixture.GetUniqueConnectionString();
        await using var conn = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var dbName = new Npgsql.NpgsqlConnectionStringBuilder(_connectionString).Database;
        await using var cmd = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
        await cmd.ExecuteNonQueryAsync();

        _factory = new CustomWebApplicationFactory(_connectionString);
        await _factory.EnsureDatabaseCreatedAsync();
        _client = _factory.CreateAuthenticatedClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await using var conn = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var dbName = new Npgsql.NpgsqlConnectionStringBuilder(_connectionString).Database;
        await using var cmd = new Npgsql.NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task CreateContent_ValidBody_Returns201()
    {
        var request = new
        {
            ContentType = (int)ContentType.BlogPost,
            Body = "Test blog post content",
            Title = "Test Title",
        };

        var response = await _client.PostAsJsonAsync("/api/content", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task CreateContent_MissingBody_Returns400()
    {
        var request = new
        {
            ContentType = (int)ContentType.BlogPost,
            Body = "",
        };

        var response = await _client.PostAsJsonAsync("/api/content", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetContent_ExistingId_Returns200()
    {
        var createRequest = new
        {
            ContentType = (int)ContentType.SocialPost,
            Body = "Test social post",
        };
        var createResponse = await _client.PostAsJsonAsync("/api/content", createRequest);
        var contentId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        var response = await _client.GetAsync($"/api/content/{contentId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetContent_NonExistentId_Returns404()
    {
        var response = await _client.GetAsync($"/api/content/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListContent_ReturnsPagedResult()
    {
        var request = new
        {
            ContentType = (int)ContentType.BlogPost,
            Body = "List test content",
        };
        await _client.PostAsJsonAsync("/api/content", request);

        var response = await _client.GetAsync("/api/content");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.TryGetProperty("items", out _));
    }

    [Fact]
    public async Task DeleteContent_Returns204()
    {
        var createRequest = new
        {
            ContentType = (int)ContentType.SocialPost,
            Body = "Content to delete",
        };
        var createResponse = await _client.PostAsJsonAsync("/api/content", createRequest);
        var contentId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        var response = await _client.DeleteAsync($"/api/content/{contentId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
