diff --git a/planning/01-foundation/implementation/deep_implement_config.json b/planning/01-foundation/implementation/deep_implement_config.json
index 1c455c6..ab601d8 100644
--- a/planning/01-foundation/implementation/deep_implement_config.json
+++ b/planning/01-foundation/implementation/deep_implement_config.json
@@ -29,6 +29,10 @@
     "section-03-application": {
       "status": "complete",
       "commit_hash": "57d7f05"
+    },
+    "section-04-infrastructure": {
+      "status": "complete",
+      "commit_hash": "bd22618"
     }
   },
   "pre_commit": {
diff --git a/src/PersonalBrandAssistant.Api/Endpoints/ContentEndpoints.cs b/src/PersonalBrandAssistant.Api/Endpoints/ContentEndpoints.cs
new file mode 100644
index 0000000..6fb2295
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/Endpoints/ContentEndpoints.cs
@@ -0,0 +1,63 @@
+using MediatR;
+using PersonalBrandAssistant.Api.Extensions;
+using PersonalBrandAssistant.Application.Features.Content.Commands.CreateContent;
+using PersonalBrandAssistant.Application.Features.Content.Commands.DeleteContent;
+using PersonalBrandAssistant.Application.Features.Content.Commands.UpdateContent;
+using PersonalBrandAssistant.Application.Features.Content.Queries.GetContent;
+using PersonalBrandAssistant.Application.Features.Content.Queries.ListContent;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Api.Endpoints;
+
+public static class ContentEndpoints
+{
+    public static void MapContentEndpoints(this IEndpointRouteBuilder app)
+    {
+        var group = app.MapGroup("/api/content").WithTags("Content");
+
+        group.MapGet("/", ListContent);
+        group.MapGet("/{id:guid}", GetContent);
+        group.MapPost("/", CreateContent);
+        group.MapPut("/{id:guid}", UpdateContent);
+        group.MapDelete("/{id:guid}", DeleteContent);
+    }
+
+    private static async Task<IResult> ListContent(
+        ISender sender,
+        ContentType? contentType = null,
+        ContentStatus? status = null,
+        int pageSize = 20,
+        string? cursor = null)
+    {
+        var query = new ListContentQuery(contentType, status, Math.Min(pageSize, 50), cursor);
+        var result = await sender.Send(query);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> GetContent(ISender sender, Guid id)
+    {
+        var query = new GetContentQuery(id);
+        var result = await sender.Send(query);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> CreateContent(ISender sender, CreateContentCommand command)
+    {
+        var result = await sender.Send(command);
+        return result.ToCreatedHttpResult("/api/content");
+    }
+
+    private static async Task<IResult> UpdateContent(ISender sender, Guid id, UpdateContentCommand command)
+    {
+        var updatedCommand = command with { Id = id };
+        var result = await sender.Send(updatedCommand);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> DeleteContent(ISender sender, Guid id)
+    {
+        var command = new DeleteContentCommand(id);
+        var result = await sender.Send(command);
+        return result.ToHttpResult();
+    }
+}
diff --git a/src/PersonalBrandAssistant.Api/Endpoints/HealthEndpoints.cs b/src/PersonalBrandAssistant.Api/Endpoints/HealthEndpoints.cs
new file mode 100644
index 0000000..1122e8a
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/Endpoints/HealthEndpoints.cs
@@ -0,0 +1,20 @@
+using Microsoft.Extensions.Diagnostics.HealthChecks;
+
+namespace PersonalBrandAssistant.Api.Endpoints;
+
+public static class HealthEndpoints
+{
+    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
+    {
+        app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
+            .ExcludeFromDescription();
+
+        app.MapGet("/health/ready", async (HealthCheckService healthCheckService) =>
+        {
+            var report = await healthCheckService.CheckHealthAsync();
+            return report.Status == HealthStatus.Healthy
+                ? Results.Ok(new { status = "Ready" })
+                : Results.Json(new { status = "Unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
+        }).WithTags("Health");
+    }
+}
diff --git a/src/PersonalBrandAssistant.Api/Extensions/ResultExtensions.cs b/src/PersonalBrandAssistant.Api/Extensions/ResultExtensions.cs
new file mode 100644
index 0000000..0ec9684
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/Extensions/ResultExtensions.cs
@@ -0,0 +1,62 @@
+using Microsoft.AspNetCore.Mvc;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Api.Extensions;
+
+public static class ResultExtensions
+{
+    public static IResult ToHttpResult<T>(this Result<T> result)
+    {
+        if (result.IsSuccess)
+            return Results.Ok(result.Value);
+
+        return result.ErrorCode switch
+        {
+            ErrorCode.ValidationFailed => Results.Problem(new ProblemDetails
+            {
+                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
+                Title = "Validation Failed",
+                Status = StatusCodes.Status400BadRequest,
+                Detail = string.Join("; ", result.Errors),
+                Extensions = { ["errors"] = result.Errors },
+            }),
+            ErrorCode.NotFound => Results.Problem(new ProblemDetails
+            {
+                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
+                Title = "Not Found",
+                Status = StatusCodes.Status404NotFound,
+                Detail = result.Errors.FirstOrDefault() ?? "Resource not found.",
+            }),
+            ErrorCode.Conflict => Results.Problem(new ProblemDetails
+            {
+                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
+                Title = "Conflict",
+                Status = StatusCodes.Status409Conflict,
+                Detail = result.Errors.FirstOrDefault() ?? "A conflict occurred.",
+            }),
+            ErrorCode.Unauthorized => Results.Problem(new ProblemDetails
+            {
+                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
+                Title = "Unauthorized",
+                Status = StatusCodes.Status401Unauthorized,
+                Detail = result.Errors.FirstOrDefault() ?? "Unauthorized.",
+            }),
+            _ => Results.Problem(new ProblemDetails
+            {
+                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
+                Title = "Internal Server Error",
+                Status = StatusCodes.Status500InternalServerError,
+                Detail = "An unexpected error occurred.",
+            }),
+        };
+    }
+
+    public static IResult ToCreatedHttpResult<T>(this Result<T> result, string routePrefix)
+    {
+        if (!result.IsSuccess)
+            return result.ToHttpResult();
+
+        return Results.Created($"{routePrefix}/{result.Value}", result.Value);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Api/Handlers/GlobalExceptionHandler.cs b/src/PersonalBrandAssistant.Api/Handlers/GlobalExceptionHandler.cs
new file mode 100644
index 0000000..b20c2f0
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/Handlers/GlobalExceptionHandler.cs
@@ -0,0 +1,36 @@
+using Microsoft.AspNetCore.Diagnostics;
+using Microsoft.AspNetCore.Mvc;
+
+namespace PersonalBrandAssistant.Api.Handlers;
+
+public class GlobalExceptionHandler : IExceptionHandler
+{
+    private readonly ILogger<GlobalExceptionHandler> _logger;
+
+    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
+    {
+        _logger = logger;
+    }
+
+    public async ValueTask<bool> TryHandleAsync(
+        HttpContext httpContext,
+        Exception exception,
+        CancellationToken cancellationToken)
+    {
+        _logger.LogError(exception, "Unhandled exception occurred");
+
+        var problemDetails = new ProblemDetails
+        {
+            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
+            Title = "Internal Server Error",
+            Status = StatusCodes.Status500InternalServerError,
+            Detail = "An unexpected error occurred.",
+        };
+
+        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
+        httpContext.Response.ContentType = "application/problem+json";
+        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
+
+        return true;
+    }
+}
diff --git a/src/PersonalBrandAssistant.Api/Middleware/ApiKeyMiddleware.cs b/src/PersonalBrandAssistant.Api/Middleware/ApiKeyMiddleware.cs
new file mode 100644
index 0000000..d61850b
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/Middleware/ApiKeyMiddleware.cs
@@ -0,0 +1,50 @@
+using Microsoft.AspNetCore.Mvc;
+
+namespace PersonalBrandAssistant.Api.Middleware;
+
+public class ApiKeyMiddleware
+{
+    private const string ApiKeyHeaderName = "X-Api-Key";
+    private static readonly HashSet<string> ExemptPaths = ["/health"];
+
+    private readonly RequestDelegate _next;
+    private readonly string _apiKey;
+
+    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
+    {
+        _next = next;
+        _apiKey = configuration["ApiKey"]
+            ?? throw new InvalidOperationException("ApiKey configuration is required.");
+    }
+
+    public async Task InvokeAsync(HttpContext context)
+    {
+        if (IsExempt(context.Request.Path))
+        {
+            await _next(context);
+            return;
+        }
+
+        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedKey) ||
+            !string.Equals(providedKey, _apiKey, StringComparison.Ordinal))
+        {
+            var problemDetails = new ProblemDetails
+            {
+                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
+                Title = "Unauthorized",
+                Status = StatusCodes.Status401Unauthorized,
+                Detail = "Invalid or missing API key.",
+            };
+
+            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
+            context.Response.ContentType = "application/problem+json";
+            await context.Response.WriteAsJsonAsync(problemDetails);
+            return;
+        }
+
+        await _next(context);
+    }
+
+    private static bool IsExempt(PathString path) =>
+        ExemptPaths.Any(exempt => path.Equals(exempt, StringComparison.OrdinalIgnoreCase));
+}
diff --git a/src/PersonalBrandAssistant.Api/Program.cs b/src/PersonalBrandAssistant.Api/Program.cs
index 1760df1..fbede3c 100644
--- a/src/PersonalBrandAssistant.Api/Program.cs
+++ b/src/PersonalBrandAssistant.Api/Program.cs
@@ -1,6 +1,50 @@
+using PersonalBrandAssistant.Api.Endpoints;
+using PersonalBrandAssistant.Api.Handlers;
+using PersonalBrandAssistant.Api.Middleware;
+using PersonalBrandAssistant.Application;
+using PersonalBrandAssistant.Infrastructure;
+using Serilog;
+
 var builder = WebApplication.CreateBuilder(args);
+
+builder.Host.UseSerilog((context, loggerConfiguration) =>
+    loggerConfiguration.ReadFrom.Configuration(context.Configuration));
+
+builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
+builder.Services.AddProblemDetails();
+
+builder.Services.AddApplication();
+builder.Services.AddInfrastructure(builder.Configuration);
+
+builder.Services.AddCors(options =>
+{
+    options.AddPolicy("AllowAngularDev", policy =>
+    {
+        policy.WithOrigins("http://localhost:4200")
+              .AllowAnyHeader()
+              .AllowAnyMethod();
+    });
+});
+
+builder.Services.AddEndpointsApiExplorer();
+builder.Services.AddSwaggerGen();
+
 var app = builder.Build();
 
-app.MapGet("/", () => "Hello World!");
+app.UseExceptionHandler();
+app.UseSerilogRequestLogging();
+app.UseCors("AllowAngularDev");
+app.UseMiddleware<ApiKeyMiddleware>();
+
+if (app.Environment.IsDevelopment())
+{
+    app.UseSwagger();
+    app.UseSwaggerUI();
+}
+
+app.MapHealthEndpoints();
+app.MapContentEndpoints();
 
 app.Run();
+
+public partial class Program { }
diff --git a/src/PersonalBrandAssistant.Api/appsettings.json b/src/PersonalBrandAssistant.Api/appsettings.json
index 10f68b8..eb2babb 100644
--- a/src/PersonalBrandAssistant.Api/appsettings.json
+++ b/src/PersonalBrandAssistant.Api/appsettings.json
@@ -1,9 +1,28 @@
 {
-  "Logging": {
-    "LogLevel": {
+  "ConnectionStrings": {
+    "DefaultConnection": ""
+  },
+  "ApiKey": "",
+  "Serilog": {
+    "MinimumLevel": {
       "Default": "Information",
-      "Microsoft.AspNetCore": "Warning"
-    }
+      "Override": {
+        "Microsoft": "Warning",
+        "Microsoft.Hosting.Lifetime": "Information"
+      }
+    },
+    "WriteTo": [
+      {
+        "Name": "Console",
+        "Args": {
+          "formatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact"
+        }
+      }
+    ],
+    "Enrich": ["FromLogContext"]
+  },
+  "AuditLog": {
+    "RetentionDays": 90
   },
   "AllowedHosts": "*"
 }
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/ApiKeyMiddlewareTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/ApiKeyMiddlewareTests.cs
new file mode 100644
index 0000000..c8bc4b1
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/ApiKeyMiddlewareTests.cs
@@ -0,0 +1,77 @@
+using System.Net;
+using System.Text.Json;
+using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Api;
+
+public class ApiKeyMiddlewareTests : IClassFixture<PostgresFixture>, IAsyncLifetime
+{
+    private readonly PostgresFixture _fixture;
+    private CustomWebApplicationFactory _factory = null!;
+    private string _connectionString = null!;
+
+    public ApiKeyMiddlewareTests(PostgresFixture fixture)
+    {
+        _fixture = fixture;
+    }
+
+    public async Task InitializeAsync()
+    {
+        _connectionString = _fixture.GetUniqueConnectionString();
+        await using var conn = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
+        await conn.OpenAsync();
+        var dbName = new Npgsql.NpgsqlConnectionStringBuilder(_connectionString).Database;
+        await using var cmd = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
+        await cmd.ExecuteNonQueryAsync();
+        _factory = new CustomWebApplicationFactory(_connectionString);
+        await _factory.EnsureDatabaseCreatedAsync();
+    }
+
+    public async Task DisposeAsync()
+    {
+        await _factory.DisposeAsync();
+        await using var conn = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
+        await conn.OpenAsync();
+        var dbName = new Npgsql.NpgsqlConnectionStringBuilder(_connectionString).Database;
+        await using var cmd = new Npgsql.NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)", conn);
+        await cmd.ExecuteNonQueryAsync();
+    }
+
+    [Fact]
+    public async Task ValidApiKey_ReturnsSuccess()
+    {
+        using var client = _factory.CreateAuthenticatedClient();
+        var response = await client.GetAsync("/health/ready");
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task InvalidApiKey_Returns401()
+    {
+        using var client = _factory.CreateClient();
+        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");
+
+        var response = await client.GetAsync("/health/ready");
+        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
+
+        var body = await response.Content.ReadAsStringAsync();
+        using var json = JsonDocument.Parse(body);
+        Assert.Equal("Unauthorized", json.RootElement.GetProperty("title").GetString());
+    }
+
+    [Fact]
+    public async Task MissingApiKey_Returns401()
+    {
+        using var client = _factory.CreateClient();
+        var response = await client.GetAsync("/health/ready");
+        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task HealthLiveness_ExemptFromApiKey()
+    {
+        using var client = _factory.CreateClient();
+        var response = await client.GetAsync("/health");
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/ContentEndpointsTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/ContentEndpointsTests.cs
new file mode 100644
index 0000000..0f1734b
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/ContentEndpointsTests.cs
@@ -0,0 +1,128 @@
+using System.Net;
+using System.Net.Http.Json;
+using System.Text.Json;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Api;
+
+public class ContentEndpointsTests : IClassFixture<PostgresFixture>, IAsyncLifetime
+{
+    private readonly PostgresFixture _fixture;
+    private CustomWebApplicationFactory _factory = null!;
+    private HttpClient _client = null!;
+    private string _connectionString = null!;
+
+    public ContentEndpointsTests(PostgresFixture fixture)
+    {
+        _fixture = fixture;
+    }
+
+    public async Task InitializeAsync()
+    {
+        _connectionString = _fixture.GetUniqueConnectionString();
+        await using var conn = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
+        await conn.OpenAsync();
+        var dbName = new Npgsql.NpgsqlConnectionStringBuilder(_connectionString).Database;
+        await using var cmd = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
+        await cmd.ExecuteNonQueryAsync();
+
+        _factory = new CustomWebApplicationFactory(_connectionString);
+        await _factory.EnsureDatabaseCreatedAsync();
+        _client = _factory.CreateAuthenticatedClient();
+    }
+
+    public async Task DisposeAsync()
+    {
+        _client.Dispose();
+        await _factory.DisposeAsync();
+        await using var conn = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
+        await conn.OpenAsync();
+        var dbName = new Npgsql.NpgsqlConnectionStringBuilder(_connectionString).Database;
+        await using var cmd = new Npgsql.NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)", conn);
+        await cmd.ExecuteNonQueryAsync();
+    }
+
+    [Fact]
+    public async Task CreateContent_ValidBody_Returns201()
+    {
+        var request = new
+        {
+            ContentType = (int)ContentType.BlogPost,
+            Body = "Test blog post content",
+            Title = "Test Title",
+        };
+
+        var response = await _client.PostAsJsonAsync("/api/content", request);
+        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
+        Assert.NotNull(response.Headers.Location);
+    }
+
+    [Fact]
+    public async Task CreateContent_MissingBody_Returns400()
+    {
+        var request = new
+        {
+            ContentType = (int)ContentType.BlogPost,
+            Body = "",
+        };
+
+        var response = await _client.PostAsJsonAsync("/api/content", request);
+        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task GetContent_ExistingId_Returns200()
+    {
+        var createRequest = new
+        {
+            ContentType = (int)ContentType.SocialPost,
+            Body = "Test social post",
+        };
+        var createResponse = await _client.PostAsJsonAsync("/api/content", createRequest);
+        var contentId = await createResponse.Content.ReadFromJsonAsync<Guid>();
+
+        var response = await _client.GetAsync($"/api/content/{contentId}");
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task GetContent_NonExistentId_Returns404()
+    {
+        var response = await _client.GetAsync($"/api/content/{Guid.NewGuid()}");
+        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task ListContent_ReturnsPagedResult()
+    {
+        var request = new
+        {
+            ContentType = (int)ContentType.BlogPost,
+            Body = "List test content",
+        };
+        await _client.PostAsJsonAsync("/api/content", request);
+
+        var response = await _client.GetAsync("/api/content");
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+
+        var body = await response.Content.ReadAsStringAsync();
+        using var json = JsonDocument.Parse(body);
+        Assert.True(json.RootElement.TryGetProperty("items", out _));
+    }
+
+    [Fact]
+    public async Task DeleteContent_Returns200()
+    {
+        var createRequest = new
+        {
+            ContentType = (int)ContentType.SocialPost,
+            Body = "Content to delete",
+        };
+        var createResponse = await _client.PostAsJsonAsync("/api/content", createRequest);
+        var contentId = await createResponse.Content.ReadFromJsonAsync<Guid>();
+
+        var response = await _client.DeleteAsync($"/api/content/{contentId}");
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/CustomWebApplicationFactory.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/CustomWebApplicationFactory.cs
new file mode 100644
index 0000000..eacab21
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/CustomWebApplicationFactory.cs
@@ -0,0 +1,59 @@
+using Microsoft.AspNetCore.Hosting;
+using Microsoft.AspNetCore.Mvc.Testing;
+using Microsoft.AspNetCore.TestHost;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Configuration;
+using Microsoft.Extensions.DependencyInjection;
+using PersonalBrandAssistant.Infrastructure.Data;
+using PersonalBrandAssistant.Infrastructure.Services;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Api;
+
+public class CustomWebApplicationFactory : WebApplicationFactory<Program>
+{
+    private readonly string _connectionString;
+
+    public CustomWebApplicationFactory(string connectionString)
+    {
+        _connectionString = connectionString;
+    }
+
+    public const string TestApiKey = "test-api-key-12345";
+
+    protected override void ConfigureWebHost(IWebHostBuilder builder)
+    {
+        builder.UseEnvironment("Development");
+
+        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
+        builder.UseSetting("ApiKey", TestApiKey);
+        builder.UseSetting("AuditLog:RetentionDays", "90");
+
+        builder.ConfigureTestServices(services =>
+        {
+            // Remove hosted services that depend on DB schema existing at startup
+            RemoveService<DataSeeder>(services);
+            RemoveService<AuditLogCleanupService>(services);
+        });
+    }
+
+    private static void RemoveService<T>(IServiceCollection services)
+    {
+        var descriptors = services.Where(d => d.ImplementationType == typeof(T)).ToList();
+        foreach (var descriptor in descriptors)
+            services.Remove(descriptor);
+    }
+
+    public async Task EnsureDatabaseCreatedAsync()
+    {
+        using var scope = Services.CreateScope();
+        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
+        await context.Database.EnsureCreatedAsync();
+    }
+
+    public HttpClient CreateAuthenticatedClient()
+    {
+        var client = CreateClient();
+        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
+        return client;
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/GlobalExceptionHandlerTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/GlobalExceptionHandlerTests.cs
new file mode 100644
index 0000000..65ae293
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/GlobalExceptionHandlerTests.cs
@@ -0,0 +1,58 @@
+using System.Net;
+using System.Text.Json;
+using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Api;
+
+public class GlobalExceptionHandlerTests : IClassFixture<PostgresFixture>, IAsyncLifetime
+{
+    private readonly PostgresFixture _fixture;
+    private CustomWebApplicationFactory _factory = null!;
+    private string _connectionString = null!;
+
+    public GlobalExceptionHandlerTests(PostgresFixture fixture)
+    {
+        _fixture = fixture;
+    }
+
+    public async Task InitializeAsync()
+    {
+        _connectionString = _fixture.GetUniqueConnectionString();
+        await using var conn = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
+        await conn.OpenAsync();
+        var dbName = new Npgsql.NpgsqlConnectionStringBuilder(_connectionString).Database;
+        await using var cmd = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
+        await cmd.ExecuteNonQueryAsync();
+        _factory = new CustomWebApplicationFactory(_connectionString);
+        await _factory.EnsureDatabaseCreatedAsync();
+    }
+
+    public async Task DisposeAsync()
+    {
+        await _factory.DisposeAsync();
+        await using var conn = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
+        await conn.OpenAsync();
+        var dbName = new Npgsql.NpgsqlConnectionStringBuilder(_connectionString).Database;
+        await using var cmd = new Npgsql.NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)", conn);
+        await cmd.ExecuteNonQueryAsync();
+    }
+
+    [Fact]
+    public async Task UnhandledException_Returns500ProblemDetailsWithNoStackTrace()
+    {
+        using var client = _factory.CreateAuthenticatedClient();
+
+        var content = new StringContent("not json at all", System.Text.Encoding.UTF8, "application/json");
+        var response = await client.PostAsync("/api/content", content);
+
+        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
+
+        var body = await response.Content.ReadAsStringAsync();
+        using var json = JsonDocument.Parse(body);
+
+        Assert.Equal("Internal Server Error", json.RootElement.GetProperty("title").GetString());
+        Assert.Equal("An unexpected error occurred.", json.RootElement.GetProperty("detail").GetString());
+        // Ensure no stack trace leaked
+        Assert.False(json.RootElement.TryGetProperty("stackTrace", out _));
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/HealthEndpointTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/HealthEndpointTests.cs
new file mode 100644
index 0000000..fe1665d
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/HealthEndpointTests.cs
@@ -0,0 +1,54 @@
+using System.Net;
+using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Api;
+
+public class HealthEndpointTests : IClassFixture<PostgresFixture>, IAsyncLifetime
+{
+    private readonly PostgresFixture _fixture;
+    private CustomWebApplicationFactory _factory = null!;
+    private string _connectionString = null!;
+
+    public HealthEndpointTests(PostgresFixture fixture)
+    {
+        _fixture = fixture;
+    }
+
+    public async Task InitializeAsync()
+    {
+        _connectionString = _fixture.GetUniqueConnectionString();
+        await using var conn = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
+        await conn.OpenAsync();
+        var dbName = new Npgsql.NpgsqlConnectionStringBuilder(_connectionString).Database;
+        await using var cmd = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
+        await cmd.ExecuteNonQueryAsync();
+        _factory = new CustomWebApplicationFactory(_connectionString);
+        await _factory.EnsureDatabaseCreatedAsync();
+    }
+
+    public async Task DisposeAsync()
+    {
+        await _factory.DisposeAsync();
+        await using var conn = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
+        await conn.OpenAsync();
+        var dbName = new Npgsql.NpgsqlConnectionStringBuilder(_connectionString).Database;
+        await using var cmd = new Npgsql.NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)", conn);
+        await cmd.ExecuteNonQueryAsync();
+    }
+
+    [Fact]
+    public async Task HealthLiveness_Returns200()
+    {
+        using var client = _factory.CreateClient();
+        var response = await client.GetAsync("/health");
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task HealthReady_WithApiKey_Returns200()
+    {
+        using var client = _factory.CreateAuthenticatedClient();
+        var response = await client.GetAsync("/health/ready");
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/ResultToHttpMapperTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/ResultToHttpMapperTests.cs
new file mode 100644
index 0000000..f9f9d0a
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/ResultToHttpMapperTests.cs
@@ -0,0 +1,117 @@
+using System.Net;
+using System.Text.Json;
+using Microsoft.AspNetCore.Http;
+using Microsoft.AspNetCore.Http.HttpResults;
+using PersonalBrandAssistant.Api.Extensions;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Api;
+
+public class ResultToHttpMapperTests
+{
+    [Fact]
+    public void Success_MapsTo200WithValue()
+    {
+        var result = Result<string>.Success("hello");
+        var httpResult = result.ToHttpResult();
+
+        var okResult = Assert.IsType<Ok<string>>(httpResult);
+        Assert.Equal("hello", okResult.Value);
+    }
+
+    [Fact]
+    public void ValidationFailure_MapsTo400ProblemDetails()
+    {
+        var result = Result<string>.ValidationFailure(["Field is required", "Invalid format"]);
+        var httpResult = result.ToHttpResult();
+
+        var problemResult = Assert.IsType<ProblemHttpResult>(httpResult);
+        Assert.Equal(StatusCodes.Status400BadRequest, problemResult.StatusCode);
+        Assert.Equal("Validation Failed", problemResult.ProblemDetails.Title);
+        Assert.Contains("errors", problemResult.ProblemDetails.Extensions.Keys);
+    }
+
+    [Fact]
+    public void NotFound_MapsTo404ProblemDetails()
+    {
+        var result = Result<string>.NotFound("Item not found");
+        var httpResult = result.ToHttpResult();
+
+        var problemResult = Assert.IsType<ProblemHttpResult>(httpResult);
+        Assert.Equal(StatusCodes.Status404NotFound, problemResult.StatusCode);
+        Assert.Equal("Not Found", problemResult.ProblemDetails.Title);
+        Assert.Equal("Item not found", problemResult.ProblemDetails.Detail);
+    }
+
+    [Fact]
+    public void Conflict_MapsTo409ProblemDetails()
+    {
+        var result = Result<string>.Conflict("Version mismatch");
+        var httpResult = result.ToHttpResult();
+
+        var problemResult = Assert.IsType<ProblemHttpResult>(httpResult);
+        Assert.Equal(StatusCodes.Status409Conflict, problemResult.StatusCode);
+        Assert.Equal("Conflict", problemResult.ProblemDetails.Title);
+    }
+
+    [Fact]
+    public void Unauthorized_MapsTo401ProblemDetails()
+    {
+        var result = Result<string>.Failure(ErrorCode.Unauthorized, "Not authorized");
+        var httpResult = result.ToHttpResult();
+
+        var problemResult = Assert.IsType<ProblemHttpResult>(httpResult);
+        Assert.Equal(StatusCodes.Status401Unauthorized, problemResult.StatusCode);
+    }
+
+    [Fact]
+    public void InternalError_MapsTo500ProblemDetails()
+    {
+        var result = Result<string>.Failure(ErrorCode.InternalError, "Something broke");
+        var httpResult = result.ToHttpResult();
+
+        var problemResult = Assert.IsType<ProblemHttpResult>(httpResult);
+        Assert.Equal(StatusCodes.Status500InternalServerError, problemResult.StatusCode);
+        Assert.Equal("An unexpected error occurred.", problemResult.ProblemDetails.Detail);
+    }
+
+    [Fact]
+    public void AllErrors_IncludeRequiredRfc9457Fields()
+    {
+        var errorCodes = new[] { ErrorCode.ValidationFailed, ErrorCode.NotFound, ErrorCode.Conflict };
+
+        foreach (var errorCode in errorCodes)
+        {
+            var result = Result<string>.Failure(errorCode, "test error");
+            var httpResult = result.ToHttpResult();
+            var problemResult = Assert.IsType<ProblemHttpResult>(httpResult);
+
+            Assert.NotNull(problemResult.ProblemDetails.Type);
+            Assert.NotNull(problemResult.ProblemDetails.Title);
+            Assert.NotNull(problemResult.ProblemDetails.Status);
+            Assert.NotNull(problemResult.ProblemDetails.Detail);
+        }
+    }
+
+    [Fact]
+    public void ToCreatedHttpResult_Success_Returns201WithLocation()
+    {
+        var id = Guid.NewGuid();
+        var result = Result<Guid>.Success(id);
+        var httpResult = result.ToCreatedHttpResult("/api/content");
+
+        var createdResult = Assert.IsType<Created<Guid>>(httpResult);
+        Assert.Equal($"/api/content/{id}", createdResult.Location);
+    }
+
+    [Fact]
+    public void ToCreatedHttpResult_Failure_DelegatesToToHttpResult()
+    {
+        var result = Result<Guid>.NotFound("Not found");
+        var httpResult = result.ToCreatedHttpResult("/api/content");
+
+        var problemResult = Assert.IsType<ProblemHttpResult>(httpResult);
+        Assert.Equal(StatusCodes.Status404NotFound, problemResult.StatusCode);
+    }
+}
