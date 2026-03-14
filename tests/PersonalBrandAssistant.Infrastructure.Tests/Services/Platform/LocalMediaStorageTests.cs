using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Infrastructure.Services.MediaServices;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.MediaServices;

public class LocalMediaStorageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalMediaStorage _storage;

    public LocalMediaStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"media-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var options = Options.Create(new MediaStorageOptions
        {
            BasePath = _tempDir,
            SigningKey = "test-signing-key-for-hmac-256",
            MaxFileSizeBytes = 1024 * 1024, // 1MB for tests
        });

        _storage = new LocalMediaStorage(options, NullLogger<LocalMediaStorage>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static MemoryStream CreateJpegStream(int size = 100)
    {
        var data = new byte[size];
        data[0] = 0xFF;
        data[1] = 0xD8;
        data[2] = 0xFF;
        return new MemoryStream(data);
    }

    private static MemoryStream CreatePngStream(int size = 100)
    {
        var data = new byte[size];
        data[0] = 0x89;
        data[1] = 0x50;
        data[2] = 0x4E;
        data[3] = 0x47;
        return new MemoryStream(data);
    }

    [Fact]
    public async Task SaveAsync_CreatesFile_InDateOrganizedPath()
    {
        using var stream = CreateJpegStream();
        var fileId = await _storage.SaveAsync(stream, "test.jpg", "image/jpeg", CancellationToken.None);

        Assert.NotEmpty(fileId);
        Assert.EndsWith(".jpg", fileId);

        var path = await _storage.GetPathAsync(fileId, CancellationToken.None);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task SaveAsync_ReturnsUniqueFileIds()
    {
        using var stream1 = CreateJpegStream();
        using var stream2 = CreateJpegStream();

        var id1 = await _storage.SaveAsync(stream1, "test.jpg", "image/jpeg", CancellationToken.None);
        var id2 = await _storage.SaveAsync(stream2, "test.jpg", "image/jpeg", CancellationToken.None);

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public async Task SaveAsync_ValidatesMagicBytes_RejectsMismatch()
    {
        using var pngStream = CreatePngStream();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _storage.SaveAsync(pngStream, "test.jpg", "image/jpeg", CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_RejectsOversizedFiles()
    {
        var data = new byte[2 * 1024 * 1024]; // 2MB > 1MB limit
        data[0] = 0xFF;
        data[1] = 0xD8;
        data[2] = 0xFF;
        using var largeStream = new MemoryStream(data);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _storage.SaveAsync(largeStream, "big.jpg", "image/jpeg", CancellationToken.None));
    }

    [Fact]
    public async Task GetStreamAsync_ReturnsReadableStream()
    {
        using var inputStream = CreateJpegStream(200);
        var originalBytes = inputStream.ToArray();
        var fileId = await _storage.SaveAsync(inputStream, "test.jpg", "image/jpeg", CancellationToken.None);

        await using var result = await _storage.GetStreamAsync(fileId, CancellationToken.None);
        using var ms = new MemoryStream();
        await result.CopyToAsync(ms);

        Assert.Equal(originalBytes, ms.ToArray());
    }

    [Fact]
    public async Task GetStreamAsync_ThrowsForNonExistent()
    {
        var fakeId = $"2026-03-{Guid.NewGuid()}.jpg";
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _storage.GetStreamAsync(fakeId, CancellationToken.None));
    }

    [Fact]
    public async Task GetPathAsync_ReturnsCorrectPath()
    {
        using var stream = CreateJpegStream();
        var fileId = await _storage.SaveAsync(stream, "test.jpg", "image/jpeg", CancellationToken.None);

        var path = await _storage.GetPathAsync(fileId, CancellationToken.None);

        Assert.EndsWith(".jpg", path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile()
    {
        using var stream = CreateJpegStream();
        var fileId = await _storage.SaveAsync(stream, "test.jpg", "image/jpeg", CancellationToken.None);
        var path = await _storage.GetPathAsync(fileId, CancellationToken.None);

        var deleted = await _storage.DeleteAsync(fileId, CancellationToken.None);

        Assert.True(deleted);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalseForNonExistent()
    {
        var fakeId = $"2026-03-{Guid.NewGuid()}.jpg";
        var result = await _storage.DeleteAsync(fakeId, CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task GetSignedUrlAsync_GeneratesUrlWithTokenAndExpiry()
    {
        using var stream = CreateJpegStream();
        var fileId = await _storage.SaveAsync(stream, "test.jpg", "image/jpeg", CancellationToken.None);

        var url = await _storage.GetSignedUrlAsync(fileId, TimeSpan.FromHours(1), CancellationToken.None);

        Assert.Contains($"/api/media/{fileId}", url);
        Assert.Contains("token=", url);
        Assert.Contains("expires=", url);
    }
}
