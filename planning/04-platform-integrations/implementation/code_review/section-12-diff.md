diff --git a/src/PersonalBrandAssistant.Api/appsettings.json b/src/PersonalBrandAssistant.Api/appsettings.json
index cd9c54b..f9d00f7 100644
--- a/src/PersonalBrandAssistant.Api/appsettings.json
+++ b/src/PersonalBrandAssistant.Api/appsettings.json
@@ -47,5 +47,26 @@
     "ExecutionTimeoutSeconds": 180,
     "LogPromptContent": false
   },
+  "PlatformIntegrations": {
+    "Twitter": {
+      "CallbackUrl": "http://localhost:4200/platforms/twitter/callback",
+      "BaseUrl": "https://api.x.com/2"
+    },
+    "LinkedIn": {
+      "CallbackUrl": "http://localhost:4200/platforms/linkedin/callback",
+      "ApiVersion": "202603",
+      "BaseUrl": "https://api.linkedin.com/rest"
+    },
+    "Instagram": {
+      "CallbackUrl": "http://localhost:4200/platforms/instagram/callback"
+    },
+    "YouTube": {
+      "CallbackUrl": "http://localhost:4200/platforms/youtube/callback",
+      "DailyQuotaLimit": 10000
+    }
+  },
+  "MediaStorage": {
+    "BasePath": "./media"
+  },
   "AllowedHosts": "*"
 }
diff --git a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
index 39db350..4e366fe 100644
--- a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
@@ -11,6 +11,10 @@ using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
 using PersonalBrandAssistant.Infrastructure.Data;
 using PersonalBrandAssistant.Infrastructure.Data.Interceptors;
 using PersonalBrandAssistant.Infrastructure.Services;
+using PersonalBrandAssistant.Infrastructure.Services.MediaServices;
+using PersonalBrandAssistant.Infrastructure.Services.PlatformServices;
+using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;
+using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;
 
 namespace PersonalBrandAssistant.Infrastructure;
 
@@ -69,14 +73,63 @@ public static class DependencyInjection
         services.AddScoped<IApprovalService, ApprovalService>();
         services.AddScoped<IContentScheduler, ContentScheduler>();
         services.AddScoped<INotificationService, NotificationService>();
-        services.AddScoped<IPublishingPipeline, PublishingPipelineStub>();
 
+        // Platform integration options
+        services.Configure<PlatformIntegrationOptions>(configuration.GetSection("PlatformIntegrations"));
+        services.Configure<MediaStorageOptions>(configuration.GetSection("MediaStorage"));
+
+        // Singleton services
+        services.AddSingleton(TimeProvider.System);
+        services.AddMemoryCache();
+        services.AddSingleton<IMediaStorage, LocalMediaStorage>();
+
+        // Platform adapters with typed HttpClients
+        services.AddHttpClient<TwitterPlatformAdapter>(client =>
+        {
+            client.BaseAddress = new Uri(
+                configuration["PlatformIntegrations:Twitter:BaseUrl"] ?? "https://api.x.com/2");
+        });
+        services.AddHttpClient<LinkedInPlatformAdapter>(client =>
+        {
+            client.BaseAddress = new Uri(
+                configuration["PlatformIntegrations:LinkedIn:BaseUrl"] ?? "https://api.linkedin.com/rest");
+            client.DefaultRequestHeaders.Add("X-Restli-Protocol-Version", "2.0.0");
+            var apiVersion = configuration["PlatformIntegrations:LinkedIn:ApiVersion"] ?? "202603";
+            client.DefaultRequestHeaders.Add("Linkedin-Version", apiVersion);
+        });
+        services.AddHttpClient<InstagramPlatformAdapter>(client =>
+        {
+            client.BaseAddress = new Uri("https://graph.facebook.com/v19.0");
+        });
+        services.AddHttpClient<YouTubePlatformAdapter>();
+
+        // Scoped services
+        services.AddScoped<IPublishingPipeline, PublishingPipeline>();
+        services.AddScoped<IOAuthManager, OAuthManager>();
+        services.AddScoped<IRateLimiter, DatabaseRateLimiter>();
+
+        // Platform adapters (multi-registration for IEnumerable<ISocialPlatform>)
+        services.AddScoped<ISocialPlatform>(sp => sp.GetRequiredService<TwitterPlatformAdapter>());
+        services.AddScoped<ISocialPlatform>(sp => sp.GetRequiredService<LinkedInPlatformAdapter>());
+        services.AddScoped<ISocialPlatform>(sp => sp.GetRequiredService<InstagramPlatformAdapter>());
+        services.AddScoped<ISocialPlatform>(sp => sp.GetRequiredService<YouTubePlatformAdapter>());
+
+        // Content formatters (multi-registration)
+        services.AddScoped<IPlatformContentFormatter, TwitterContentFormatter>();
+        services.AddScoped<IPlatformContentFormatter, LinkedInContentFormatter>();
+        services.AddScoped<IPlatformContentFormatter, InstagramContentFormatter>();
+        services.AddScoped<IPlatformContentFormatter, YouTubeContentFormatter>();
+
+        // Background services
         services.AddHostedService<DataSeeder>();
         services.AddHostedService<AuditLogCleanupService>();
         services.AddHostedService<ScheduledPublishProcessor>();
         services.AddHostedService<RetryFailedProcessor>();
         services.AddHostedService<WorkflowRehydrator>();
         services.AddHostedService<RetentionCleanupService>();
