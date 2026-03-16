diff --git a/planning/03-agent-orchestration/implementation/deep_implement_config.json b/planning/03-agent-orchestration/implementation/deep_implement_config.json
index ebbb6da..0daf890 100644
--- a/planning/03-agent-orchestration/implementation/deep_implement_config.json
+++ b/planning/03-agent-orchestration/implementation/deep_implement_config.json
@@ -55,6 +55,10 @@
     "section-09-orchestrator": {
       "status": "complete",
       "commit_hash": "d921aea"
+    },
+    "section-10-api-endpoints": {
+      "status": "complete",
+      "commit_hash": "eafeb46"
     }
   },
   "pre_commit": {
diff --git a/src/PersonalBrandAssistant.Api/Endpoints/AgentEndpoints.cs b/src/PersonalBrandAssistant.Api/Endpoints/AgentEndpoints.cs
index 8218a24..b678c2c 100644
--- a/src/PersonalBrandAssistant.Api/Endpoints/AgentEndpoints.cs
+++ b/src/PersonalBrandAssistant.Api/Endpoints/AgentEndpoints.cs
@@ -1,5 +1,6 @@
 using System.Text.Json;
 using PersonalBrandAssistant.Api.Extensions;
+using PersonalBrandAssistant.Application.Common.Errors;
 using PersonalBrandAssistant.Application.Common.Interfaces;
 using PersonalBrandAssistant.Application.Common.Models;
 using PersonalBrandAssistant.Domain.Enums;
@@ -35,7 +36,6 @@ public static class AgentEndpoints
         httpContext.Response.Headers["X-Accel-Buffering"] = "no";
 
         var ct = httpContext.RequestAborted;
-        var writer = httpContext.Response.BodyWriter;
 
         try
         {
@@ -72,10 +72,13 @@ public static class AgentEndpoints
             }
             else
             {
+                var safeMessage = result.ErrorCode == ErrorCode.ValidationFailed
+                    ? string.Join("; ", result.Errors)
+                    : "Agent execution failed.";
                 await WriteSseEventAsync(httpContext, new
                 {
                     type = "error",
-                    message = string.Join("; ", result.Errors),
+                    message = safeMessage,
                 });
             }
         }
diff --git a/src/PersonalBrandAssistant.Api/appsettings.json b/src/PersonalBrandAssistant.Api/appsettings.json
index 17187e2..2fad453 100644
--- a/src/PersonalBrandAssistant.Api/appsettings.json
+++ b/src/PersonalBrandAssistant.Api/appsettings.json
@@ -28,5 +28,25 @@
     "WorkflowTransitionLogDays": 180,
     "NotificationDays": 90
   },
+  "AgentOrchestration": {
+    "ApiKey": "",
+    "DailyBudget": 10.00,
+    "MonthlyBudget": 100.00,
+    "DefaultModelTier": "Standard",
+    "Models": {
+      "Fast": "claude-haiku-4-5",
+      "Standard": "claude-sonnet-4-5-20250929",
+      "Advanced": "claude-opus-4-6"
+    },
+    "Pricing": {
+      "claude-haiku-4-5": { "InputPerMillion": 1.00, "OutputPerMillion": 5.00 },
+      "claude-sonnet-4-5-20250929": { "InputPerMillion": 3.00, "OutputPerMillion": 15.00 },
+      "claude-opus-4-6": { "InputPerMillion": 5.00, "OutputPerMillion": 25.00 }
+    },
+    "PromptsPath": "prompts",
+    "MaxRetriesPerExecution": 3,
+    "ExecutionTimeoutSeconds": 180,
+    "LogPromptContent": true
+  },
   "AllowedHosts": "*"
 }
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs b/src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs
index 4ca0549..892df22 100644
--- a/src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs
+++ b/src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs
@@ -6,10 +6,13 @@ public class AgentOrchestrationOptions
 
     public decimal DailyBudget { get; init; } = 10.00m;
     public decimal MonthlyBudget { get; init; } = 100.00m;
+    public string DefaultModelTier { get; init; } = "Standard";
+    public Dictionary<string, string> Models { get; init; } = new();
+    public Dictionary<string, ModelPricingOptions> Pricing { get; init; } = new();
     public string PromptsPath { get; init; } = "prompts";
-    public int ExecutionTimeoutSeconds { get; init; } = 120;
     public int MaxRetriesPerExecution { get; init; } = 3;
-    public Dictionary<string, ModelPricingOptions> Pricing { get; init; } = new();
+    public int ExecutionTimeoutSeconds { get; init; } = 180;
+    public bool LogPromptContent { get; init; } = true;
 }
 
 public record ModelPricingOptions
diff --git a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
index 7bf2d3d..23786f1 100644
--- a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
@@ -3,6 +3,9 @@ using Microsoft.EntityFrameworkCore;
 using Microsoft.Extensions.Configuration;
 using Microsoft.Extensions.DependencyInjection;
 using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Infrastructure.Agents;
+using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
 using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
 using PersonalBrandAssistant.Infrastructure.Data;
 using PersonalBrandAssistant.Infrastructure.Data.Interceptors;
