using System.Net;
using System.Text.Json;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Api;

public class ApiKeyMiddlewareTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private CustomWebApplicationFactory _factory = null!;
    private string _connectionString = null!;

    public ApiKeyMiddlewareTests(PostgresFixture fixture)
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
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await using var conn = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var dbName = new Npgsql.NpgsqlConnectionStringBuilder(_connectionString).Database;
        await using var cmd = new Npgsql.NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task ValidApiKey_ReturnsSuccess()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InvalidApiKey_Returns401()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");

        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Unauthorized", json.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task MissingApiKey_Returns401()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthLiveness_ExemptFromApiKey()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
