using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Tests.Api;

public class AgentEndpointsTests : IClassFixture<AgentEndpointsTests.AgentTestFactory>
{
    private readonly AgentTestFactory _factory;
    private static readonly Mock<IAgentOrchestrator> _orchestratorMock = new();
    private static readonly Mock<ITokenTracker> _tokenTrackerMock = new();

    public AgentEndpointsTests(AgentTestFactory factory)
    {
        _factory = factory;
        _orchestratorMock.Reset();
        _tokenTrackerMock.Reset();
    }

    private HttpClient CreateClient() => _factory.CreateAuthenticatedClient();

    // --- POST /api/agents/execute ---

    [Fact]
    public async Task Execute_ReturnsAccepted_WithExecutionId()
    {
        var executionId = Guid.NewGuid();
        _orchestratorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<AgentTask>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AgentExecutionResult>.Success(
                new AgentExecutionResult(executionId, AgentExecutionStatus.Completed, null, null)));

        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/agents/execute", new
        {
            Type = AgentCapabilityType.Writer,
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(executionId.ToString(), json.RootElement.GetProperty("executionId").GetString());
    }

    [Fact]
    public async Task Execute_WithWaitTrue_ReturnsFullResult()
    {
        var executionId = Guid.NewGuid();
        var output = new AgentOutput { GeneratedText = "Generated text", CreatesContent = false };
        _orchestratorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<AgentTask>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AgentExecutionResult>.Success(
                new AgentExecutionResult(executionId, AgentExecutionStatus.Completed, output, null)));

        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/agents/execute?wait=true", new
        {
            Type = AgentCapabilityType.Writer,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Execute_BudgetExceeded_Returns400()
    {
        _orchestratorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<AgentTask>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AgentExecutionResult>.Failure(
                ErrorCode.ValidationFailed, "Budget exceeded"));

        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/agents/execute", new
        {
            Type = AgentCapabilityType.Writer,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- GET /api/agents/executions/{id} ---

    [Fact]
    public async Task GetExecution_ReturnsExecution()
    {
        var execution = AgentExecution.Create(AgentCapabilityType.Writer, ModelTier.Standard);
        _orchestratorMock
            .Setup(x => x.GetExecutionStatusAsync(execution.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AgentExecution>.Success(execution));

        using var client = CreateClient();
        var response = await client.GetAsync($"/api/agents/executions/{execution.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetExecution_Returns404_ForUnknownId()
    {
        var unknownId = Guid.NewGuid();
        _orchestratorMock
            .Setup(x => x.GetExecutionStatusAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AgentExecution>.NotFound("Not found"));

        using var client = CreateClient();
        var response = await client.GetAsync($"/api/agents/executions/{unknownId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- GET /api/agents/executions ---

    [Fact]
    public async Task ListExecutions_ReturnsArray()
    {
        _orchestratorMock
            .Setup(x => x.ListExecutionsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AgentExecution[]>.Success([]));

        using var client = CreateClient();
        var response = await client.GetAsync("/api/agents/executions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListExecutions_FiltersBy_ContentId()
    {
        var contentId = Guid.NewGuid();
        _orchestratorMock
            .Setup(x => x.ListExecutionsAsync(contentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AgentExecution[]>.Success([]));

        using var client = CreateClient();
        var response = await client.GetAsync($"/api/agents/executions?contentId={contentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _orchestratorMock.Verify(
            x => x.ListExecutionsAsync(contentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- GET /api/agents/usage ---

    [Fact]
    public async Task GetUsage_ReturnsUsageSummary()
    {
        _tokenTrackerMock
            .Setup(x => x.GetCostForPeriodAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5.50m);

        using var client = CreateClient();
        var response = await client.GetAsync("/api/agents/usage");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(5.50m, json.RootElement.GetProperty("totalCost").GetDecimal());
    }

    // --- GET /api/agents/budget ---

    [Fact]
    public async Task GetBudget_ReturnsBudgetStatus()
    {
        _tokenTrackerMock
            .Setup(x => x.GetBudgetRemainingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(7.50m);
        _tokenTrackerMock
            .Setup(x => x.IsOverBudgetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        using var client = CreateClient();
        var response = await client.GetAsync("/api/agents/budget");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(7.50m, json.RootElement.GetProperty("budgetRemaining").GetDecimal());
        Assert.False(json.RootElement.GetProperty("isOverBudget").GetBoolean());
    }

    // --- POST /api/agents/stream ---

    [Fact]
    public async Task Stream_ReturnsEventStream_ContentType()
    {
        var executionId = Guid.NewGuid();
        _orchestratorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<AgentTask>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AgentExecutionResult>.Success(
                new AgentExecutionResult(executionId, AgentExecutionStatus.Completed, null, null)));

        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/agents/stream", new
        {
            Type = AgentCapabilityType.Writer,
        });

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Stream_EmitsCompleteEvent_OnSuccess()
    {
        var executionId = Guid.NewGuid();
        _orchestratorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<AgentTask>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AgentExecutionResult>.Success(
                new AgentExecutionResult(executionId, AgentExecutionStatus.Completed, null, null)));

        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/agents/stream", new
        {
            Type = AgentCapabilityType.Writer,
        });

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"type\":\"complete\"", body);
        Assert.Contains(executionId.ToString(), body);
    }

    [Fact]
    public async Task Stream_EmitsErrorEvent_OnFailure()
    {
        _orchestratorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<AgentTask>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AgentExecutionResult>.Failure(
                ErrorCode.ValidationFailed, "Budget exceeded"));

        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/agents/stream", new
        {
            Type = AgentCapabilityType.Writer,
        });

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"type\":\"error\"", body);
        Assert.Contains("Budget exceeded", body);
    }

    // --- Test Factory ---

    public class AgentTestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiKey", "test-api-key-12345");
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Host=localhost;Database=test_agents;Username=test;Password=test");

            builder.ConfigureTestServices(services =>
            {
                // Remove background services
                var hostedServices = services
                    .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService))
                    .ToList();
                foreach (var svc in hostedServices)
                    services.Remove(svc);

                // Mock orchestrator and token tracker
                services.AddScoped<IAgentOrchestrator>(_ => _orchestratorMock.Object);
                services.AddScoped<ITokenTracker>(_ => _tokenTrackerMock.Object);
            });
        }

        public HttpClient CreateAuthenticatedClient()
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key-12345");
            return client;
        }
    }
}
