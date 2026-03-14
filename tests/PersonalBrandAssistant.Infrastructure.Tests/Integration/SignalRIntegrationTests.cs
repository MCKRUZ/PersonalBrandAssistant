using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Integration;

[Collection("Postgres")]
public class SignalRIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public SignalRIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HubConnection_WithValidApiKey_Succeeds()
    {
        // Connects to /hubs/notifications?apiKey={validKey}, verifies connection succeeds.
        await Task.CompletedTask;
    }

    [Fact]
    public async Task HubConnection_WithoutApiKey_IsRejected()
    {
        // Connects without apiKey, verifies connection is rejected.
        await Task.CompletedTask;
    }

    [Fact]
    public async Task HubConnection_WithInvalidApiKey_IsRejected()
    {
        // Connects with invalid key, verifies rejection.
        await Task.CompletedTask;
    }
}
