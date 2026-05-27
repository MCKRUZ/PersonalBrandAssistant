diff --git a/src/PBA.Api/appsettings.json b/src/PBA.Api/appsettings.json
index 4779c43..2e1fd1c 100644
--- a/src/PBA.Api/appsettings.json
+++ b/src/PBA.Api/appsettings.json
@@ -16,5 +16,34 @@
     "RemoteName": "origin",
     "Branch": "main",
     "BaseUrl": "https://matthewkruczek.ai"
+  },
+  "Encryption": {
+    "Key": ""
+  },
+  "ContentTransformer": {
+    "BaseUrl": "https://matthewkruczek.ai"
+  },
+  "Publishing": {
+    "Medium": {
+      "Enabled": false,
+      "DefaultPublishStatus": "draft"
+    },
+    "Substack": {
+      "Enabled": false,
+      "PublicationSlug": "",
+      "DefaultAudience": "everyone"
+    },
+    "LinkedIn": {
+      "Enabled": false,
+      "ClientId": "",
+      "ClientSecret": "",
+      "RedirectUri": ""
+    },
+    "Twitter": {
+      "Enabled": false,
+      "ClientId": "",
+      "ClientSecret": "",
+      "RedirectUri": ""
+    }
   }
 }
diff --git a/src/PBA.Infrastructure/DependencyInjection.cs b/src/PBA.Infrastructure/DependencyInjection.cs
index 72c9aef..177d8c7 100644
--- a/src/PBA.Infrastructure/DependencyInjection.cs
+++ b/src/PBA.Infrastructure/DependencyInjection.cs
@@ -1,14 +1,18 @@
+using System.Net.Http.Headers;
 using Microsoft.EntityFrameworkCore;
 using Microsoft.Extensions.Configuration;
 using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Options;
 using PBA.Application.Common.Interfaces;
+using PBA.Domain.Enums;
 using PBA.Infrastructure.Configuration;
-using PBA.Infrastructure.Data;
 using PBA.Infrastructure.Connectors;
+using PBA.Infrastructure.Data;
 using PBA.Infrastructure.Publishing;
 using PBA.Infrastructure.Seeding;
 using PBA.Infrastructure.Security;
 using PBA.Infrastructure.Services;
+using PBA.Infrastructure.Transformers;
 
 namespace PBA.Infrastructure;
 
@@ -43,19 +47,86 @@ public static class DependencyInjection
         services.AddHostedService<AiConnectionsService>();
 
         services.Configure<BlogConnectorOptions>(configuration.GetSection(BlogConnectorOptions.SectionName));
-        services.AddKeyedScoped<IPlatformConnector, BlogConnector>(PBA.Domain.Enums.Platform.Blog);
 
+        services.AddScoped<IContentPublisher, ContentPublisher>();
+        services.AddScoped<IContentScheduler, HangfireContentScheduler>();
+        services.AddHostedService<ScheduledPublishReconciler>();
+
+        services.AddPublishingDependencies(configuration);
+
+        services.AddScoped<IFeedSeedService, FeedSeedService>();
+
+        return services;
+    }
+
+    internal static IServiceCollection AddPublishingDependencies(
+        this IServiceCollection services,
+        IConfiguration configuration)
+    {
+        // Options
         services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));
+        services.Configure<MediumOptions>(configuration.GetSection(MediumOptions.SectionName));
+        services.Configure<SubstackOptions>(configuration.GetSection(SubstackOptions.SectionName));
         services.Configure<LinkedInOptions>(configuration.GetSection(LinkedInOptions.SectionName));
         services.Configure<TwitterOptions>(configuration.GetSection(TwitterOptions.SectionName));
+        services.Configure<TransformerOptions>(configuration.GetSection(TransformerOptions.SectionName));
+
+        // Security
         services.AddSingleton<ITokenEncryptor, TokenEncryptor>();
         services.AddScoped<IOAuthService, OAuthService>();
 
-        services.AddScoped<IContentPublisher, ContentPublisher>();
-        services.AddScoped<IContentScheduler, HangfireContentScheduler>();
-        services.AddHostedService<ScheduledPublishReconciler>();
+        // Content transformation
+        services.AddScoped<IContentTransformer, ContentTransformer>();
 
