using System.Net;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Api;

[Collection("Postgres")]
public class SwaggerTests
{
    private readonly PostgresFixture _fixture;

    public SwaggerTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Swagger_InDevelopment_ReturnsOk()
    {
        var connStr = _fixture.GetUniqueConnectionString();
        await using var factory = new CustomWebApplicationFactory(connStr);
        await factory.EnsureDatabaseCreatedAsync();

        var client = factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_InProduction_ReturnsNotFound()
    {
        var connStr = _fixture.GetUniqueConnectionString();
        await using var factory = new CustomWebApplicationFactory(connStr, environment: "Production");
        await factory.EnsureDatabaseCreatedAsync();

        var client = factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
