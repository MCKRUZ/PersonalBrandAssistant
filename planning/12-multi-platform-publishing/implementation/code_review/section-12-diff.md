diff --git a/src/PBA.Api/Endpoints/ContentEndpoints.cs b/src/PBA.Api/Endpoints/ContentEndpoints.cs
index 255b2f7..582f375 100644
--- a/src/PBA.Api/Endpoints/ContentEndpoints.cs
+++ b/src/PBA.Api/Endpoints/ContentEndpoints.cs
@@ -110,7 +110,7 @@ public static class ContentEndpoints
 
         group.MapPut("/{id:guid}/schedule", async (Guid id, ScheduleContentRequest body, ISender sender, CancellationToken ct) =>
         {
-            var command = new ScheduleContent.Command(id, body.ScheduledAt);
+            var command = new ScheduleContent.Command(id, body.ScheduledAt, body.TargetPlatforms);
             var result = await sender.Send(command, ct);
             return result.ToApiResult();
         });
@@ -121,9 +121,24 @@ public static class ContentEndpoints
             return result.ToApiResult();
         });
 
-        group.MapPost("/{id:guid}/publish", async (Guid id, ISender sender, CancellationToken ct) =>
+        group.MapPost("/{id:guid}/publish", async (Guid id, PublishContentRequest? body, ISender sender, CancellationToken ct) =>
         {
-            var result = await sender.Send(new PublishContent.Command(id), ct);
+            var result = await sender.Send(new PublishContent.Command(id, body?.TargetPlatforms), ct);
+            return result.ToApiResult();
+        });
+
+        group.MapGet("/{id:guid}/publish-status", async (Guid id, ISender sender, CancellationToken ct) =>
+        {
+            var result = await sender.Send(new GetPublishStatus.Query(id), ct);
+            return result.ToApiResult();
+        });
+
+        group.MapPost("/{id:guid}/retry/{platform}", async (Guid id, string platform, ISender sender, CancellationToken ct) =>
+        {
+            if (!Enum.TryParse<Platform>(platform, ignoreCase: true, out var targetPlatform))
+                return Results.BadRequest("Invalid platform");
+
+            var result = await sender.Send(new RetryPlatformPublish.Command(id, targetPlatform), ct);
             return result.ToApiResult();
         });
 
diff --git a/src/PBA.Api/Endpoints/OAuthEndpoints.cs b/src/PBA.Api/Endpoints/OAuthEndpoints.cs
new file mode 100644
index 0000000..a899b91
--- /dev/null
+++ b/src/PBA.Api/Endpoints/OAuthEndpoints.cs
@@ -0,0 +1,100 @@
+using System.Security;
+using Microsoft.EntityFrameworkCore;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Enums;
+
+namespace PBA.Api.Endpoints;
+
+public static class OAuthEndpoints
+{
+    private static readonly HashSet<Platform> OAuthPlatforms = [Platform.LinkedIn, Platform.Twitter];
+
+    public static void MapOAuthEndpoints(this IEndpointRouteBuilder app)
+    {
+        var group = app.MapGroup("/api/auth").WithTags("OAuth");
+
+        group.MapGet("/{platform}/authorize", async (
+            string platform,
+            IOAuthService oauthService,
+            CancellationToken ct) =>
+        {
+            if (!Enum.TryParse<Platform>(platform, ignoreCase: true, out var p))
+                return Results.BadRequest("Invalid platform");
+
+            if (!OAuthPlatforms.Contains(p))
+                return Results.BadRequest($"{p} does not support OAuth. Use credential storage instead.");
+
+            var authUrl = await oauthService.GetAuthorizationUrlAsync(p, ct);
+            return Results.Redirect(authUrl);
+        });
+
+        group.MapGet("/{platform}/callback", async (
+            string platform,
+            string code,
+            string state,
+            IOAuthService oauthService,
+            CancellationToken ct) =>
+        {
+            if (!Enum.TryParse<Platform>(platform, ignoreCase: true, out var p))
+                return Results.BadRequest("Invalid platform");
+
+            try
+            {
+                await oauthService.ExchangeCodeAsync(p, code, state, ct);
+                return Results.Redirect($"/settings/platforms?connected={p}");
+            }
+            catch (SecurityException)
+            {
+                return Results.Forbid();
+            }
+            catch
+            {
+                return Results.Redirect($"/settings/platforms?error=auth_failed");
+            }
+        });
+
+        group.MapGet("/{platform}/status", async (
+            string platform,
+            IAppDbContext db,
+            CancellationToken ct) =>
+        {
+            if (!Enum.TryParse<Platform>(platform, ignoreCase: true, out var p))
+                return Results.BadRequest("Invalid platform");
+
+            var credential = await db.PlatformCredentials
+                .FirstOrDefaultAsync(c => c.Platform == p && c.IsActive, ct);
+
+            if (credential is null)
+                return Results.Ok(new { status = "NotConfigured" });
+
+            if (credential.AccessTokenExpiresAt.HasValue &&
+                credential.AccessTokenExpiresAt.Value < DateTimeOffset.UtcNow)
+                return Results.Ok(new { status = "Expired" });
+
+            return Results.Ok(new
+            {
+                status = "Connected",
+                expiresAt = credential.AccessTokenExpiresAt
+            });
+        });
+
+        group.MapDelete("/{platform}", async (
+            string platform,
+            IAppDbContext db,
+            CancellationToken ct) =>
+        {
+            if (!Enum.TryParse<Platform>(platform, ignoreCase: true, out var p))
+                return Results.BadRequest("Invalid platform");
+
+            var credential = await db.PlatformCredentials
+                .FirstOrDefaultAsync(c => c.Platform == p, ct);
+
+            if (credential is null)
+                return Results.NotFound();
+
+            db.PlatformCredentials.Remove(credential);
+            await db.SaveChangesAsync(ct);
+            return Results.Ok();
+        });
+    }
+}
diff --git a/src/PBA.Api/Endpoints/PlatformEndpoints.cs b/src/PBA.Api/Endpoints/PlatformEndpoints.cs
new file mode 100644
index 0000000..5d47184
--- /dev/null
+++ b/src/PBA.Api/Endpoints/PlatformEndpoints.cs
@@ -0,0 +1,108 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Features.Content.Dtos;
+using PBA.Domain.Enums;
+
+namespace PBA.Api.Endpoints;
+
+public static class PlatformEndpoints
+{
+    private static readonly Platform[] SupportedPlatforms =
+        [Platform.Blog, Platform.Medium, Platform.Substack, Platform.LinkedIn, Platform.Twitter];
+
+    public static void MapPlatformEndpoints(this IEndpointRouteBuilder app)
+    {
+        var group = app.MapGroup("/api/platforms").WithTags("Platforms");
+
+        group.MapGet("/", async (
+            IAppDbContext db,
+            IServiceProvider sp,
+            CancellationToken ct) =>
+        {
+            var credentials = await db.PlatformCredentials
+                .Where(c => c.IsActive)
+                .ToListAsync(ct);
+
+            var lastPublishDates = await db.ContentPlatformPublishes
+                .Where(p => p.Status == PublishStatus.Published)
+                .GroupBy(p => p.Platform)
+                .Select(g => new { Platform = g.Key, LastPublish = g.Max(p => p.PublishedAt) })
+                .ToDictionaryAsync(x => x.Platform, x => x.LastPublish, ct);
+
+            var result = SupportedPlatforms.Select(platform =>
+            {
+                var cred = credentials.FirstOrDefault(c => c.Platform == platform);
+                var connector = sp.GetKeyedService<IPlatformConnector>(platform);
+
+                var status = "NotConfigured";
+                if (cred is not null)
+                {
+                    status = cred.AccessTokenExpiresAt.HasValue &&
+                             cred.AccessTokenExpiresAt.Value < DateTimeOffset.UtcNow
+                        ? "Expired"
+                        : "Connected";
+                }
+
+                return new PlatformStatusDto
+                {
+                    Platform = platform,
+                    IsConnected = cred is not null && status == "Connected",
+                    Status = status,
+                    ExpiresAt = cred?.AccessTokenExpiresAt,
+                    LastPublishDate = lastPublishDates.GetValueOrDefault(platform),
+                    Capabilities = connector?.GetCapabilities()
+                };
+            }).ToList();
+
+            return Results.Ok(result);
+        });
+
+        group.MapPost("/{platform}/credentials", async (
+            string platform,
+            StoreCredentialsRequest body,
+            IAppDbContext db,
+            ITokenEncryptor encryptor,
+            CancellationToken ct) =>
+        {
+            if (!Enum.TryParse<Platform>(platform, ignoreCase: true, out var p))
+                return Results.BadRequest("Invalid platform");
+
+            switch (p)
+            {
+                case Platform.Medium:
+                    if (string.IsNullOrWhiteSpace(body.Token))
+                        return Results.BadRequest("Token is required for Medium");
+                    break;
+                case Platform.Substack:
+                    if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password))
+                        return Results.BadRequest("Email and password are required for Substack");
+                    break;
+                default:
+                    return Results.BadRequest($"{p} uses OAuth. Use /api/auth/{p}/authorize instead.");
+            }
+
+            var existing = await db.PlatformCredentials
+                .FirstOrDefaultAsync(c => c.Platform == p, ct);
+
+            if (existing is not null)
+                db.PlatformCredentials.Remove(existing);
+
+            var credential = new Domain.Entities.PlatformCredential
+            {
+                Platform = p,
+                IsActive = true,
+                EncryptedAccessToken = string.Empty
+            };
+
+            if (p == Platform.Medium)
+            {
+                credential.EncryptedIntegrationToken = encryptor.Encrypt(body.Token!);
+            }
+
+            db.PlatformCredentials.Add(credential);
+            await db.SaveChangesAsync(ct);
+            return Results.Ok();
+        });
+    }
+}
diff --git a/src/PBA.Api/Program.cs b/src/PBA.Api/Program.cs
index 7a49565..e25977e 100644
--- a/src/PBA.Api/Program.cs
+++ b/src/PBA.Api/Program.cs
@@ -46,6 +46,8 @@ app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp =
 app.MapIdeaEndpoints();
 app.MapIdeaSourceEndpoints();
 app.MapContentEndpoints();