-        services.AddScoped<IFeedSeedService, FeedSeedService>();
+        // Keyed connectors
+        services.AddKeyedScoped<IPlatformConnector, BlogConnector>(Platform.Blog);
+        services.AddKeyedScoped<IPlatformConnector, MediumConnector>(Platform.Medium);
+        services.AddKeyedScoped<IPlatformConnector, LinkedInConnector>(Platform.LinkedIn);
+        services.AddKeyedScoped<IPlatformConnector, TwitterConnector>(Platform.Twitter);
+        services.AddKeyedScoped<IPlatformConnector, SubstackConnector>(Platform.Substack);
+
+        // Keyed formatters
+        services.AddKeyedScoped<IPlatformFormatter, BlogFormatter>(Platform.Blog);
+        services.AddKeyedScoped<IPlatformFormatter, MediumFormatter>(Platform.Medium);
+        services.AddKeyedScoped<IPlatformFormatter, LinkedInFormatter>(Platform.LinkedIn);
+        services.AddKeyedScoped<IPlatformFormatter, TwitterFormatter>(Platform.Twitter);
+        services.AddKeyedScoped<IPlatformFormatter, SubstackFormatter>(Platform.Substack);
+
+        // HttpClient factories
+        services.AddHttpClient<MediumConnector>(client =>
+        {
+            client.BaseAddress = new Uri("https://api.medium.com");
+            client.DefaultRequestHeaders.Accept.Add(
+                new MediaTypeWithQualityHeaderValue("application/json"));
+            client.DefaultRequestHeaders.Add("Accept-Charset", "utf-8");
+        });
+
+        services.AddHttpClient<LinkedInConnector>(client =>
+        {
+            client.BaseAddress = new Uri("https://api.linkedin.com");
+            client.DefaultRequestHeaders.Accept.Add(
+                new MediaTypeWithQualityHeaderValue("application/json"));
+        })
+        .AddStandardResilienceHandler();
+
+        services.AddHttpClient<TwitterConnector>(client =>
+        {
+            client.BaseAddress = new Uri("https://api.x.com");
+            client.DefaultRequestHeaders.Accept.Add(
+                new MediaTypeWithQualityHeaderValue("application/json"));
+        });
+
+        services.AddHttpClient<SubstackConnector>((sp, client) =>
+        {
+            var options = sp.GetRequiredService<IOptionsMonitor<SubstackOptions>>().CurrentValue;
+            var slug = string.IsNullOrEmpty(options.PublicationSlug) ? "default" : options.PublicationSlug;
+            client.BaseAddress = new Uri($"https://{slug}.substack.com");
+            client.DefaultRequestHeaders.Accept.Add(
+                new MediaTypeWithQualityHeaderValue("application/json"));
+        });
+
+        // Retry handler
+        services.AddScoped<IPublishRetryHandler, PublishRetryHandler>();
 
         return services;
     }
diff --git a/src/PBA.Infrastructure/PBA.Infrastructure.csproj b/src/PBA.Infrastructure/PBA.Infrastructure.csproj
index 71db133..5cd2a9f 100644
--- a/src/PBA.Infrastructure/PBA.Infrastructure.csproj
+++ b/src/PBA.Infrastructure/PBA.Infrastructure.csproj
@@ -20,6 +20,7 @@
     </PackageReference>
     <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.0" />
     <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
+    <PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="10.0.0" />
     <PackageReference Include="Ardalis.GuardClauses" Version="5.0.0" />
     <PackageReference Include="Hangfire.Core" Version="1.8.17" />
     <PackageReference Include="Hangfire.PostgreSql" Version="1.20.10" />
