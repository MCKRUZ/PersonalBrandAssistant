diff --git a/src/PersonalBrandAssistant.Api/Endpoints/MediaEndpoints.cs b/src/PersonalBrandAssistant.Api/Endpoints/MediaEndpoints.cs
new file mode 100644
index 0000000..4fefbb1
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/Endpoints/MediaEndpoints.cs
@@ -0,0 +1,70 @@
+using System.Security.Cryptography;
+using System.Text;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Api.Endpoints;
+
+public static class MediaEndpoints
+{
+    private static readonly Dictionary<string, string> ExtensionToContentType = new()
+    {
+        [".jpg"] = "image/jpeg",
+        [".png"] = "image/png",
+        [".gif"] = "image/gif",
+        [".webp"] = "image/webp",
+        [".mp4"] = "video/mp4",
+        [".mov"] = "video/quicktime",
+    };
+
+    public static void MapMediaEndpoints(this IEndpointRouteBuilder app)
+    {
+        var group = app.MapGroup("/api/media").WithTags("Media");
+        group.MapGet("/{fileId}", ServeMedia).AllowAnonymous();
+    }
+
+    private static async Task<IResult> ServeMedia(
+        string fileId,
+        string token,
+        long expires,
+        IMediaStorage mediaStorage,
+        IOptions<MediaStorageOptions> options)
+    {
+        if (DateTimeOffset.UtcNow > DateTimeOffset.FromUnixTimeSeconds(expires))
+            return Results.StatusCode(403);
+
+        var signingKey = options.Value.SigningKey;
+        if (string.IsNullOrEmpty(signingKey))
+            return Results.StatusCode(500);
+
+        var expectedToken = ComputeHmac(fileId, expires, signingKey);
+        var tokenBytes = Encoding.UTF8.GetBytes(token);
+        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
+
+        if (!CryptographicOperations.FixedTimeEquals(tokenBytes, expectedBytes))
+            return Results.StatusCode(403);
+
+        try
+        {
+            var stream = await mediaStorage.GetStreamAsync(fileId, CancellationToken.None);
+            var extension = Path.GetExtension(fileId);
+            var contentType = ExtensionToContentType.GetValueOrDefault(extension, "application/octet-stream");
+
+            var remaining = DateTimeOffset.FromUnixTimeSeconds(expires) - DateTimeOffset.UtcNow;
+            return Results.Stream(stream, contentType, enableRangeProcessing: false);
+        }
+        catch (FileNotFoundException)
+        {
+            return Results.NotFound();
+        }
+    }
+
+    private static string ComputeHmac(string fileId, long expiryEpoch, string signingKey)
+    {
+        var key = Encoding.UTF8.GetBytes(signingKey);
+        var message = Encoding.UTF8.GetBytes($"{fileId}:{expiryEpoch}");
+        var hash = HMACSHA256.HashData(key, message);
+        return Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_').TrimEnd('=');
+    }
+}
diff --git a/src/PersonalBrandAssistant.Api/Program.cs b/src/PersonalBrandAssistant.Api/Program.cs
index cf1b25c..89a7911 100644
--- a/src/PersonalBrandAssistant.Api/Program.cs
+++ b/src/PersonalBrandAssistant.Api/Program.cs
@@ -58,6 +58,7 @@ app.MapApprovalEndpoints();
 app.MapSchedulingEndpoints();
 app.MapNotificationEndpoints();
 app.MapAgentEndpoints();