@@ -41,6 +44,27 @@ public static class DependencyInjection
 
         services.AddSingleton<IEncryptionService, EncryptionService>();
 
+        // Agent orchestration
+        services.Configure<AgentOrchestrationOptions>(
+            configuration.GetSection(AgentOrchestrationOptions.SectionName));
+        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
+        services.AddSingleton<IPromptTemplateService>(sp =>
+        {
+            var options = configuration.GetSection(AgentOrchestrationOptions.SectionName)
+                .Get<AgentOrchestrationOptions>() ?? new AgentOrchestrationOptions();
+            return new PromptTemplateService(
+                options.PromptsPath,
+                sp.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>(),
+                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PromptTemplateService>>());
+        });
+        services.AddScoped<ITokenTracker, TokenTracker>();
+        services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();
+        services.AddScoped<IAgentCapability, WriterAgentCapability>();
+        services.AddScoped<IAgentCapability, SocialAgentCapability>();
+        services.AddScoped<IAgentCapability, RepurposeAgentCapability>();
+        services.AddScoped<IAgentCapability, EngagementAgentCapability>();
+        services.AddScoped<IAgentCapability, AnalyticsAgentCapability>();
+
         services.AddScoped<IWorkflowEngine, WorkflowEngine>();
         services.AddScoped<IApprovalService, ApprovalService>();
         services.AddScoped<IContentScheduler, ContentScheduler>();
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AgentEndpointsTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AgentEndpointsTests.cs
index 7f2a1c8..20b1d75 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AgentEndpointsTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AgentEndpointsTests.cs
@@ -16,18 +16,29 @@ namespace PersonalBrandAssistant.Infrastructure.Tests.Api;
 
 public class AgentEndpointsTests : IClassFixture<AgentEndpointsTests.AgentTestFactory>
 {
+    private const string TestApiKey = "test-api-key-12345";
     private readonly AgentTestFactory _factory;
-    private static readonly Mock<IAgentOrchestrator> _orchestratorMock = new();
-    private static readonly Mock<ITokenTracker> _tokenTrackerMock = new();
+    private readonly Mock<IAgentOrchestrator> _orchestratorMock = new();
+    private readonly Mock<ITokenTracker> _tokenTrackerMock = new();
 
     public AgentEndpointsTests(AgentTestFactory factory)
     {
         _factory = factory;
-        _orchestratorMock.Reset();
-        _tokenTrackerMock.Reset();
     }
 
-    private HttpClient CreateClient() => _factory.CreateAuthenticatedClient();
+    private HttpClient CreateClient()
+    {
+        var client = _factory.WithWebHostBuilder(builder =>
+        {
+            builder.ConfigureTestServices(services =>
+            {
+                services.AddScoped<IAgentOrchestrator>(_ => _orchestratorMock.Object);
+                services.AddScoped<ITokenTracker>(_ => _tokenTrackerMock.Object);
+            });
+        }).CreateClient();
+        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
+        return client;
+    }
 
     // --- POST /api/agents/execute ---
 
@@ -256,30 +267,18 @@ public class AgentEndpointsTests : IClassFixture<AgentEndpointsTests.AgentTestFa
         protected override void ConfigureWebHost(IWebHostBuilder builder)
         {
             builder.UseEnvironment("Development");
-            builder.UseSetting("ApiKey", "test-api-key-12345");
+            builder.UseSetting("ApiKey", TestApiKey);
             builder.UseSetting("ConnectionStrings:DefaultConnection",
                 "Host=localhost;Database=test_agents;Username=test;Password=test");
 
             builder.ConfigureTestServices(services =>
             {
-                // Remove background services
                 var hostedServices = services
                     .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService))
                     .ToList();
                 foreach (var svc in hostedServices)
                     services.Remove(svc);
-
-                // Mock orchestrator and token tracker
-                services.AddScoped<IAgentOrchestrator>(_ => _orchestratorMock.Object);
-                services.AddScoped<ITokenTracker>(_ => _tokenTrackerMock.Object);
             });
         }
