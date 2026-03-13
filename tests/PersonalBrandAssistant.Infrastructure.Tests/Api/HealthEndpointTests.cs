using System.Net;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Api;

public class HealthEndpointTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private CustomWebApplicationFactory _factory = null!;
    private string _connectionString = null!;

    public HealthEndpointTests(PostgresFixture fixture)
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
    public async Task HealthLiveness_Returns200()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_WithApiKey_Returns200()
    {
        using var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
