using System.Net;
using System.Text.Json;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Api;

public class GlobalExceptionHandlerTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private CustomWebApplicationFactory _factory = null!;
    private string _connectionString = null!;

    public GlobalExceptionHandlerTests(PostgresFixture fixture)
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
    public async Task UnhandledException_Returns500ProblemDetailsWithNoStackTrace()
    {
        using var client = _factory.CreateAuthenticatedClient();

        var content = new StringContent("not json at all", System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/content", content);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal("Internal Server Error", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("An unexpected error occurred.", json.RootElement.GetProperty("detail").GetString());
        // Ensure no stack trace leaked
        Assert.False(json.RootElement.TryGetProperty("stackTrace", out _));
    }
}
