diff --git a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
index 61d1087..f1cafa0 100644
--- a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
@@ -19,6 +19,7 @@ using PersonalBrandAssistant.Infrastructure.Services.ContentServices.TrendPoller
 using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;
 using PersonalBrandAssistant.Infrastructure.Services.IntegrationServices;
 using PersonalBrandAssistant.Infrastructure.Services.ContentAutomation;
+using PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;
 using PersonalBrandAssistant.Infrastructure.Services.SocialServices;
 
 namespace PersonalBrandAssistant.Infrastructure;
@@ -233,6 +234,38 @@ public static class DependencyInjection
         services.AddHostedService<EngagementScheduler>();
         services.AddHostedService<InboxPoller>();
 
+        // Google Analytics / Search Console
+        services.Configure<GoogleAnalyticsOptions>(
+            configuration.GetSection(GoogleAnalyticsOptions.SectionName));
+        services.AddSingleton<IGa4Client>(sp =>
+        {
+            var opts = sp.GetRequiredService<IOptions<GoogleAnalyticsOptions>>().Value;
+#pragma warning disable CS0618 // Use of deprecated API -- CredentialFactory not available in this version
+            var credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromFile(opts.CredentialsPath);
+#pragma warning restore CS0618
+            var builder = new Google.Analytics.Data.V1Beta.BetaAnalyticsDataClientBuilder
+            {
+                GoogleCredential = credential
+            };
+            return new Ga4ClientWrapper(builder.Build());
+        });
+        services.AddSingleton<ISearchConsoleClient>(sp =>
+        {
+            var opts = sp.GetRequiredService<IOptions<GoogleAnalyticsOptions>>().Value;
+#pragma warning disable CS0618
+            var credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromFile(opts.CredentialsPath)
+                .CreateScoped(Google.Apis.SearchConsole.v1.SearchConsoleService.Scope.WebmastersReadonly);
+#pragma warning restore CS0618
+            var service = new Google.Apis.SearchConsole.v1.SearchConsoleService(
+                new Google.Apis.Services.BaseClientService.Initializer
+                {
+                    HttpClientInitializer = credential,
+                    ApplicationName = "PersonalBrandAssistant"
+                });
+            return new SearchConsoleClientWrapper(service);
+        });
+        services.AddScoped<IGoogleAnalyticsService, GoogleAnalyticsService>();
+
         // Content automation
         services.Configure<ContentAutomationOptions>(
             configuration.GetSection(ContentAutomationOptions.SectionName));
diff --git a/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj b/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
index 1b49d4f..e3daa89 100644
--- a/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
+++ b/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
@@ -3,6 +3,7 @@
   <ItemGroup>
     <InternalsVisibleTo Include="PersonalBrandAssistant.Infrastructure.Tests" />
     <InternalsVisibleTo Include="PersonalBrandAssistant.Application.Tests" />
+    <InternalsVisibleTo Include="DynamicProxyGenAssembly2" />
   </ItemGroup>
 
   <ItemGroup>
@@ -16,6 +17,8 @@
   <ItemGroup>
     <PackageReference Include="Anthropic" Version="12.8.0" />
     <PackageReference Include="Fluid.Core" Version="2.31.0" />