+app.MapOAuthEndpoints();
+app.MapPlatformEndpoints();
 app.MapFeedEndpoints();
 
 app.MapHub<ContentHub>("/hubs/content");
diff --git a/src/PBA.Application/Features/Content/Commands/RetryPlatformPublish.cs b/src/PBA.Application/Features/Content/Commands/RetryPlatformPublish.cs
new file mode 100644
index 0000000..3329614
--- /dev/null
+++ b/src/PBA.Application/Features/Content/Commands/RetryPlatformPublish.cs
@@ -0,0 +1,33 @@
+using MediatR;
+using Microsoft.EntityFrameworkCore;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Common;
+using PBA.Domain.Enums;
+
+namespace PBA.Application.Features.Content.Commands;
+
+public static class RetryPlatformPublish
+{
+    public record Command(Guid ContentId, Platform Platform) : IRequest<Result>;
+
+    internal sealed class Handler(
+        IAppDbContext db,
+        IPublishRetryHandler retryHandler) : IRequestHandler<Command, Result>
+    {
+        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
+        {
+            var record = await db.ContentPlatformPublishes
+                .FirstOrDefaultAsync(p =>
+                    p.ContentId == request.ContentId
+                    && p.Platform == request.Platform
+                    && p.Status == PublishStatus.Failed,
+                    cancellationToken);
+
+            if (record is null)
+                return Result.NotFound($"No failed publish found for {request.Platform}");
+
+            await retryHandler.RetryAsync(record.Id, cancellationToken);
+            return Result.Success();
+        }
+    }
+}
diff --git a/src/PBA.Application/Features/Content/Commands/ScheduleContent.cs b/src/PBA.Application/Features/Content/Commands/ScheduleContent.cs
index 8ff2b7f..49c2d82 100644
--- a/src/PBA.Application/Features/Content/Commands/ScheduleContent.cs
+++ b/src/PBA.Application/Features/Content/Commands/ScheduleContent.cs
@@ -8,7 +8,7 @@ namespace PBA.Application.Features.Content.Commands;
 
 public static class ScheduleContent
 {
-    public record Command(Guid ContentId, DateTimeOffset ScheduledAt) : IRequest<Result>;
+    public record Command(Guid ContentId, DateTimeOffset ScheduledAt, IReadOnlyList<Platform>? TargetPlatforms = null) : IRequest<Result>;
 
     internal sealed class Handler(IAppDbContext db, IContentScheduler scheduler) : IRequestHandler<Command, Result>
     {
@@ -20,6 +20,9 @@ public static class ScheduleContent
 
             content.ScheduledAt = request.ScheduledAt;
 
+            if (request.TargetPlatforms is { Count: > 0 })
+                content.TargetPlatforms = request.TargetPlatforms.ToList();
+
             var machine = ContentStateMachine.Create(content);
             try
             {
diff --git a/src/PBA.Application/Features/Content/Dtos/CreateContentRequest.cs b/src/PBA.Application/Features/Content/Dtos/CreateContentRequest.cs
index 83a6cf6..3172764 100644
--- a/src/PBA.Application/Features/Content/Dtos/CreateContentRequest.cs
+++ b/src/PBA.Application/Features/Content/Dtos/CreateContentRequest.cs
@@ -9,4 +9,5 @@ public record CreateContentRequest
     public Platform PrimaryPlatform { get; init; }
     public Guid? SourceIdeaId { get; init; }
     public IReadOnlyList<string> Tags { get; init; } = [];
+    public IReadOnlyList<Platform>? TargetPlatforms { get; init; }
 }
diff --git a/src/PBA.Application/Features/Content/Dtos/PlatformStatusDto.cs b/src/PBA.Application/Features/Content/Dtos/PlatformStatusDto.cs
new file mode 100644
index 0000000..cf6abea
--- /dev/null
+++ b/src/PBA.Application/Features/Content/Dtos/PlatformStatusDto.cs
@@ -0,0 +1,14 @@
+using PBA.Application.Common.Models;
+using PBA.Domain.Enums;
+
+namespace PBA.Application.Features.Content.Dtos;
+
+public record PlatformStatusDto
+{
+    public Platform Platform { get; init; }
+    public bool IsConnected { get; init; }
+    public string Status { get; init; } = "NotConfigured";
+    public DateTimeOffset? ExpiresAt { get; init; }
+    public DateTimeOffset? LastPublishDate { get; init; }
+    public PlatformCapabilities? Capabilities { get; init; }
+}
diff --git a/src/PBA.Application/Features/Content/Dtos/PublishContentRequest.cs b/src/PBA.Application/Features/Content/Dtos/PublishContentRequest.cs
new file mode 100644
index 0000000..258471e
--- /dev/null
+++ b/src/PBA.Application/Features/Content/Dtos/PublishContentRequest.cs
@@ -0,0 +1,8 @@
+using PBA.Domain.Enums;
+
+namespace PBA.Application.Features.Content.Dtos;
+
+public record PublishContentRequest
+{
+    public IReadOnlyList<Platform>? TargetPlatforms { get; init; }
+}
diff --git a/src/PBA.Application/Features/Content/Dtos/PublishStatusDto.cs b/src/PBA.Application/Features/Content/Dtos/PublishStatusDto.cs
new file mode 100644
index 0000000..285a2bb
--- /dev/null
+++ b/src/PBA.Application/Features/Content/Dtos/PublishStatusDto.cs
@@ -0,0 +1,7 @@
+namespace PBA.Application.Features.Content.Dtos;
+
+public record PublishStatusDto
+{
+    public Guid ContentId { get; init; }
+    public IReadOnlyList<PlatformPublishDto> Platforms { get; init; } = [];
+}
diff --git a/src/PBA.Application/Features/Content/Dtos/RetryPublishRequest.cs b/src/PBA.Application/Features/Content/Dtos/RetryPublishRequest.cs
new file mode 100644
index 0000000..14c7f44
--- /dev/null
+++ b/src/PBA.Application/Features/Content/Dtos/RetryPublishRequest.cs
@@ -0,0 +1,8 @@
+using PBA.Domain.Enums;
+
+namespace PBA.Application.Features.Content.Dtos;
+
+public record RetryPublishRequest
+{
+    public Platform Platform { get; init; }
+}
diff --git a/src/PBA.Application/Features/Content/Dtos/ScheduleContentRequest.cs b/src/PBA.Application/Features/Content/Dtos/ScheduleContentRequest.cs
index 10a6268..9d45880 100644
--- a/src/PBA.Application/Features/Content/Dtos/ScheduleContentRequest.cs
+++ b/src/PBA.Application/Features/Content/Dtos/ScheduleContentRequest.cs
@@ -1,6 +1,9 @@
+using PBA.Domain.Enums;
+
 namespace PBA.Application.Features.Content.Dtos;
 
 public record ScheduleContentRequest
 {
     public DateTimeOffset ScheduledAt { get; init; }
+    public IReadOnlyList<Platform>? TargetPlatforms { get; init; }
 }
diff --git a/src/PBA.Application/Features/Content/Dtos/StoreCredentialsRequest.cs b/src/PBA.Application/Features/Content/Dtos/StoreCredentialsRequest.cs
new file mode 100644
index 0000000..f8dea2d
--- /dev/null
+++ b/src/PBA.Application/Features/Content/Dtos/StoreCredentialsRequest.cs
@@ -0,0 +1,8 @@
+namespace PBA.Application.Features.Content.Dtos;
+
+public record StoreCredentialsRequest
+{
+    public string? Token { get; init; }
+    public string? Email { get; init; }
+    public string? Password { get; init; }
+}
diff --git a/src/PBA.Application/Features/Content/Dtos/UpdateContentRequest.cs b/src/PBA.Application/Features/Content/Dtos/UpdateContentRequest.cs
index b6b95a5..5bf10c5 100644
--- a/src/PBA.Application/Features/Content/Dtos/UpdateContentRequest.cs
+++ b/src/PBA.Application/Features/Content/Dtos/UpdateContentRequest.cs
@@ -10,4 +10,5 @@ public record UpdateContentRequest
     public ContentType? ContentType { get; init; }
     public Platform? PrimaryPlatform { get; init; }
     public DateTimeOffset LastUpdatedAt { get; init; }
+    public IReadOnlyList<Platform>? TargetPlatforms { get; init; }
 }
diff --git a/src/PBA.Application/Features/Content/Queries/GetPublishStatus.cs b/src/PBA.Application/Features/Content/Queries/GetPublishStatus.cs
new file mode 100644
index 0000000..ea4c722
--- /dev/null
+++ b/src/PBA.Application/Features/Content/Queries/GetPublishStatus.cs
@@ -0,0 +1,40 @@
+using MediatR;
+using Microsoft.EntityFrameworkCore;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Features.Content.Dtos;
+using PBA.Domain.Common;
+
+namespace PBA.Application.Features.Content.Queries;
+
+public static class GetPublishStatus
+{
+    public record Query(Guid ContentId) : IRequest<Result<PublishStatusDto>>;
+
+    internal sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<PublishStatusDto>>
+    {
+        public async Task<Result<PublishStatusDto>> Handle(Query request, CancellationToken cancellationToken)
+        {
+            var contentExists = await db.Contents.AnyAsync(c => c.Id == request.ContentId, cancellationToken);
+            if (!contentExists)
+                return Result<PublishStatusDto>.NotFound($"Content {request.ContentId} not found");
+
+            var publishes = await db.ContentPlatformPublishes
+                .Where(p => p.ContentId == request.ContentId)
+                .Select(p => new PlatformPublishDto
+                {
+                    Id = p.Id,
+                    Platform = p.Platform,
+                    PublishStatus = p.Status,
+                    PublishedUrl = p.PublishedUrl,
+                    PublishedAt = p.PublishedAt
+                })
+                .ToListAsync(cancellationToken);
+
+            return new PublishStatusDto
+            {
+                ContentId = request.ContentId,
+                Platforms = publishes
+            };
+        }
+    }
+}
diff --git a/tests/PBA.Api.Tests/Endpoints/ContentEndpointsTests.cs b/tests/PBA.Api.Tests/Endpoints/ContentEndpointsTests.cs
index b022ade..539906d 100644
--- a/tests/PBA.Api.Tests/Endpoints/ContentEndpointsTests.cs
+++ b/tests/PBA.Api.Tests/Endpoints/ContentEndpointsTests.cs
@@ -329,4 +329,50 @@ public class ContentEndpointsTests : IClassFixture<TestWebApplicationFactory>
         var detail = await GetContent(id);
         Assert.Equal(ContentStatus.Published, detail.Status);
     }