+app.MapMediaEndpoints();
 
 app.Run();
 
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/MediaStorageOptions.cs b/src/PersonalBrandAssistant.Application/Common/Models/MediaStorageOptions.cs
index c1c1e34..c80a535 100644
--- a/src/PersonalBrandAssistant.Application/Common/Models/MediaStorageOptions.cs
+++ b/src/PersonalBrandAssistant.Application/Common/Models/MediaStorageOptions.cs
@@ -4,4 +4,5 @@ public class MediaStorageOptions
 {
     public string BasePath { get; set; } = "./media";
     public string? SigningKey { get; set; }
+    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024;
 }
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/MediaServices/LocalMediaStorage.cs b/src/PersonalBrandAssistant.Infrastructure/Services/MediaServices/LocalMediaStorage.cs
new file mode 100644
index 0000000..6194d4e
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/MediaServices/LocalMediaStorage.cs
@@ -0,0 +1,147 @@
+using System.Security.Cryptography;
+using System.Text;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.MediaServices;
+
+public class LocalMediaStorage : IMediaStorage
+{
+    private readonly MediaStorageOptions _options;
+    private readonly ILogger<LocalMediaStorage> _logger;
+
+    private static readonly Dictionary<string, (byte[] Magic, int Offset)[]> MagicBytes = new()
+    {
+        ["image/jpeg"] = [(new byte[] { 0xFF, 0xD8, 0xFF }, 0)],
+        ["image/png"] = [(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, 0)],
+        ["image/gif"] = [(new byte[] { 0x47, 0x49, 0x46, 0x38 }, 0)],
+        ["image/webp"] = [(new byte[] { 0x52, 0x49, 0x46, 0x46 }, 0)],
+        ["video/mp4"] = [(new byte[] { 0x66, 0x74, 0x79, 0x70 }, 4)],
+    };
+
+    private static readonly Dictionary<string, string> MimeToExtension = new()
+    {
+        ["image/jpeg"] = ".jpg",
+        ["image/png"] = ".png",
+        ["image/gif"] = ".gif",
+        ["image/webp"] = ".webp",
+        ["video/mp4"] = ".mp4",
+        ["video/quicktime"] = ".mov",
+    };
+
+    public LocalMediaStorage(IOptions<MediaStorageOptions> options, ILogger<LocalMediaStorage> logger)
+    {
+        _options = options.Value;
+        _logger = logger;
+    }
+
+    public async Task<string> SaveAsync(Stream content, string fileName, string mimeType, CancellationToken ct)
+    {
+        if (content.Length > _options.MaxFileSizeBytes)
+            throw new InvalidOperationException($"File exceeds maximum size of {_options.MaxFileSizeBytes} bytes");
+
+        if (!MimeToExtension.TryGetValue(mimeType, out var extension))
+            throw new InvalidOperationException($"Unsupported MIME type: {mimeType}");
+
+        var header = new byte[12];
+        var bytesRead = await content.ReadAsync(header, ct);
+        content.Position = 0;
+
+        if (!ValidateMagicBytes(mimeType, header, bytesRead))
+            throw new InvalidOperationException($"File content does not match claimed MIME type {mimeType}");
+
+        var now = DateTimeOffset.UtcNow;
+        var guid = Guid.NewGuid();
+        var yyyy = now.ToString("yyyy");
+        var mm = now.ToString("MM");
+
+        var directory = Path.Combine(_options.BasePath, yyyy, mm);
+        Directory.CreateDirectory(directory);
+
+        var filePath = Path.Combine(directory, $"{guid}{extension}");
+        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
+        await content.CopyToAsync(fileStream, ct);
+
+        var fileId = $"{yyyy}-{mm}-{guid}{extension}";
+        _logger.LogInformation("Saved media file {FileId} ({MimeType}, {Size} bytes)", fileId, mimeType, content.Length);
+        return fileId;
+    }
+
+    public async Task<Stream> GetStreamAsync(string fileId, CancellationToken ct)
+    {
+        var path = ResolvePath(fileId);
+        if (!File.Exists(path))
+            throw new FileNotFoundException($"Media file not found: {fileId}");
+
+        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
+    }
+
+    public Task<string> GetPathAsync(string fileId, CancellationToken ct)
+    {
+        var path = ResolvePath(fileId);
+        if (!File.Exists(path))
+            throw new FileNotFoundException($"Media file not found: {fileId}");
+
+        return Task.FromResult(path);
+    }
+
+    public Task<bool> DeleteAsync(string fileId, CancellationToken ct)
+    {
+        var path = ResolvePath(fileId);
+        if (!File.Exists(path))
+            return Task.FromResult(false);
+
+        File.Delete(path);
+        _logger.LogInformation("Deleted media file {FileId}", fileId);
+        return Task.FromResult(true);
+    }
+
+    public Task<string> GetSignedUrlAsync(string fileId, TimeSpan expiry, CancellationToken ct)
+    {
+        var expiryEpoch = DateTimeOffset.UtcNow.Add(expiry).ToUnixTimeSeconds();
+        var token = ComputeHmac(fileId, expiryEpoch);
+        var url = $"/api/media/{fileId}?token={token}&expires={expiryEpoch}";
+        return Task.FromResult(url);
+    }
+
+    private string ResolvePath(string fileId)
+    {
+        // fileId format: {yyyy}-{MM}-{guid}.{ext}
+        var parts = fileId.Split('-', 3);
+        if (parts.Length < 3)
+            throw new ArgumentException($"Invalid fileId format: {fileId}");
+
+        var yyyy = parts[0];
+        var mm = parts[1];
+        var guidAndExt = parts[2];
+
+        return Path.Combine(_options.BasePath, yyyy, mm, guidAndExt);
+    }
+
+    private string ComputeHmac(string fileId, long expiryEpoch)
+    {
+        var key = Encoding.UTF8.GetBytes(_options.SigningKey ?? throw new InvalidOperationException("SigningKey is not configured"));
+        var message = Encoding.UTF8.GetBytes($"{fileId}:{expiryEpoch}");
+        var hash = HMACSHA256.HashData(key, message);
+        return Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_').TrimEnd('=');
+    }
+
+    private static bool ValidateMagicBytes(string mimeType, byte[] header, int bytesRead)
+    {
+        if (!MagicBytes.TryGetValue(mimeType, out var signatures))
+            return false;
+
+        foreach (var (magic, offset) in signatures)
+        {
+            if (offset + magic.Length > bytesRead)
+                return false;
+
+            if (header.AsSpan(offset, magic.Length).SequenceEqual(magic))
+                return true;
+        }
+
+        return false;
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/CustomWebApplicationFactory.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/CustomWebApplicationFactory.cs
index f94454a..05ee590 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/CustomWebApplicationFactory.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/CustomWebApplicationFactory.cs
@@ -4,9 +4,12 @@ using Microsoft.AspNetCore.TestHost;
 using Microsoft.EntityFrameworkCore;
 using Microsoft.Extensions.Configuration;
 using Microsoft.Extensions.DependencyInjection;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
 using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
 using PersonalBrandAssistant.Infrastructure.Data;
 using PersonalBrandAssistant.Infrastructure.Services;
+using PersonalBrandAssistant.Infrastructure.Services.MediaServices;
 
 namespace PersonalBrandAssistant.Infrastructure.Tests.Api;
 
@@ -30,6 +33,8 @@ public class CustomWebApplicationFactory : WebApplicationFactory<Program>
         builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
         builder.UseSetting("ApiKey", TestApiKey);
         builder.UseSetting("AuditLog:RetentionDays", "90");
+        builder.UseSetting("MediaStorage:BasePath", Path.Combine(Path.GetTempPath(), "media-test"));
+        builder.UseSetting("MediaStorage:SigningKey", "test-signing-key-for-hmac-256");
 
         builder.ConfigureTestServices(services =>
         {
@@ -40,6 +45,13 @@ public class CustomWebApplicationFactory : WebApplicationFactory<Program>
             RemoveService<RetryFailedProcessor>(services);
             RemoveService<WorkflowRehydrator>(services);
             RemoveService<RetentionCleanupService>(services);
+
+            services.Configure<MediaStorageOptions>(opts =>
+            {
+                opts.BasePath = Path.Combine(Path.GetTempPath(), "media-test");
+                opts.SigningKey = "test-signing-key-for-hmac-256";
+            });
+            services.AddSingleton<IMediaStorage, LocalMediaStorage>();
         });
     }
 
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/LocalMediaStorageTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/LocalMediaStorageTests.cs
new file mode 100644
index 0000000..411bd7b
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/LocalMediaStorageTests.cs
@@ -0,0 +1,165 @@
+using Microsoft.Extensions.Logging.Abstractions;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Infrastructure.Services.MediaServices;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.MediaServices;
+
+public class LocalMediaStorageTests : IDisposable
+{
+    private readonly string _tempDir;
+    private readonly LocalMediaStorage _storage;
+
+    public LocalMediaStorageTests()
+    {
+        _tempDir = Path.Combine(Path.GetTempPath(), $"media-test-{Guid.NewGuid():N}");
+        Directory.CreateDirectory(_tempDir);
+
+        var options = Options.Create(new MediaStorageOptions
+        {
+            BasePath = _tempDir,
+            SigningKey = "test-signing-key-for-hmac-256",
+            MaxFileSizeBytes = 1024 * 1024, // 1MB for tests
+        });
+
+        _storage = new LocalMediaStorage(options, NullLogger<LocalMediaStorage>.Instance);
+    }
+
+    public void Dispose()
+    {
+        if (Directory.Exists(_tempDir))
+            Directory.Delete(_tempDir, true);
+    }
+
+    private static MemoryStream CreateJpegStream(int size = 100)
+    {
+        var data = new byte[size];
+        data[0] = 0xFF;
+        data[1] = 0xD8;
+        data[2] = 0xFF;
+        return new MemoryStream(data);
+    }
+
+    private static MemoryStream CreatePngStream(int size = 100)
+    {
+        var data = new byte[size];
+        data[0] = 0x89;
+        data[1] = 0x50;
+        data[2] = 0x4E;
+        data[3] = 0x47;
+        return new MemoryStream(data);
+    }
+
+    [Fact]
+    public async Task SaveAsync_CreatesFile_InDateOrganizedPath()
+    {
+        using var stream = CreateJpegStream();
+        var fileId = await _storage.SaveAsync(stream, "test.jpg", "image/jpeg", CancellationToken.None);
+
+        Assert.NotEmpty(fileId);
+        Assert.EndsWith(".jpg", fileId);
+
+        var path = await _storage.GetPathAsync(fileId, CancellationToken.None);
+        Assert.True(File.Exists(path));
+    }
+
+    [Fact]
+    public async Task SaveAsync_ReturnsUniqueFileIds()
+    {
+        using var stream1 = CreateJpegStream();
+        using var stream2 = CreateJpegStream();
+
+        var id1 = await _storage.SaveAsync(stream1, "test.jpg", "image/jpeg", CancellationToken.None);
+        var id2 = await _storage.SaveAsync(stream2, "test.jpg", "image/jpeg", CancellationToken.None);
+
+        Assert.NotEqual(id1, id2);
+    }
+
+    [Fact]
+    public async Task SaveAsync_ValidatesMagicBytes_RejectsMismatch()
+    {
+        using var pngStream = CreatePngStream();
+
+        await Assert.ThrowsAsync<InvalidOperationException>(
+            () => _storage.SaveAsync(pngStream, "test.jpg", "image/jpeg", CancellationToken.None));
+    }
+
+    [Fact]
+    public async Task SaveAsync_RejectsOversizedFiles()
+    {
+        var data = new byte[2 * 1024 * 1024]; // 2MB > 1MB limit
+        data[0] = 0xFF;
+        data[1] = 0xD8;
+        data[2] = 0xFF;
+        using var largeStream = new MemoryStream(data);
+
+        await Assert.ThrowsAsync<InvalidOperationException>(
+            () => _storage.SaveAsync(largeStream, "big.jpg", "image/jpeg", CancellationToken.None));
+    }
+
+    [Fact]
+    public async Task GetStreamAsync_ReturnsReadableStream()
+    {
+        using var inputStream = CreateJpegStream(200);
+        var originalBytes = inputStream.ToArray();
+        var fileId = await _storage.SaveAsync(inputStream, "test.jpg", "image/jpeg", CancellationToken.None);
+
+        await using var result = await _storage.GetStreamAsync(fileId, CancellationToken.None);
+        using var ms = new MemoryStream();
+        await result.CopyToAsync(ms);
+
+        Assert.Equal(originalBytes, ms.ToArray());
+    }
+
+    [Fact]
+    public async Task GetStreamAsync_ThrowsForNonExistent()
+    {
+        await Assert.ThrowsAsync<FileNotFoundException>(
+            () => _storage.GetStreamAsync("2026-03-nonexistent.jpg", CancellationToken.None));
+    }
+
+    [Fact]
+    public async Task GetPathAsync_ReturnsCorrectPath()
+    {
+        using var stream = CreateJpegStream();
+        var fileId = await _storage.SaveAsync(stream, "test.jpg", "image/jpeg", CancellationToken.None);
+
+        var path = await _storage.GetPathAsync(fileId, CancellationToken.None);
+
+        Assert.EndsWith(".jpg", path);
+        Assert.True(File.Exists(path));
+    }
+
+    [Fact]
+    public async Task DeleteAsync_RemovesFile()
+    {
+        using var stream = CreateJpegStream();
+        var fileId = await _storage.SaveAsync(stream, "test.jpg", "image/jpeg", CancellationToken.None);
+        var path = await _storage.GetPathAsync(fileId, CancellationToken.None);
+
+        var deleted = await _storage.DeleteAsync(fileId, CancellationToken.None);
+
+        Assert.True(deleted);
+        Assert.False(File.Exists(path));
+    }
+
+    [Fact]
+    public async Task DeleteAsync_ReturnsFalseForNonExistent()
+    {
+        var result = await _storage.DeleteAsync("2026-03-nonexistent.jpg", CancellationToken.None);
+        Assert.False(result);
+    }
+
+    [Fact]
+    public async Task GetSignedUrlAsync_GeneratesUrlWithTokenAndExpiry()
+    {
+        using var stream = CreateJpegStream();
+        var fileId = await _storage.SaveAsync(stream, "test.jpg", "image/jpeg", CancellationToken.None);
+
+        var url = await _storage.GetSignedUrlAsync(fileId, TimeSpan.FromHours(1), CancellationToken.None);
+
+        Assert.Contains($"/api/media/{fileId}", url);
+        Assert.Contains("token=", url);
+        Assert.Contains("expires=", url);
+    }
+}