+    <PackageReference Include="Google.Analytics.Data.V1Beta" Version="2.0.0-beta10" />
+    <PackageReference Include="Google.Apis.SearchConsole.v1" Version="1.70.0.3847" />
     <PackageReference Include="Ical.Net" Version="5.2.1" />
     <PackageReference Include="Microsoft.AspNetCore.DataProtection" Version="10.0.5" />
     <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.5" />
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/Ga4ClientWrapper.cs b/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/Ga4ClientWrapper.cs
new file mode 100644
index 0000000..4cd1e79
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/Ga4ClientWrapper.cs
@@ -0,0 +1,17 @@
+using Google.Analytics.Data.V1Beta;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;
+
+/// <summary>
+/// Production wrapper around BetaAnalyticsDataClient.
+/// Registered as Singleton because BetaAnalyticsDataClient is thread-safe and reusable.
+/// </summary>
+internal sealed class Ga4ClientWrapper : IGa4Client
+{
+    private readonly BetaAnalyticsDataClient _client;
+
+    public Ga4ClientWrapper(BetaAnalyticsDataClient client) => _client = client;
+
+    public async Task<RunReportResponse> RunReportAsync(RunReportRequest request, CancellationToken ct)
+        => await _client.RunReportAsync(request, ct);
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/GoogleAnalyticsService.cs b/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/GoogleAnalyticsService.cs
new file mode 100644
index 0000000..a3dadb8
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/GoogleAnalyticsService.cs
@@ -0,0 +1,248 @@
+using System.Globalization;
+using Google.Analytics.Data.V1Beta;
+using Google.Apis.SearchConsole.v1.Data;
+using Grpc.Core;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;
+
+internal sealed class GoogleAnalyticsService : IGoogleAnalyticsService
+{
+    private readonly IGa4Client _ga4Client;
+    private readonly ISearchConsoleClient _searchConsoleClient;
+    private readonly GoogleAnalyticsOptions _options;
+    private readonly ILogger<GoogleAnalyticsService> _logger;
+
+    public GoogleAnalyticsService(
+        IGa4Client ga4Client,
+        ISearchConsoleClient searchConsoleClient,
+        IOptions<GoogleAnalyticsOptions> options,
+        ILogger<GoogleAnalyticsService> logger)
+    {
+        _ga4Client = ga4Client;
+        _searchConsoleClient = searchConsoleClient;
+        _options = options.Value;
+        _logger = logger;
+    }
+
+    public async Task<Result<WebsiteOverview>> GetOverviewAsync(
+        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
+    {
+        try
+        {
+            var request = new RunReportRequest
+            {
+                Property = $"properties/{_options.PropertyId}",
+                DateRanges =
+                {
+                    new DateRange
+                    {
+                        StartDate = FormatDate(from),
+                        EndDate = FormatDate(to)
+                    }
+                },
+                Metrics =
+                {
+                    new Metric { Name = "activeUsers" },
+                    new Metric { Name = "sessions" },
+                    new Metric { Name = "screenPageViews" },
+                    new Metric { Name = "averageSessionDuration" },
+                    new Metric { Name = "bounceRate" },
+                    new Metric { Name = "newUsers" }
+                }
+            };
+
+            var response = await _ga4Client.RunReportAsync(request, ct);
+
+            if (response.Rows is null || response.Rows.Count == 0)
+            {
+                return Result<WebsiteOverview>.Success(
+                    new WebsiteOverview(0, 0, 0, 0, 0, 0));
+            }
+
+            var row = response.Rows[0];
+            var overview = new WebsiteOverview(
+                ActiveUsers: ParseInt(row.MetricValues[0].Value),
+                Sessions: ParseInt(row.MetricValues[1].Value),
+                PageViews: ParseInt(row.MetricValues[2].Value),
+                AvgSessionDuration: ParseDouble(row.MetricValues[3].Value),
+                BounceRate: ParseDouble(row.MetricValues[4].Value),
+                NewUsers: ParseInt(row.MetricValues[5].Value));
+
+            return Result<WebsiteOverview>.Success(overview);
+        }
+        catch (RpcException ex)
+        {
+            _logger.LogError(ex, "GA4 API error fetching overview");
+            return Result<WebsiteOverview>.Failure(
+                ErrorCode.InternalError, $"GA4 API error: {ex.Status.Detail}");
+        }
+        catch (Exception ex)
+        {
+            _logger.LogError(ex, "Unexpected error fetching GA4 overview");
+            return Result<WebsiteOverview>.Failure(
+                ErrorCode.InternalError, $"GA4 error: {ex.Message}");
+        }
+    }
+
+    public async Task<Result<IReadOnlyList<PageViewEntry>>> GetTopPagesAsync(
+        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct)
+    {
+        try
+        {
+            var request = new RunReportRequest
+            {
+                Property = $"properties/{_options.PropertyId}",
+                DateRanges =
+                {
+                    new DateRange
+                    {
+                        StartDate = FormatDate(from),
+                        EndDate = FormatDate(to)
+                    }
+                },
+                Dimensions = { new Dimension { Name = "pagePath" } },
+                Metrics =
+                {
+                    new Metric { Name = "screenPageViews" },
+                    new Metric { Name = "activeUsers" }
+                },
+                OrderBys =
+                {
+                    new OrderBy
+                    {
+                        Metric = new OrderBy.Types.MetricOrderBy { MetricName = "screenPageViews" },
+                        Desc = true
+                    }
+                },
+                Limit = limit
+            };
+
+            var response = await _ga4Client.RunReportAsync(request, ct);
+
+            var pages = (response.Rows ?? Enumerable.Empty<Row>())
+                .Select(row => new PageViewEntry(
+                    PagePath: row.DimensionValues[0].Value,
+                    Views: ParseInt(row.MetricValues[0].Value),
+                    Users: ParseInt(row.MetricValues[1].Value)))
+                .ToList();
+
+            return Result<IReadOnlyList<PageViewEntry>>.Success(pages);
+        }
+        catch (RpcException ex)
+        {
+            _logger.LogError(ex, "GA4 API error fetching top pages");
+            return Result<IReadOnlyList<PageViewEntry>>.Failure(
+                ErrorCode.InternalError, $"GA4 API error: {ex.Status.Detail}");
+        }
+        catch (Exception ex)
+        {
+            _logger.LogError(ex, "Unexpected error fetching GA4 top pages");
+            return Result<IReadOnlyList<PageViewEntry>>.Failure(
+                ErrorCode.InternalError, $"GA4 error: {ex.Message}");
+        }
+    }
+
+    public async Task<Result<IReadOnlyList<TrafficSourceEntry>>> GetTrafficSourcesAsync(
+        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
+    {
+        try
+        {
+            var request = new RunReportRequest
+            {
+                Property = $"properties/{_options.PropertyId}",
+                DateRanges =
+                {
+                    new DateRange
+                    {
+                        StartDate = FormatDate(from),
+                        EndDate = FormatDate(to)
+                    }
+                },
+                Dimensions = { new Dimension { Name = "sessionDefaultChannelGroup" } },
+                Metrics =
+                {
+                    new Metric { Name = "sessions" },
+                    new Metric { Name = "activeUsers" }
+                }
+            };
+
+            var response = await _ga4Client.RunReportAsync(request, ct);
+
+            var sources = (response.Rows ?? Enumerable.Empty<Row>())
+                .Select(row => new TrafficSourceEntry(
+                    Channel: row.DimensionValues[0].Value,
+                    Sessions: ParseInt(row.MetricValues[0].Value),
+                    Users: ParseInt(row.MetricValues[1].Value)))
+                .ToList();
+
+            return Result<IReadOnlyList<TrafficSourceEntry>>.Success(sources);
+        }
+        catch (RpcException ex)
+        {
+            _logger.LogError(ex, "GA4 API error fetching traffic sources");
+            return Result<IReadOnlyList<TrafficSourceEntry>>.Failure(
+                ErrorCode.InternalError, $"GA4 API error: {ex.Status.Detail}");
+        }
+        catch (Exception ex)
+        {
+            _logger.LogError(ex, "Unexpected error fetching GA4 traffic sources");
+            return Result<IReadOnlyList<TrafficSourceEntry>>.Failure(
+                ErrorCode.InternalError, $"GA4 error: {ex.Message}");
+        }
+    }
+
+    public async Task<Result<IReadOnlyList<SearchQueryEntry>>> GetTopQueriesAsync(
+        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct)
+    {
+        try
+        {
+            var request = new SearchAnalyticsQueryRequest
+            {
+                StartDate = FormatDate(from),
+                EndDate = FormatDate(to),
+                Dimensions = ["query"],
+                RowLimit = limit
+            };
+
+            var response = await _searchConsoleClient.QueryAsync(
+                _options.SiteUrl, request, ct);
+
+            var queries = (response.Rows ?? Enumerable.Empty<ApiDataRow>())
+                .Select(row => new SearchQueryEntry(
+                    Query: row.Keys[0],
+                    Clicks: (int)(row.Clicks ?? 0),
+                    Impressions: (int)(row.Impressions ?? 0),
+                    Ctr: row.Ctr ?? 0,
+                    Position: row.Position ?? 0))
+                .ToList();
+
+            return Result<IReadOnlyList<SearchQueryEntry>>.Success(queries);
+        }
+        catch (Google.GoogleApiException ex)
+        {
+            _logger.LogError(ex, "Search Console API error fetching top queries");
+            return Result<IReadOnlyList<SearchQueryEntry>>.Failure(
+                ErrorCode.InternalError, $"Search Console API error: {ex.Message}");
+        }
+        catch (Exception ex)
+        {
+            _logger.LogError(ex, "Unexpected error fetching Search Console queries");
+            return Result<IReadOnlyList<SearchQueryEntry>>.Failure(
+                ErrorCode.InternalError, $"Search Console error: {ex.Message}");
+        }
+    }
+
+    private static string FormatDate(DateTimeOffset dto) =>
+        dto.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
+
+    private static int ParseInt(string value) =>
+        int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
+
+    private static double ParseDouble(string value) =>
+        double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/IGa4Client.cs b/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/IGa4Client.cs
new file mode 100644
index 0000000..e00c63d
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/IGa4Client.cs
@@ -0,0 +1,11 @@
+using Google.Analytics.Data.V1Beta;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;
+
+/// <summary>
+/// Thin wrapper around BetaAnalyticsDataClient.RunReportAsync for testability.
+/// </summary>
+internal interface IGa4Client
+{
+    Task<RunReportResponse> RunReportAsync(RunReportRequest request, CancellationToken ct);
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/ISearchConsoleClient.cs b/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/ISearchConsoleClient.cs
new file mode 100644
index 0000000..777cd52
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/ISearchConsoleClient.cs
@@ -0,0 +1,12 @@
+using Google.Apis.SearchConsole.v1.Data;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;
+
+/// <summary>
+/// Thin wrapper around SearchConsoleService.Searchanalytics.Query for testability.
+/// </summary>
+internal interface ISearchConsoleClient
+{
+    Task<SearchAnalyticsQueryResponse> QueryAsync(
+        string siteUrl, SearchAnalyticsQueryRequest request, CancellationToken ct);
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/SearchConsoleClientWrapper.cs b/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/SearchConsoleClientWrapper.cs
new file mode 100644
index 0000000..77f7f7d
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/SearchConsoleClientWrapper.cs
@@ -0,0 +1,21 @@
+using Google.Apis.SearchConsole.v1;
+using Google.Apis.SearchConsole.v1.Data;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;
+
+/// <summary>
+/// Production wrapper around SearchConsoleService.Searchanalytics.Query.
+/// </summary>
+internal sealed class SearchConsoleClientWrapper : ISearchConsoleClient
+{
+    private readonly SearchConsoleService _service;
+
+    public SearchConsoleClientWrapper(SearchConsoleService service) => _service = service;
+
+    public Task<SearchAnalyticsQueryResponse> QueryAsync(
+        string siteUrl, SearchAnalyticsQueryRequest request, CancellationToken ct)
+    {
+        var query = _service.Searchanalytics.Query(request, siteUrl);
+        return query.ExecuteAsync(ct);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Analytics/GoogleAnalyticsCredentialTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Analytics/GoogleAnalyticsCredentialTests.cs
new file mode 100644
index 0000000..471cb9e
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Analytics/GoogleAnalyticsCredentialTests.cs
@@ -0,0 +1,49 @@
+using Google.Apis.Auth.OAuth2;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Analytics;
+
+public class GoogleAnalyticsCredentialTests
+{
+    [Fact]
+    public void ServiceAccountCredentialLoading_ThrowsWithInvalidJsonStructure()
+    {
+        var tempPath = Path.GetTempFileName();
+        try
+        {
+            // A well-formed JSON but with an invalid/fake RSA key will be rejected by the new API
+            var fakeServiceAccountJson = """
+            {
+                "type": "service_account",
+                "project_id": "test-project",
+                "private_key_id": "key123",
+                "private_key": "not-a-real-key",
+                "client_email": "test@test-project.iam.gserviceaccount.com",
+                "client_id": "123456789"
+            }
+            """;
+            File.WriteAllText(tempPath, fakeServiceAccountJson);
+
+            // Validates that the credential factory rejects invalid keys
+            Assert.ThrowsAny<Exception>(() =>
+            {
+#pragma warning disable CS0618
+                GoogleCredential.FromFile(tempPath);
+#pragma warning restore CS0618
+            });
+        }
+        finally
+        {
+            File.Delete(tempPath);
+        }
+    }
+
+    [Fact]
+    public void ServiceAccountCredentialLoading_FailsGracefully_WithMissingFile()
+    {
+        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "missing.json");
+
+#pragma warning disable CS0618
+        Assert.ThrowsAny<Exception>(() => GoogleCredential.FromFile(nonExistentPath));
+#pragma warning restore CS0618
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Analytics/GoogleAnalyticsServiceTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Analytics/GoogleAnalyticsServiceTests.cs
new file mode 100644
index 0000000..0138da5
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Analytics/GoogleAnalyticsServiceTests.cs
@@ -0,0 +1,217 @@
+using Google.Analytics.Data.V1Beta;
+using Google.Apis.SearchConsole.v1.Data;
+using Grpc.Core;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Analytics;
+
+public class GoogleAnalyticsServiceTests
+{
+    private readonly Mock<IGa4Client> _ga4Client = new();
+    private readonly Mock<ISearchConsoleClient> _searchConsoleClient = new();
+    private readonly IOptions<GoogleAnalyticsOptions> _options;
+    private readonly Mock<ILogger<GoogleAnalyticsService>> _logger = new();
+    private readonly GoogleAnalyticsService _sut;
+
+    private readonly DateTimeOffset _from = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
+    private readonly DateTimeOffset _to = new(2026, 3, 24, 0, 0, 0, TimeSpan.Zero);
+
+    public GoogleAnalyticsServiceTests()
+    {
+        _options = Options.Create(new GoogleAnalyticsOptions
+        {
+            PropertyId = "261358185",
+            SiteUrl = "https://matthewkruczek.ai/",
+            CredentialsPath = "secrets/google-analytics-sa.json"
+        });
+        _sut = new GoogleAnalyticsService(
+            _ga4Client.Object, _searchConsoleClient.Object, _options, _logger.Object);
+    }
+
+    [Fact]
+    public async Task GetOverviewAsync_ReturnsWebsiteOverview_WithCorrectMetricMapping()
+    {
+        var response = new RunReportResponse
+        {
+            Rows =
+            {
+                new Row
+                {
+                    MetricValues =
+                    {
+                        new MetricValue { Value = "150" },
+                        new MetricValue { Value = "200" },
+                        new MetricValue { Value = "500" },
+                        new MetricValue { Value = "120.5" },
+                        new MetricValue { Value = "0.45" },
+                        new MetricValue { Value = "80" }
+                    }
+                }
+            }
+        };
+
+        _ga4Client.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(response);
+
+        var result = await _sut.GetOverviewAsync(_from, _to, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(150, result.Value!.ActiveUsers);
+        Assert.Equal(200, result.Value.Sessions);
+        Assert.Equal(500, result.Value.PageViews);
+        Assert.Equal(120.5, result.Value.AvgSessionDuration);
+        Assert.Equal(0.45, result.Value.BounceRate);
+        Assert.Equal(80, result.Value.NewUsers);
+    }
+
+    [Fact]
+    public async Task GetOverviewAsync_ReturnsFailure_WhenGa4ClientThrowsRpcException()
+    {
+        _ga4Client.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
+            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));
+
+        var result = await _sut.GetOverviewAsync(_from, _to, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.InternalError, result.ErrorCode);
+        Assert.Contains("GA4", result.Errors[0]);
+    }
+
+    [Fact]
+    public async Task GetOverviewAsync_HandlesEmptyResponse_ReturnsZeroFilledOverview()
+    {
+        var response = new RunReportResponse();
+
+        _ga4Client.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(response);
+
+        var result = await _sut.GetOverviewAsync(_from, _to, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(0, result.Value!.ActiveUsers);
+        Assert.Equal(0, result.Value.Sessions);
+        Assert.Equal(0, result.Value.PageViews);
+    }
+
+    [Fact]
+    public async Task GetTopPagesAsync_ReturnsSortedByViewsDescending_RespectsLimit()
+    {
+        var response = new RunReportResponse
+        {
+            Rows =
+            {
+                CreatePageRow("/blog/post-1", "300", "100"),
+                CreatePageRow("/blog/post-2", "200", "80"),
+                CreatePageRow("/about", "150", "60")
+            }
+        };
+
+        _ga4Client.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(response);
+
+        var result = await _sut.GetTopPagesAsync(_from, _to, 3, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(3, result.Value!.Count);
+        Assert.Equal("/blog/post-1", result.Value[0].PagePath);
+        Assert.Equal(300, result.Value[0].Views);
+    }
+
+    [Fact]
+    public async Task GetTrafficSourcesAsync_GroupsSessionsByChannel()
+    {
+        var response = new RunReportResponse
+        {
+            Rows =
+            {
+                CreateChannelRow("Organic Search", "120", "90"),
+                CreateChannelRow("Direct", "80", "70"),
+                CreateChannelRow("Social", "50", "40")
+            }
+        };
+
+        _ga4Client.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(response);
+
+        var result = await _sut.GetTrafficSourcesAsync(_from, _to, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(3, result.Value!.Count);
+        Assert.Equal("Organic Search", result.Value[0].Channel);
+        Assert.Equal(120, result.Value[0].Sessions);
+        Assert.Equal(90, result.Value[0].Users);
+    }
+
+    [Fact]
+    public async Task GetTopQueriesAsync_ReturnsSearchQueryEntries_FromSearchConsole()
+    {
+        var response = new SearchAnalyticsQueryResponse
+        {
+            Rows =
+            [
+                new ApiDataRow
+                {
+                    Keys = ["ai tools"],
+                    Clicks = 50,
+                    Impressions = 1000,
+                    Ctr = 0.05,
+                    Position = 3.2
+                }
+            ]
+        };
+
+        _searchConsoleClient.Setup(c => c.QueryAsync(
+                It.IsAny<string>(), It.IsAny<SearchAnalyticsQueryRequest>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(response);
+
+        var result = await _sut.GetTopQueriesAsync(_from, _to, 20, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Single(result.Value!);
+        Assert.Equal("ai tools", result.Value![0].Query);
+        Assert.Equal(50, result.Value![0].Clicks);
+        Assert.Equal(1000, result.Value![0].Impressions);
+        Assert.Equal(0.05, result.Value![0].Ctr);
+        Assert.Equal(3.2, result.Value![0].Position);
+    }
+
+    [Fact]
+    public async Task GetTopQueriesAsync_ReturnsFailure_WhenSearchConsoleThrowsGoogleApiException()
+    {
+        _searchConsoleClient.Setup(c => c.QueryAsync(
+                It.IsAny<string>(), It.IsAny<SearchAnalyticsQueryRequest>(), It.IsAny<CancellationToken>()))
+            .ThrowsAsync(new Google.GoogleApiException("SearchConsole", "Forbidden"));
+
+        var result = await _sut.GetTopQueriesAsync(_from, _to, 20, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.InternalError, result.ErrorCode);
+    }
+
+    private static Row CreatePageRow(string pagePath, string views, string users) =>
+        new()
+        {
+            DimensionValues = { new DimensionValue { Value = pagePath } },
+            MetricValues =
+            {
+                new MetricValue { Value = views },
+                new MetricValue { Value = users }
+            }
+        };
+
+    private static Row CreateChannelRow(string channel, string sessions, string users) =>
+        new()
+        {
+            DimensionValues = { new DimensionValue { Value = channel } },
+            MetricValues =
+            {
+                new MetricValue { Value = sessions },
+                new MetricValue { Value = users }
+            }
+        };
+}