+
+    [Fact]
+    public async Task PostPublish_WithTargetPlatforms_Returns200()
+    {
+        var id = await CreateTestContent();
+        await _client.PostAsJsonAsync($"/api/content/{id}/draft", new DraftContentRequest { Action = "draft" });
+        await _client.PutAsync($"/api/content/{id}/approve", null);
+
+        var body = new PublishContentRequest { TargetPlatforms = [Platform.Blog] };
+        var response = await _client.PostAsJsonAsync($"/api/content/{id}/publish", body);
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task GetPublishStatus_Returns200WithPlatformList()
+    {
+        var id = await CreateTestContent();
+        await _client.PostAsJsonAsync($"/api/content/{id}/draft", new DraftContentRequest { Action = "draft" });
+        await _client.PutAsync($"/api/content/{id}/approve", null);
+        await _client.PostAsync($"/api/content/{id}/publish", null);
+
+        var response = await _client.GetAsync($"/api/content/{id}/publish-status");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        var body = await response.Content.ReadAsStringAsync();
+        Assert.Contains("platforms", body, StringComparison.OrdinalIgnoreCase);
+    }
+
+    [Fact]
+    public async Task GetPublishStatus_Returns404ForNonexistent()
+    {
+        var response = await _client.GetAsync($"/api/content/{Guid.NewGuid()}/publish-status");
+
+        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task PostRetry_InvalidPlatform_Returns400()
+    {
+        var id = await CreateTestContent();
+
+        var response = await _client.PostAsync($"/api/content/{id}/retry/InvalidPlatform", null);
+
+        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
+    }
 }
diff --git a/tests/PBA.Api.Tests/Endpoints/OAuthEndpointsTests.cs b/tests/PBA.Api.Tests/Endpoints/OAuthEndpointsTests.cs
new file mode 100644
index 0000000..7b48c33
--- /dev/null
+++ b/tests/PBA.Api.Tests/Endpoints/OAuthEndpointsTests.cs
@@ -0,0 +1,107 @@
+using System.Net;
+using System.Net.Http.Json;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using Microsoft.Extensions.DependencyInjection;
+using PBA.Infrastructure.Data;
+using Xunit;
+
+namespace PBA.Api.Tests.Endpoints;
+
+public class OAuthEndpointsTests : IClassFixture<TestWebApplicationFactory>
+{
+    private readonly HttpClient _client;
+    private readonly TestWebApplicationFactory _factory;
+
+    public OAuthEndpointsTests(TestWebApplicationFactory factory)
+    {
+        _factory = factory;
+        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
+        {
+            AllowAutoRedirect = false
+        });
+    }
+
+    [Fact]
+    public async Task Authorize_LinkedIn_Returns302WithRedirectUrl()
+    {
+        var response = await _client.GetAsync("/api/auth/LinkedIn/authorize");
+
+        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
+        Assert.Contains("oauth.test", response.Headers.Location?.ToString());
+    }
+
+    [Fact]
+    public async Task Authorize_UnsupportedPlatform_Returns400()
+    {
+        var response = await _client.GetAsync("/api/auth/Blog/authorize");
+
+        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task Callback_ValidCode_Returns302RedirectToFrontend()
+    {
+        var response = await _client.GetAsync("/api/auth/LinkedIn/callback?code=abc&state=valid");
+
+        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
+        Assert.Contains("connected=LinkedIn", response.Headers.Location?.ToString());
+    }
+
+    [Fact]
+    public async Task Status_NotConfigured_ReturnsNotConfiguredStatus()
+    {
+        var response = await _client.GetAsync("/api/auth/Twitter/status");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        var body = await response.Content.ReadAsStringAsync();
+        Assert.Contains("NotConfigured", body);
+    }
+
+    [Fact]
+    public async Task Status_ConnectedPlatform_ReturnsConnectedStatus()
+    {
+        using var scope = _factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
+        db.PlatformCredentials.Add(new PlatformCredential
+        {
+            Platform = Platform.LinkedIn,
+            IsActive = true,
+            EncryptedAccessToken = "test",
+            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
+        });
+        await db.SaveChangesAsync();
+
+        var response = await _client.GetAsync("/api/auth/LinkedIn/status");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        var body = await response.Content.ReadAsStringAsync();
+        Assert.Contains("Connected", body);
+    }
+
+    [Fact]
+    public async Task Delete_ConnectedPlatform_Returns200()
+    {
+        using var scope = _factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
+        db.PlatformCredentials.Add(new PlatformCredential
+        {
+            Platform = Platform.Medium,
+            IsActive = true,
+            EncryptedAccessToken = "test"
+        });
+        await db.SaveChangesAsync();
+
+        var response = await _client.DeleteAsync("/api/auth/Medium");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task Delete_NonexistentPlatform_Returns404()
+    {
+        var response = await _client.DeleteAsync("/api/auth/Substack");
+
+        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
+    }
+}
diff --git a/tests/PBA.Api.Tests/Endpoints/PlatformEndpointsTests.cs b/tests/PBA.Api.Tests/Endpoints/PlatformEndpointsTests.cs
new file mode 100644
index 0000000..a97a52f
--- /dev/null
+++ b/tests/PBA.Api.Tests/Endpoints/PlatformEndpointsTests.cs
@@ -0,0 +1,57 @@
+using System.Net;
+using System.Net.Http.Json;
+using PBA.Application.Features.Content.Dtos;
+using Xunit;
+
+namespace PBA.Api.Tests.Endpoints;
+
+public class PlatformEndpointsTests : IClassFixture<TestWebApplicationFactory>
+{
+    private readonly HttpClient _client;
+
+    public PlatformEndpointsTests(TestWebApplicationFactory factory)
+    {
+        _client = factory.CreateClient();
+    }
+
+    [Fact]
+    public async Task GetPlatforms_Returns200WithPlatformList()
+    {
+        var response = await _client.GetAsync("/api/platforms");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        var body = await response.Content.ReadAsStringAsync();
+        Assert.Contains("Blog", body);
+        Assert.Contains("Medium", body);
+    }
+
+    [Fact]
+    public async Task PostCredentials_Medium_Returns200()
+    {
+        var body = new StoreCredentialsRequest { Token = "test-token" };
+
+        var response = await _client.PostAsJsonAsync("/api/platforms/Medium/credentials", body);
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task PostCredentials_EmptyToken_Returns400()
+    {
+        var body = new StoreCredentialsRequest { Token = "" };
+
+        var response = await _client.PostAsJsonAsync("/api/platforms/Medium/credentials", body);
+
+        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task PostCredentials_OAuthPlatform_Returns400()
+    {
+        var body = new StoreCredentialsRequest { Token = "test" };
+
+        var response = await _client.PostAsJsonAsync("/api/platforms/LinkedIn/credentials", body);
+
+        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
+    }
+}
diff --git a/tests/PBA.Api.Tests/TestWebApplicationFactory.cs b/tests/PBA.Api.Tests/TestWebApplicationFactory.cs
index 601bb21..f5d056d 100644
--- a/tests/PBA.Api.Tests/TestWebApplicationFactory.cs
+++ b/tests/PBA.Api.Tests/TestWebApplicationFactory.cs
@@ -59,6 +59,20 @@ public class TestWebApplicationFactory : WebApplicationFactory<Program>
             transformerMock.Setup(x => x.TransformAsync(It.IsAny<PBA.Domain.Entities.Content>(), It.IsAny<PBA.Domain.Enums.Platform>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((PBA.Domain.Entities.Content c, PBA.Domain.Enums.Platform _, CancellationToken _) => c.Body);
             services.AddSingleton<IContentTransformer>(transformerMock.Object);
+
+            var oauthMock = new Mock<IOAuthService>();
+            oauthMock.Setup(x => x.GetAuthorizationUrlAsync(It.IsAny<PBA.Domain.Enums.Platform>(), It.IsAny<CancellationToken>()))
+                .ReturnsAsync("https://oauth.test/authorize?state=test");
+            oauthMock.Setup(x => x.ExchangeCodeAsync(It.IsAny<PBA.Domain.Enums.Platform>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+                .ReturnsAsync(new PBA.Domain.Entities.PlatformCredential { Platform = PBA.Domain.Enums.Platform.LinkedIn, IsActive = true, EncryptedAccessToken = "encrypted" });
+            services.AddSingleton(oauthMock.Object);
+
+            var encryptorMock = new Mock<ITokenEncryptor>();
+            encryptorMock.Setup(x => x.Encrypt(It.IsAny<string>())).Returns<string>(s => $"enc:{s}");
+            encryptorMock.Setup(x => x.Decrypt(It.IsAny<string>())).Returns<string>(s => s.Replace("enc:", ""));
+            services.AddSingleton(encryptorMock.Object);
+
+            services.AddSingleton(new Mock<IPublishRetryHandler>().Object);
         });
 
         builder.UseEnvironment("Testing");