-
-        public HttpClient CreateAuthenticatedClient()
-        {
-            var client = CreateClient();
-            client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key-12345");
-            return client;
-        }
     }
 }
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/AgentServiceRegistrationTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/AgentServiceRegistrationTests.cs
new file mode 100644
index 0000000..ede656a
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/AgentServiceRegistrationTests.cs
@@ -0,0 +1,92 @@
+using Microsoft.AspNetCore.Hosting;
+using Microsoft.AspNetCore.Mvc.Testing;
+using Microsoft.AspNetCore.TestHost;
+using Microsoft.Extensions.DependencyInjection;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Infrastructure.Tests.Mocks;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.DependencyInjection;
+
+public class AgentServiceRegistrationTests : IClassFixture<AgentServiceRegistrationTests.DiTestFactory>
+{
+    private const string TestApiKey = "test-api-key-12345";
+    private readonly DiTestFactory _factory;
+
+    public AgentServiceRegistrationTests(DiTestFactory factory)
+    {
+        _factory = factory;
+    }
+
+    private IServiceProvider CreateServiceProvider()
+    {
+        var factory = _factory.WithWebHostBuilder(builder =>
+        {
+            builder.ConfigureTestServices(services =>
+            {
+                // Replace ChatClientFactory with mock to avoid real Anthropic API key requirement
+                services.AddSingleton<IChatClientFactory>(new MockChatClientFactory());
+            });
+        });
+        return factory.Services.CreateScope().ServiceProvider;
+    }
+
+    [Fact]
+    public void IChatClientFactory_Resolves()
+    {
+        var sp = CreateServiceProvider();
+        var service = sp.GetService<IChatClientFactory>();
+        Assert.NotNull(service);
+    }
+
+    [Fact]
+    public void IPromptTemplateService_Resolves()
+    {
+        var sp = CreateServiceProvider();
+        var service = sp.GetService<IPromptTemplateService>();
+        Assert.NotNull(service);
+    }
+
+    [Fact]
+    public void ITokenTracker_Resolves()
+    {
+        var sp = CreateServiceProvider();
+        var service = sp.GetService<ITokenTracker>();
+        Assert.NotNull(service);
+    }
+
+    [Fact]
+    public void IAgentOrchestrator_Resolves()
+    {
+        var sp = CreateServiceProvider();
+        var service = sp.GetService<IAgentOrchestrator>();
+        Assert.NotNull(service);
+    }
+
+    [Fact]
+    public void AllAgentCapabilities_Resolve()
+    {
+        var sp = CreateServiceProvider();
+        var capabilities = sp.GetServices<IAgentCapability>().ToList();
+        Assert.Equal(5, capabilities.Count);
+    }
+
+    public class DiTestFactory : WebApplicationFactory<Program>
+    {
+        protected override void ConfigureWebHost(IWebHostBuilder builder)
+        {
+            builder.UseEnvironment("Development");
+            builder.UseSetting("ApiKey", TestApiKey);
+            builder.UseSetting("ConnectionStrings:DefaultConnection",
+                "Host=localhost;Database=test_di;Username=test;Password=test");
+
+            builder.ConfigureTestServices(services =>
+            {
+                var hostedServices = services
+                    .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService))
+                    .ToList();
+                foreach (var svc in hostedServices)
+                    services.Remove(svc);
+            });
+        }
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockChatClient.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockChatClient.cs
new file mode 100644
index 0000000..ccb9f0d
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockChatClient.cs
@@ -0,0 +1,71 @@
+using System.Runtime.CompilerServices;
+using Microsoft.Extensions.AI;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Mocks;
+
+public sealed class MockChatClient : IChatClient
+{
+    private readonly string _responseText;
+    private readonly int _inputTokens;
+    private readonly int _outputTokens;
+    private int _callCount;
+    private readonly int _failFirstNCalls;
+
+    public MockChatClient(
+        string responseText = "Mock response",
+        int inputTokens = 100,
+        int outputTokens = 50,
+        int failFirstNCalls = 0)
+    {
+        _responseText = responseText;
+        _inputTokens = inputTokens;
+        _outputTokens = outputTokens;
+        _failFirstNCalls = failFirstNCalls;
+    }
+
+    public int CallCount => _callCount;
+
+    public ChatClientMetadata Metadata { get; } = new("MockChatClient", null, "mock-model");
+
+    public async Task<ChatResponse> GetResponseAsync(
+        IEnumerable<ChatMessage> messages,
+        ChatOptions? options = null,
+        CancellationToken cancellationToken = default)
+    {
+        var count = Interlocked.Increment(ref _callCount);
+        if (count <= _failFirstNCalls)
+            throw new HttpRequestException("Simulated transient failure");
+
+        await Task.CompletedTask;
+
+        return new ChatResponse(new ChatMessage(ChatRole.Assistant, _responseText))
+        {
+            Usage = new UsageDetails
+            {
+                InputTokenCount = _inputTokens,
+                OutputTokenCount = _outputTokens,
+            },
+        };
+    }
+
+    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
+        IEnumerable<ChatMessage> messages,
+        ChatOptions? options = null,
+        [EnumeratorCancellation] CancellationToken cancellationToken = default)
+    {
+        var count = Interlocked.Increment(ref _callCount);
+        if (count <= _failFirstNCalls)
+            throw new HttpRequestException("Simulated transient failure");
+
+        var words = _responseText.Split(' ');
+        foreach (var word in words)
+        {
+            await Task.Yield();
+            yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ");
+        }
+    }
+
+    public object? GetService(Type serviceType, object? serviceKey = null) => null;
+
+    public void Dispose() { }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockChatClientFactory.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockChatClientFactory.cs
new file mode 100644
index 0000000..c54a868
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockChatClientFactory.cs
@@ -0,0 +1,18 @@
+using Microsoft.Extensions.AI;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Mocks;
+
+public sealed class MockChatClientFactory : IChatClientFactory
+{
+    private readonly MockChatClient _client;
+
+    public MockChatClientFactory(MockChatClient? client = null)
+    {
+        _client = client ?? new MockChatClient();
+    }
+
+    public IChatClient CreateClient(ModelTier tier) => _client;
+    public IChatClient CreateStreamingClient(ModelTier tier) => _client;
+}
