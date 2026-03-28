diff --git a/docker-compose.override.yml b/docker-compose.override.yml
index 20a6c8b..ff7cf1a 100644
--- a/docker-compose.override.yml
+++ b/docker-compose.override.yml
@@ -3,6 +3,13 @@ services:
     ports:
       - "5432:5432"
 
+  sidecar:
+    ports:
+      - "3001:3001"
+    networks:
+      - default
+      - internal
+
   api:
     build:
       context: .
diff --git a/docker-compose.yml b/docker-compose.yml
index f52bade..fee8381 100644
--- a/docker-compose.yml
+++ b/docker-compose.yml
@@ -13,6 +13,21 @@ services:
       interval: 5s
       timeout: 5s
       retries: 5
+    networks:
+      - default
+    restart: unless-stopped
+
+  sidecar:
+    build:
+      context: ../claude-code-sidecar
+    container_name: pba-sidecar
+    environment:
+      PORT: "3001"
+      SIDECAR_CONFIG_DIR: /config
+    volumes:
+      - ./prompts:/config/prompts:ro
+    networks:
+      - internal
     restart: unless-stopped
 
   api:
@@ -25,14 +40,47 @@ services:
     depends_on:
       db:
         condition: service_healthy
+      sidecar:
+        condition: service_started
     environment:
       ConnectionStrings__DefaultConnection: "Host=db;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
       ApiKey: ${API_KEY}
       DataProtection__KeyPath: /data-protection-keys
       ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Production}
+      Sidecar__WebSocketUrl: ws://sidecar:3001/ws
+      TrendMonitoring__TrendRadarApiUrl: http://trendradar:8000/api
+      TrendMonitoring__FreshRssApiUrl: http://freshrss:80/api
     volumes:
       - dpkeys:/data-protection-keys
       - logs:/app/logs
+    networks:
+      - default
+      - internal
+    restart: unless-stopped
+
+  trendradar:
+    image: sansan0/trendradar:0.3.0
+    container_name: pba-trendradar
+    volumes:
+      - trendradar_data:/data
+    networks:
+      - internal
+    restart: unless-stopped
+
+  freshrss:
+    image: freshrss/freshrss:1.24.3
+    container_name: pba-freshrss
+    ports:
+      - "8080:80"
+    environment:
+      CRON_MIN: "*/15"
+      TZ: America/New_York
+    volumes:
+      - freshrss_data:/var/www/FreshRSS/data
+      - freshrss_extensions:/var/www/FreshRSS/extensions
+    networks:
+      - default
+      - internal
     restart: unless-stopped
 
   web:
@@ -46,7 +94,15 @@ services:
       - api
     restart: unless-stopped
 
+networks:
+  internal:
+    driver: bridge
+    internal: true
+
 volumes:
   pgdata:
   dpkeys:
   logs:
+  trendradar_data:
+  freshrss_data:
+  freshrss_extensions:
diff --git a/src/PersonalBrandAssistant.Api/appsettings.json b/src/PersonalBrandAssistant.Api/appsettings.json
index ac085f9..00a6c0b 100644
--- a/src/PersonalBrandAssistant.Api/appsettings.json
+++ b/src/PersonalBrandAssistant.Api/appsettings.json
@@ -57,5 +57,26 @@
   "MediaStorage": {
     "BasePath": "./media"
   },
+  "Sidecar": {
+    "WebSocketUrl": "ws://localhost:3001/ws",
+    "ConnectionTimeoutSeconds": 30,
+    "ReconnectDelaySeconds": 5
+  },
+  "ContentEngine": {
+    "BrandVoiceScoreThreshold": 70,
+    "MaxAutoRegenerateAttempts": 3,
+    "EngagementRetentionDays": 30,
+    "EngagementAggregationIntervalHours": 4,
+    "MaxTreeDepth": 3,
+    "SlotMaterializationDays": 7
+  },
+  "TrendMonitoring": {
+    "AggregationIntervalMinutes": 30,
+    "TrendRadarApiUrl": "http://trendradar:8000/api",
+    "FreshRssApiUrl": "http://freshrss:80/api",
+    "RedditSubreddits": ["programming", "dotnet", "webdev"],
+    "RelevanceScoreThreshold": 0.6,
+    "MaxSuggestionsPerCycle": 10
+  },
   "AllowedHosts": "*"
 }
diff --git a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
index dae3ff3..a7fa8ed 100644
--- a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
@@ -75,13 +75,21 @@ public static class DependencyInjection
         services.AddScoped<IContentScheduler, ContentScheduler>();
         services.AddScoped<INotificationService, NotificationService>();
 
-        // Content pipeline
+        // Content engine options
+        services.Configure<SidecarOptions>(
+            configuration.GetSection(SidecarOptions.SectionName));
         services.Configure<ContentEngineOptions>(
             configuration.GetSection(ContentEngineOptions.SectionName));