diff --git a/tests/PBA.Infrastructure.Tests/DependencyInjection/PublishingDependencyTests.cs b/tests/PBA.Infrastructure.Tests/DependencyInjection/PublishingDependencyTests.cs
new file mode 100644
index 0000000..9fd0a11
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/DependencyInjection/PublishingDependencyTests.cs
@@ -0,0 +1,154 @@
+using Hangfire;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Configuration;
+using Microsoft.Extensions.DependencyInjection;
+using Moq;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Connectors;
+using PBA.Infrastructure.Data;
+using PBA.Infrastructure.Publishing;
+using PBA.Infrastructure.Security;
+using PBA.Infrastructure.Transformers;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.DependencyInjection;
+
+public class PublishingDependencyTests : IDisposable
+{
+    private readonly ServiceProvider _provider;
+
+    public PublishingDependencyTests()
+    {
+        var config = new ConfigurationBuilder()
+            .AddInMemoryCollection(new Dictionary<string, string?>
+            {
+                ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
+                ["Publishing:Medium:Enabled"] = "true",
+                ["Publishing:Substack:Enabled"] = "true",
+                ["Publishing:Substack:PublicationSlug"] = "test",
+                ["Publishing:LinkedIn:ClientId"] = "test",
+                ["Publishing:LinkedIn:ClientSecret"] = "test",
+                ["Publishing:LinkedIn:RedirectUri"] = "https://localhost/callback",
+                ["Publishing:Twitter:ClientId"] = "test",
+                ["Publishing:Twitter:ClientSecret"] = "test",
+                ["Publishing:Twitter:RedirectUri"] = "https://localhost/callback",
+                ["BlogConnector:RepoPath"] = "/tmp",
+                ["BlogConnector:TemplatePath"] = "/tmp/template.html",
+                ["ContentTransformer:BaseUrl"] = "https://test.example.com",
+            })
+            .Build();
+
+        var services = new ServiceCollection();
+
+        services.AddDbContext<ApplicationDbContext>(o =>
+            o.UseInMemoryDatabase($"PublishingDI_{Guid.NewGuid()}"));
+        services.AddScoped<IAppDbContext>(sp =>
+            sp.GetRequiredService<ApplicationDbContext>());
+
+        services.AddSingleton(Mock.Of<IProcessRunner>());
+        services.AddSingleton(Mock.Of<ISidecarClient>());
+        services.AddSingleton(Mock.Of<IBackgroundJobClient>());
+        services.AddLogging();
+        services.AddHttpClient();
+
+        services.Configure<BlogConnectorOptions>(config.GetSection(BlogConnectorOptions.SectionName));
+        services.AddScoped<IContentPublisher, ContentPublisher>();
+
+        services.AddPublishingDependencies(config);
+
+        _provider = services.BuildServiceProvider();
+    }
+
+    [Theory]
+    [InlineData(Platform.Blog, typeof(BlogConnector))]
+    [InlineData(Platform.Medium, typeof(MediumConnector))]
+    [InlineData(Platform.LinkedIn, typeof(LinkedInConnector))]
+    [InlineData(Platform.Twitter, typeof(TwitterConnector))]
+    [InlineData(Platform.Substack, typeof(SubstackConnector))]
+    public void ResolveKeyedConnector_ReturnsCorrectType(Platform platform, Type expectedType)
+    {
+        using var scope = _provider.CreateScope();
+        var connector = scope.ServiceProvider.GetKeyedService<IPlatformConnector>(platform);
+
+        Assert.NotNull(connector);
+        Assert.IsType(expectedType, connector);
+    }
+
+    [Theory]
+    [InlineData(Platform.Blog, typeof(BlogFormatter))]
+    [InlineData(Platform.Medium, typeof(MediumFormatter))]
+    [InlineData(Platform.LinkedIn, typeof(LinkedInFormatter))]
+    [InlineData(Platform.Twitter, typeof(TwitterFormatter))]
+    [InlineData(Platform.Substack, typeof(SubstackFormatter))]
+    public void ResolveKeyedFormatter_ReturnsCorrectType(Platform platform, Type expectedType)
+    {
+        using var scope = _provider.CreateScope();
+        var formatter = scope.ServiceProvider.GetKeyedService<IPlatformFormatter>(platform);
+
+        Assert.NotNull(formatter);
+        Assert.IsType(expectedType, formatter);
+    }
+
+    [Fact]
+    public void ResolveContentTransformer_ReturnsContentTransformer()
+    {
+        using var scope = _provider.CreateScope();
+        var transformer = scope.ServiceProvider.GetRequiredService<IContentTransformer>();
+
+        Assert.IsType<ContentTransformer>(transformer);
+    }
+
+    [Fact]
+    public void ResolveTokenEncryptor_ReturnsSingleton()
+    {
+        using var scope = _provider.CreateScope();
+        var first = scope.ServiceProvider.GetRequiredService<ITokenEncryptor>();
+        var second = scope.ServiceProvider.GetRequiredService<ITokenEncryptor>();
+
+        Assert.Same(first, second);
+        Assert.IsType<TokenEncryptor>(first);
+    }
+
+    [Fact]
+    public void ResolveOAuthService_ReturnsOAuthService()
+    {
+        using var scope = _provider.CreateScope();
+        var service = scope.ServiceProvider.GetRequiredService<IOAuthService>();
+
+        Assert.IsType<OAuthService>(service);
+    }
+
+    [Fact]
+    public void ResolvePublishRetryHandler_ReturnsHandler()
+    {
+        using var scope = _provider.CreateScope();
+        var handler = scope.ServiceProvider.GetRequiredService<IPublishRetryHandler>();
+
+        Assert.IsType<PublishRetryHandler>(handler);
+    }
+
+    [Fact]
+    public void ResolveContentPublisher_ReturnsPublisher()
+    {
+        using var scope = _provider.CreateScope();
+        var publisher = scope.ServiceProvider.GetRequiredService<IContentPublisher>();
+
+        Assert.IsType<ContentPublisher>(publisher);
+    }
+
+    [Fact]
+    public void HttpClientFactory_CreatesClients()
+    {
+        using var scope = _provider.CreateScope();
+        var factory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
+
+        var client = factory.CreateClient(nameof(SubstackConnector));
+        Assert.NotNull(client);
+    }
+
+    public void Dispose()
+    {
+        _provider.Dispose();
+    }
+}
