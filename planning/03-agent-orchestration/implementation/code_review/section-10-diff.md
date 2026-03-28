diff --git a/planning/03-agent-orchestration/implementation/deep_implement_config.json b/planning/03-agent-orchestration/implementation/deep_implement_config.json
index b7a0026..ebbb6da 100644
--- a/planning/03-agent-orchestration/implementation/deep_implement_config.json
+++ b/planning/03-agent-orchestration/implementation/deep_implement_config.json
@@ -51,6 +51,10 @@
     "section-08-agent-capabilities": {
       "status": "complete",
       "commit_hash": "df02842"
+    },
+    "section-09-orchestrator": {
+      "status": "complete",
+      "commit_hash": "d921aea"
     }
   },
   "pre_commit": {
diff --git a/src/PersonalBrandAssistant.Api/Endpoints/AgentEndpoints.cs b/src/PersonalBrandAssistant.Api/Endpoints/AgentEndpoints.cs
new file mode 100644
index 0000000..8218a24
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/Endpoints/AgentEndpoints.cs
@@ -0,0 +1,179 @@
+using System.Text.Json;
+using PersonalBrandAssistant.Api.Extensions;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Api.Endpoints;
+
+public static class AgentEndpoints
+{
+    private static readonly JsonSerializerOptions _jsonOptions = new()
+    {
+        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
+    };
+
+    public static void MapAgentEndpoints(this IEndpointRouteBuilder app)
+    {
+        var group = app.MapGroup("/api/agents").WithTags("Agents");
+
+        group.MapPost("/stream", StreamExecution);
+        group.MapPost("/execute", ExecuteAgent);
+        group.MapGet("/executions/{id:guid}", GetExecution);
+        group.MapGet("/executions", ListExecutions);
+        group.MapGet("/usage", GetUsage);
+        group.MapGet("/budget", GetBudget);
+    }
+
+    private static async Task StreamExecution(
+        HttpContext httpContext,
+        IAgentOrchestrator orchestrator,
+        AgentExecuteRequest request)
+    {
+        httpContext.Response.ContentType = "text/event-stream";
+        httpContext.Response.Headers.CacheControl = "no-store";
+        httpContext.Response.Headers["X-Accel-Buffering"] = "no";
+
+        var ct = httpContext.RequestAborted;
+        var writer = httpContext.Response.BodyWriter;
+
+        try
+        {
+            await WriteSseEventAsync(httpContext, new { type = "status", status = "running" });
+
+            var task = new AgentTask(request.Type, request.ContentId, request.Parameters ?? new());
+            var result = await orchestrator.ExecuteAsync(task, ct);
+
+            if (result.IsSuccess)
+            {
+                var output = result.Value!;
+                if (output.Output is not null)
+                {
+                    await WriteSseEventAsync(httpContext, new
+                    {
+                        type = "token",
+                        text = output.Output.GeneratedText,
+                    });
+
+                    await WriteSseEventAsync(httpContext, new
+                    {
+                        type = "usage",
+                        inputTokens = output.Output.InputTokens,
+                        outputTokens = output.Output.OutputTokens,
+                    });
+                }
+
+                await WriteSseEventAsync(httpContext, new
+                {
+                    type = "complete",
+                    executionId = output.ExecutionId,
+                    createdContentId = output.CreatedContentId,
+                });
+            }
+            else
+            {
+                await WriteSseEventAsync(httpContext, new
+                {
+                    type = "error",
+                    message = string.Join("; ", result.Errors),
+                });
+            }
+        }
+        catch (OperationCanceledException)
+        {
+            // Client disconnected — no action needed
+        }
+        catch (Exception)
+        {
+            try
+            {
+                await WriteSseEventAsync(httpContext, new
+                {
+                    type = "error",
+                    message = "An unexpected error occurred.",
+                });
+            }
+            catch
+            {
+                // Response may already be closed
+            }
+        }
+    }
+
+    private static async Task<IResult> ExecuteAgent(
+        IAgentOrchestrator orchestrator,
+        AgentExecuteRequest request,
+        bool wait = false,
+        CancellationToken ct = default)
+    {
+        var task = new AgentTask(request.Type, request.ContentId, request.Parameters ?? new());
+        var result = await orchestrator.ExecuteAsync(task, ct);
+
+        if (!result.IsSuccess)
+            return result.ToHttpResult();
+
+        if (wait)
+            return Results.Ok(result.Value);
+
+        return Results.Accepted(
+            $"/api/agents/executions/{result.Value!.ExecutionId}",
+            new { executionId = result.Value.ExecutionId });
+    }
+
+    private static async Task<IResult> GetExecution(
+        IAgentOrchestrator orchestrator,
+        Guid id,
+        CancellationToken ct)
+    {
+        var result = await orchestrator.GetExecutionStatusAsync(id, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> ListExecutions(
+        IAgentOrchestrator orchestrator,
+        Guid? contentId = null,
+        CancellationToken ct = default)
+    {
+        var result = await orchestrator.ListExecutionsAsync(contentId, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> GetUsage(
+        ITokenTracker tokenTracker,
+        DateTimeOffset? from = null,
+        DateTimeOffset? to = null,
+        CancellationToken ct = default)
+    {
+        var fromDate = from ?? DateTimeOffset.UtcNow.Date;
+        var toDate = to ?? DateTimeOffset.UtcNow;
+
+        var cost = await tokenTracker.GetCostForPeriodAsync(fromDate, toDate, ct);
+        return Results.Ok(new { from = fromDate, to = toDate, totalCost = cost });
+    }
+
+    private static async Task<IResult> GetBudget(
+        ITokenTracker tokenTracker,
+        CancellationToken ct)
+    {
+        var remaining = await tokenTracker.GetBudgetRemainingAsync(ct);
+        var isOverBudget = await tokenTracker.IsOverBudgetAsync(ct);
+        return Results.Ok(new
+        {
+            budgetRemaining = remaining,
+            isOverBudget,
+        });
+    }
+
+    private static async Task WriteSseEventAsync(HttpContext context, object data)
+    {
+        var json = JsonSerializer.Serialize(data, _jsonOptions);
+        var bytes = System.Text.Encoding.UTF8.GetBytes($"data: {json}\n\n");
+        await context.Response.Body.WriteAsync(bytes);
+        await context.Response.Body.FlushAsync();
+    }
+}
+
+public record AgentExecuteRequest(
+    AgentCapabilityType Type,
+    Guid? ContentId,
+    Dictionary<string, string>? Parameters);
diff --git a/src/PersonalBrandAssistant.Api/Program.cs b/src/PersonalBrandAssistant.Api/Program.cs
index b728ada..cf1b25c 100644
--- a/src/PersonalBrandAssistant.Api/Program.cs
+++ b/src/PersonalBrandAssistant.Api/Program.cs
@@ -57,6 +57,7 @@ app.MapWorkflowEndpoints();
 app.MapApprovalEndpoints();
 app.MapSchedulingEndpoints();
 app.MapNotificationEndpoints();
+app.MapAgentEndpoints();
 
 app.Run();
 
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AgentEndpointsTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AgentEndpointsTests.cs
new file mode 100644
index 0000000..7f2a1c8
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AgentEndpointsTests.cs
@@ -0,0 +1,285 @@
+using System.Net;
+using System.Net.Http.Json;
+using System.Text.Json;
+using Microsoft.AspNetCore.Hosting;
+using Microsoft.AspNetCore.Mvc.Testing;
+using Microsoft.AspNetCore.TestHost;
+using Microsoft.Extensions.DependencyInjection;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Api;
+
+public class AgentEndpointsTests : IClassFixture<AgentEndpointsTests.AgentTestFactory>
+{
+    private readonly AgentTestFactory _factory;
+    private static readonly Mock<IAgentOrchestrator> _orchestratorMock = new();
+    private static readonly Mock<ITokenTracker> _tokenTrackerMock = new();
+
+    public AgentEndpointsTests(AgentTestFactory factory)
+    {
+        _factory = factory;
+        _orchestratorMock.Reset();
+        _tokenTrackerMock.Reset();
+    }
+
+    private HttpClient CreateClient() => _factory.CreateAuthenticatedClient();
+
+    // --- POST /api/agents/execute ---
+
+    [Fact]
+    public async Task Execute_ReturnsAccepted_WithExecutionId()
+    {
+        var executionId = Guid.NewGuid();
+        _orchestratorMock
+            .Setup(x => x.ExecuteAsync(It.IsAny<AgentTask>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<AgentExecutionResult>.Success(
+                new AgentExecutionResult(executionId, AgentExecutionStatus.Completed, null, null)));
+
+        using var client = CreateClient();
+        var response = await client.PostAsJsonAsync("/api/agents/execute", new
+        {
+            Type = AgentCapabilityType.Writer,
+        });
+
+        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
+        var body = await response.Content.ReadAsStringAsync();
+        using var json = JsonDocument.Parse(body);
+        Assert.Equal(executionId.ToString(), json.RootElement.GetProperty("executionId").GetString());
+    }
+
+    [Fact]
+    public async Task Execute_WithWaitTrue_ReturnsFullResult()
+    {
+        var executionId = Guid.NewGuid();
+        var output = new AgentOutput { GeneratedText = "Generated text", CreatesContent = false };
+        _orchestratorMock
+            .Setup(x => x.ExecuteAsync(It.IsAny<AgentTask>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<AgentExecutionResult>.Success(
+                new AgentExecutionResult(executionId, AgentExecutionStatus.Completed, output, null)));
+
+        using var client = CreateClient();
+        var response = await client.PostAsJsonAsync("/api/agents/execute?wait=true", new
+        {
+            Type = AgentCapabilityType.Writer,
+        });
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task Execute_BudgetExceeded_Returns400()
+    {
+        _orchestratorMock
+            .Setup(x => x.ExecuteAsync(It.IsAny<AgentTask>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<AgentExecutionResult>.Failure(
+                ErrorCode.ValidationFailed, "Budget exceeded"));
+
+        using var client = CreateClient();
+        var response = await client.PostAsJsonAsync("/api/agents/execute", new
+        {
+            Type = AgentCapabilityType.Writer,
+        });
+
+        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
+    }
+
+    // --- GET /api/agents/executions/{id} ---
+
+    [Fact]
+    public async Task GetExecution_ReturnsExecution()
+    {
+        var execution = AgentExecution.Create(AgentCapabilityType.Writer, ModelTier.Standard);
+        _orchestratorMock
+            .Setup(x => x.GetExecutionStatusAsync(execution.Id, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<AgentExecution>.Success(execution));
+
+        using var client = CreateClient();
+        var response = await client.GetAsync($"/api/agents/executions/{execution.Id}");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task GetExecution_Returns404_ForUnknownId()
+    {
+        var unknownId = Guid.NewGuid();
+        _orchestratorMock
+            .Setup(x => x.GetExecutionStatusAsync(unknownId, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<AgentExecution>.NotFound("Not found"));
+
+        using var client = CreateClient();
+        var response = await client.GetAsync($"/api/agents/executions/{unknownId}");
+
+        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
+    }
+
+    // --- GET /api/agents/executions ---
+
+    [Fact]
+    public async Task ListExecutions_ReturnsArray()
+    {
+        _orchestratorMock
+            .Setup(x => x.ListExecutionsAsync(null, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<AgentExecution[]>.Success([]));
+
+        using var client = CreateClient();
+        var response = await client.GetAsync("/api/agents/executions");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task ListExecutions_FiltersBy_ContentId()
+    {
+        var contentId = Guid.NewGuid();
+        _orchestratorMock
+            .Setup(x => x.ListExecutionsAsync(contentId, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<AgentExecution[]>.Success([]));
+
+        using var client = CreateClient();
+        var response = await client.GetAsync($"/api/agents/executions?contentId={contentId}");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        _orchestratorMock.Verify(
+            x => x.ListExecutionsAsync(contentId, It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    // --- GET /api/agents/usage ---
+
+    [Fact]
+    public async Task GetUsage_ReturnsUsageSummary()
+    {
+        _tokenTrackerMock
+            .Setup(x => x.GetCostForPeriodAsync(
+                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(5.50m);
+
+        using var client = CreateClient();
+        var response = await client.GetAsync("/api/agents/usage");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        var body = await response.Content.ReadAsStringAsync();
+        using var json = JsonDocument.Parse(body);
+        Assert.Equal(5.50m, json.RootElement.GetProperty("totalCost").GetDecimal());
+    }
+
+    // --- GET /api/agents/budget ---
+
+    [Fact]
+    public async Task GetBudget_ReturnsBudgetStatus()
+    {
+        _tokenTrackerMock
+            .Setup(x => x.GetBudgetRemainingAsync(It.IsAny<CancellationToken>()))
+            .ReturnsAsync(7.50m);
+        _tokenTrackerMock
+            .Setup(x => x.IsOverBudgetAsync(It.IsAny<CancellationToken>()))
+            .ReturnsAsync(false);
+
+        using var client = CreateClient();
+        var response = await client.GetAsync("/api/agents/budget");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        var body = await response.Content.ReadAsStringAsync();
+        using var json = JsonDocument.Parse(body);
+        Assert.Equal(7.50m, json.RootElement.GetProperty("budgetRemaining").GetDecimal());
+        Assert.False(json.RootElement.GetProperty("isOverBudget").GetBoolean());
+    }
+
+    // --- POST /api/agents/stream ---
+
+    [Fact]
+    public async Task Stream_ReturnsEventStream_ContentType()
+    {
+        var executionId = Guid.NewGuid();
+        _orchestratorMock
+            .Setup(x => x.ExecuteAsync(It.IsAny<AgentTask>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<AgentExecutionResult>.Success(
+                new AgentExecutionResult(executionId, AgentExecutionStatus.Completed, null, null)));
+
+        using var client = CreateClient();
+        var response = await client.PostAsJsonAsync("/api/agents/stream", new
+        {
+            Type = AgentCapabilityType.Writer,
+        });
+
+        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
+    }
+
+    [Fact]
+    public async Task Stream_EmitsCompleteEvent_OnSuccess()
+    {
+        var executionId = Guid.NewGuid();
+        _orchestratorMock
+            .Setup(x => x.ExecuteAsync(It.IsAny<AgentTask>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<AgentExecutionResult>.Success(
+                new AgentExecutionResult(executionId, AgentExecutionStatus.Completed, null, null)));
+
+        using var client = CreateClient();
+        var response = await client.PostAsJsonAsync("/api/agents/stream", new
+        {
+            Type = AgentCapabilityType.Writer,
+        });
+
+        var body = await response.Content.ReadAsStringAsync();
+        Assert.Contains("\"type\":\"complete\"", body);
+        Assert.Contains(executionId.ToString(), body);
+    }
+
+    [Fact]
+    public async Task Stream_EmitsErrorEvent_OnFailure()
+    {
+        _orchestratorMock
+            .Setup(x => x.ExecuteAsync(It.IsAny<AgentTask>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<AgentExecutionResult>.Failure(
+                ErrorCode.ValidationFailed, "Budget exceeded"));
+
+        using var client = CreateClient();
+        var response = await client.PostAsJsonAsync("/api/agents/stream", new
+        {
+            Type = AgentCapabilityType.Writer,
+        });
+
+        var body = await response.Content.ReadAsStringAsync();
+        Assert.Contains("\"type\":\"error\"", body);
+        Assert.Contains("Budget exceeded", body);
+    }
+
+    // --- Test Factory ---
+
+    public class AgentTestFactory : WebApplicationFactory<Program>
+    {
+        protected override void ConfigureWebHost(IWebHostBuilder builder)
+        {
+            builder.UseEnvironment("Development");
+            builder.UseSetting("ApiKey", "test-api-key-12345");
+            builder.UseSetting("ConnectionStrings:DefaultConnection",
+                "Host=localhost;Database=test_agents;Username=test;Password=test");
+
+            builder.ConfigureTestServices(services =>
+            {
+                // Remove background services
+                var hostedServices = services
+                    .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService))
+                    .ToList();
+                foreach (var svc in hostedServices)
+                    services.Remove(svc);
+
+                // Mock orchestrator and token tracker
+                services.AddScoped<IAgentOrchestrator>(_ => _orchestratorMock.Object);
+                services.AddScoped<ITokenTracker>(_ => _tokenTrackerMock.Object);
+            });
+        }
+
+        public HttpClient CreateAuthenticatedClient()
+        {
+            var client = CreateClient();
+            client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key-12345");
+            return client;
+        }
+    }
+}