+        services.Configure<TrendMonitoringOptions>(
+            configuration.GetSection(TrendMonitoringOptions.SectionName));
+
+        // Content engine services
         services.AddScoped<IBrandVoiceService, BrandVoiceService>();
         services.AddScoped<IContentPipeline, ContentPipeline>();
         services.AddScoped<IRepurposingService, RepurposingService>();
         services.AddScoped<IContentCalendarService, ContentCalendarService>();
+        services.AddScoped<ITrendMonitor, TrendMonitor>();
+        services.AddScoped<IEngagementAggregator, EngagementAggregator>();
 
         // Platform integration options
         services.Configure<PlatformIntegrationOptions>(configuration.GetSection(PlatformIntegrationOptions.SectionName));
@@ -141,6 +149,12 @@ public static class DependencyInjection
         services.AddHostedService<PlatformHealthMonitor>();
         services.AddHostedService<PublishCompletionPoller>();
 
+        // Content engine background services
+        services.AddHostedService<RepurposeOnPublishProcessor>();
+        services.AddHostedService<TrendAggregationProcessor>();
+        services.AddHostedService<EngagementAggregationProcessor>();
+        services.AddHostedService<CalendarSlotProcessor>();
+
         services.AddHealthChecks()
             .AddDbContextCheck<ApplicationDbContext>();
 
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/ContentEngineServiceRegistrationTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/ContentEngineServiceRegistrationTests.cs
new file mode 100644
index 0000000..70245c5
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/ContentEngineServiceRegistrationTests.cs
@@ -0,0 +1,198 @@
+using Microsoft.AspNetCore.Hosting;
+using Microsoft.AspNetCore.Mvc.Testing;
+using Microsoft.AspNetCore.TestHost;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Hosting;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
+using PersonalBrandAssistant.Infrastructure.Services.ContentServices;
+using PersonalBrandAssistant.Infrastructure.Tests.Mocks;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.DependencyInjection;
+
+public class ContentEngineServiceRegistrationTests
+    : IClassFixture<ContentEngineServiceRegistrationTests.DiTestFactory>, IDisposable
+{
+    private const string TestApiKey = "test-api-key-12345";
+    private readonly DiTestFactory _factory;
+    private IServiceScope? _scope;
+
+    public ContentEngineServiceRegistrationTests(DiTestFactory factory)
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
+                services.AddSingleton<ISidecarClient>(new MockSidecarClient());
+            });
+        });
+        _scope = factory.Services.CreateScope();
+        return _scope.ServiceProvider;
+    }
+
+    [Fact]
+    public void IContentPipeline_Resolves_AsScoped()
+    {
+        var sp = CreateServiceProvider();
+        var service = sp.GetService<IContentPipeline>();
+        Assert.NotNull(service);
+        Assert.IsType<ContentPipeline>(service);
+    }
+
+    [Fact]
+    public void IRepurposingService_Resolves_AsScoped()
+    {
+        var sp = CreateServiceProvider();
+        var service = sp.GetService<IRepurposingService>();
+        Assert.NotNull(service);
+        Assert.IsType<RepurposingService>(service);
+    }
+
+    [Fact]
+    public void IContentCalendarService_Resolves_AsScoped()
+    {
+        var sp = CreateServiceProvider();
+        var service = sp.GetService<IContentCalendarService>();
+        Assert.NotNull(service);
+        Assert.IsType<ContentCalendarService>(service);
+    }
+
+    [Fact]
+    public void IBrandVoiceService_Resolves_AsScoped()
+    {
+        var sp = CreateServiceProvider();
+        var service = sp.GetService<IBrandVoiceService>();
+        Assert.NotNull(service);
+        Assert.IsType<BrandVoiceService>(service);
+    }
+
+    [Fact]
+    public void ITrendMonitor_Resolves_AsScoped()
+    {
+        var sp = CreateServiceProvider();
+        var service = sp.GetService<ITrendMonitor>();
+        Assert.NotNull(service);
+        Assert.IsType<TrendMonitor>(service);
+    }
+
+    [Fact]
+    public void IEngagementAggregator_Resolves_AsScoped()
+    {
+        var sp = CreateServiceProvider();
+        var service = sp.GetService<IEngagementAggregator>();
+        Assert.NotNull(service);
+        Assert.IsType<EngagementAggregator>(service);
+    }
+
+    [Fact]
+    public void BackgroundServices_AllFourRegistered()
+    {
+        // DiTestFactory strips hosted services. Use a separate factory that captures
+        // hosted service types in ConfigureTestServices before stripping them.
+        List<Type?> capturedTypes = [];
+
+        using var factory = new HostedServiceTestFactory(capturedTypes);
+        var withBuilder = factory.WithWebHostBuilder(builder =>
+        {
+            builder.ConfigureTestServices(services =>
+            {
+                services.AddSingleton<ISidecarClient>(new MockSidecarClient());
+            });
+        });
+
+        // Trigger factory initialization
+        _ = withBuilder.Services;
+
+        Assert.Contains(typeof(RepurposeOnPublishProcessor), capturedTypes);
+        Assert.Contains(typeof(TrendAggregationProcessor), capturedTypes);
+        Assert.Contains(typeof(EngagementAggregationProcessor), capturedTypes);
+        Assert.Contains(typeof(CalendarSlotProcessor), capturedTypes);
+    }
+
+    [Fact]
+    public void SidecarOptions_BindsFromConfiguration()
+    {
+        var sp = CreateServiceProvider();
+        var options = sp.GetRequiredService<IOptions<SidecarOptions>>();
+        Assert.Equal("ws://test-sidecar:3001/ws", options.Value.WebSocketUrl);
+    }
+
+    [Fact]
+    public void ContentEngineOptions_BindsFromConfiguration()
+    {
+        var sp = CreateServiceProvider();
+        var options = sp.GetRequiredService<IOptions<ContentEngineOptions>>();
+        Assert.Equal(75, options.Value.BrandVoiceScoreThreshold);
+    }
+
+    [Fact]
+    public void TrendMonitoringOptions_BindsFromConfiguration()
+    {
+        var sp = CreateServiceProvider();
+        var options = sp.GetRequiredService<IOptions<TrendMonitoringOptions>>();
+        Assert.Equal(15, options.Value.AggregationIntervalMinutes);
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
+            // Content engine config
+            builder.UseSetting("Sidecar:WebSocketUrl", "ws://test-sidecar:3001/ws");
+            builder.UseSetting("ContentEngine:BrandVoiceScoreThreshold", "75");
+            builder.UseSetting("TrendMonitoring:AggregationIntervalMinutes", "15");
+
+            builder.ConfigureTestServices(services =>
+            {
+                var hostedServices = services
+                    .Where(d => d.ServiceType == typeof(IHostedService))
+                    .ToList();
+                foreach (var svc in hostedServices)
+                    services.Remove(svc);
+            });
+        }
+    }
+
+    /// <summary>
+    /// Factory that captures hosted service types before removing them.
+    /// </summary>
+    public class HostedServiceTestFactory(List<Type?> capturedTypes) : WebApplicationFactory<Program>
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
+                // Capture hosted service impl types before removing
+                var hostedServices = services
+                    .Where(d => d.ServiceType == typeof(IHostedService))
+                    .ToList();
+                capturedTypes.AddRange(hostedServices.Select(d => d.ImplementationType));
+
+                foreach (var svc in hostedServices)
+                    services.Remove(svc);
+            });
+        }
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Docker/DockerComposeValidationTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Docker/DockerComposeValidationTests.cs
new file mode 100644
index 0000000..d8df6ed
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Docker/DockerComposeValidationTests.cs
@@ -0,0 +1,44 @@
+namespace PersonalBrandAssistant.Infrastructure.Tests.Docker;
+
+public class DockerComposeValidationTests
+{
+    private static string GetComposeFilePath()
+    {
+        // Walk up from bin/Debug/net10.0 to find the repo root with docker-compose.yml
+        var dir = new DirectoryInfo(AppContext.BaseDirectory);
+        while (dir != null)
+        {
+            var composeFile = Path.Combine(dir.FullName, "docker-compose.yml");
+            if (File.Exists(composeFile))
+                return composeFile;
+            dir = dir.Parent;
+        }
+
+        throw new FileNotFoundException("docker-compose.yml not found in any parent directory.");
+    }
+
+    [Fact]
+    public void SidecarService_NotPublishedToExternalPorts()
+    {
+        var composeContent = File.ReadAllText(GetComposeFilePath());
+
+        // Extract the sidecar service block (from "  sidecar:" to next service or end)
+        var sidecarIndex = composeContent.IndexOf("  sidecar:", StringComparison.Ordinal);
+        if (sidecarIndex < 0)
+        {
+            Assert.Fail("Sidecar service not found in docker-compose.yml");
+            return;
+        }
+
+        // Find the next top-level service (2 spaces + word + colon at start of line)
+        var afterSidecar = composeContent[(sidecarIndex + 10)..];
+        var nextServiceMatch = System.Text.RegularExpressions.Regex.Match(
+            afterSidecar, @"^\s{2}\w+:", System.Text.RegularExpressions.RegexOptions.Multiline);
+
+        var sidecarBlock = nextServiceMatch.Success
+            ? afterSidecar[..nextServiceMatch.Index]
+            : afterSidecar;
+
+        Assert.DoesNotContain("ports:", sidecarBlock);
+    }
+}