+        services.AddHostedService<TokenRefreshProcessor>();
+        services.AddHostedService<PlatformHealthMonitor>();
+        services.AddHostedService<PublishCompletionPoller>();
 
         services.AddHealthChecks()
             .AddDbContextCheck<ApplicationDbContext>();
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/PublishingPipelineStub.cs b/src/PersonalBrandAssistant.Infrastructure/Services/PublishingPipelineStub.cs
deleted file mode 100644
index 85544d5..0000000
--- a/src/PersonalBrandAssistant.Infrastructure/Services/PublishingPipelineStub.cs
+++ /dev/null
@@ -1,14 +0,0 @@
-using PersonalBrandAssistant.Application.Common.Errors;
-using PersonalBrandAssistant.Application.Common.Interfaces;
-using PersonalBrandAssistant.Application.Common.Models;
-
-namespace PersonalBrandAssistant.Infrastructure.Services;
-
-public class PublishingPipelineStub : IPublishingPipeline
-{
-    public Task<Result<MediatR.Unit>> PublishAsync(Guid contentId, CancellationToken ct = default)
-    {
-        return Task.FromResult(
-            Result<MediatR.Unit>.Failure(ErrorCode.InternalError, "Publishing pipeline not implemented"));
-    }
-}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Services/PublishingPipelineStubTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Services/PublishingPipelineStubTests.cs
deleted file mode 100644
index 1ef3ef4..0000000
--- a/tests/PersonalBrandAssistant.Application.Tests/Services/PublishingPipelineStubTests.cs
+++ /dev/null
@@ -1,19 +0,0 @@
-using PersonalBrandAssistant.Application.Common.Errors;
-using PersonalBrandAssistant.Infrastructure.Services;
-
-namespace PersonalBrandAssistant.Application.Tests.Services;
-
-public class PublishingPipelineStubTests
-{
-    [Fact]
-    public async Task PublishAsync_ReturnsFailure_WithInternalError()
-    {
-        var stub = new PublishingPipelineStub();
-
-        var result = await stub.PublishAsync(Guid.NewGuid());
-
-        Assert.False(result.IsSuccess);
-        Assert.Equal(ErrorCode.InternalError, result.ErrorCode);
-        Assert.Contains("Publishing pipeline not implemented", result.Errors);
-    }
-}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs
index 335f9c4..f50ab82 100644
--- a/tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs
@@ -45,15 +45,18 @@ public class EnumTests
     }
 
     [Fact]
-    public void NotificationType_HasExactly5Values()
+    public void NotificationType_HasExactly8Values()
     {
         var values = Enum.GetValues<NotificationType>();
-        Assert.Equal(5, values.Length);
+        Assert.Equal(8, values.Length);
         Assert.Contains(NotificationType.ContentReadyForReview, values);
         Assert.Contains(NotificationType.ContentApproved, values);
         Assert.Contains(NotificationType.ContentRejected, values);
         Assert.Contains(NotificationType.ContentPublished, values);
         Assert.Contains(NotificationType.ContentFailed, values);
+        Assert.Contains(NotificationType.PlatformDisconnected, values);
+        Assert.Contains(NotificationType.PlatformTokenExpiring, values);
+        Assert.Contains(NotificationType.PlatformScopeMismatch, values);
     }
 
     [Fact]
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/PlatformServiceRegistrationTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/PlatformServiceRegistrationTests.cs
new file mode 100644
index 0000000..8a08697
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/PlatformServiceRegistrationTests.cs
@@ -0,0 +1,141 @@
+using Microsoft.AspNetCore.Hosting;
+using Microsoft.AspNetCore.Mvc.Testing;
+using Microsoft.AspNetCore.TestHost;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Infrastructure.Services.PlatformServices;
+using PersonalBrandAssistant.Infrastructure.Tests.Mocks;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.DependencyInjection;
+
+public class PlatformServiceRegistrationTests : IClassFixture<PlatformServiceRegistrationTests.DiTestFactory>, IDisposable
+{
+    private const string TestApiKey = "test-api-key-12345";
+    private readonly DiTestFactory _factory;
+    private IServiceScope? _scope;
+
+    public PlatformServiceRegistrationTests(DiTestFactory factory)
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
+                services.AddSingleton<IChatClientFactory>(new MockChatClientFactory());
+            });
+        });
+        _scope = factory.Services.CreateScope();
+        return _scope.ServiceProvider;
+    }
+
+    [Fact]
+    public void AllSocialPlatformAdapters_Resolve()
+    {
+        var sp = CreateServiceProvider();
+        var adapters = sp.GetServices<ISocialPlatform>().ToList();
+        Assert.Equal(4, adapters.Count);
+    }
+
+    [Fact]
+    public void AllContentFormatters_Resolve()
+    {
+        var sp = CreateServiceProvider();
+        var formatters = sp.GetServices<IPlatformContentFormatter>().ToList();
+        Assert.Equal(4, formatters.Count);
+    }
+
+    [Fact]
+    public void IOAuthManager_Resolves()
+    {
+        var sp = CreateServiceProvider();
+        var service = sp.GetService<IOAuthManager>();
+        Assert.NotNull(service);
+        Assert.IsType<OAuthManager>(service);
+    }
+
+    [Fact]
+    public void IRateLimiter_Resolves()
+    {
+        var sp = CreateServiceProvider();
+        var service = sp.GetService<IRateLimiter>();
+        Assert.NotNull(service);
+        Assert.IsType<DatabaseRateLimiter>(service);
+    }
+
+    [Fact]
+    public void IMediaStorage_Resolves()
+    {
+        var sp = CreateServiceProvider();
+        var service = sp.GetService<IMediaStorage>();
+        Assert.NotNull(service);
+    }
+
+    [Fact]
+    public void IPublishingPipeline_ResolvesRealImplementation()
+    {
+        var sp = CreateServiceProvider();
+        var service = sp.GetService<IPublishingPipeline>();
+        Assert.NotNull(service);
+        Assert.IsType<PublishingPipeline>(service);
+    }
+
+    [Fact]
+    public void PlatformIntegrationOptions_BindsFromConfig()
+    {
+        var sp = CreateServiceProvider();
+        var options = sp.GetRequiredService<IOptions<PlatformIntegrationOptions>>();
+        Assert.NotNull(options.Value.Twitter);
+        Assert.Equal("http://localhost:4200/platforms/twitter/callback", options.Value.Twitter.CallbackUrl);
+    }
+
+    [Fact]
+    public void MediaStorageOptions_BindsFromConfig()
+    {
+        var sp = CreateServiceProvider();
+        var options = sp.GetRequiredService<IOptions<MediaStorageOptions>>();
+        Assert.Equal("./test-media", options.Value.BasePath);
+    }
+
+    public void Dispose()
+    {
+        _scope?.Dispose();
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
+            // Platform integration config
+            builder.UseSetting("PlatformIntegrations:Twitter:CallbackUrl", "http://localhost:4200/platforms/twitter/callback");
+            builder.UseSetting("PlatformIntegrations:Twitter:BaseUrl", "https://api.x.com/2");
+            builder.UseSetting("PlatformIntegrations:LinkedIn:CallbackUrl", "http://localhost:4200/platforms/linkedin/callback");
+            builder.UseSetting("PlatformIntegrations:LinkedIn:ApiVersion", "202603");
+            builder.UseSetting("PlatformIntegrations:LinkedIn:BaseUrl", "https://api.linkedin.com/rest");
+            builder.UseSetting("PlatformIntegrations:Instagram:CallbackUrl", "http://localhost:4200/platforms/instagram/callback");
+            builder.UseSetting("PlatformIntegrations:YouTube:CallbackUrl", "http://localhost:4200/platforms/youtube/callback");
+            builder.UseSetting("PlatformIntegrations:YouTube:DailyQuotaLimit", "10000");
+            builder.UseSetting("MediaStorage:BasePath", "./test-media");
+            builder.UseSetting("MediaStorage:SigningKey", "test-signing-key-for-hmac-validation");
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
