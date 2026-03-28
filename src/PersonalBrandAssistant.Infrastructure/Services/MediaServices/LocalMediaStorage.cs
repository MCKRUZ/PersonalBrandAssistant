using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services.MediaServices;

public class LocalMediaStorage : IMediaStorage
{
    private readonly MediaStorageOptions _options;
    private readonly ILogger<LocalMediaStorage> _logger;

    private static readonly Dictionary<string, (byte[] Magic, int Offset)[]> MagicBytes = new()
    {
        ["image/jpeg"] = [(new byte[] { 0xFF, 0xD8, 0xFF }, 0)],
        ["image/png"] = [(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, 0)],
        ["image/gif"] = [(new byte[] { 0x47, 0x49, 0x46, 0x38 }, 0)],
        ["image/webp"] = [(new byte[] { 0x52, 0x49, 0x46, 0x46 }, 0)],
        ["video/mp4"] = [(new byte[] { 0x66, 0x74, 0x79, 0x70 }, 4)],
    };

    private static readonly Dictionary<string, string> MimeToExtension = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
        ["video/mp4"] = ".mp4",
    };

    public LocalMediaStorage(IOptions<MediaStorageOptions> options, ILogger<LocalMediaStorage> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> SaveAsync(Stream content, string fileName, string mimeType, CancellationToken ct)
    {
        if (!content.CanSeek)
            throw new InvalidOperationException("Stream must be seekable for MIME validation");

        if (content.Length > _options.MaxFileSizeBytes)
            throw new InvalidOperationException($"File exceeds maximum size of {_options.MaxFileSizeBytes} bytes");

        if (!MimeToExtension.TryGetValue(mimeType, out var extension))
            throw new InvalidOperationException($"Unsupported MIME type: {mimeType}");

        var header = new byte[12];
        var bytesRead = await content.ReadAsync(header, ct);
        content.Position = 0;

        if (!ValidateMagicBytes(mimeType, header, bytesRead))
            throw new InvalidOperationException($"File content does not match claimed MIME type {mimeType}");

        var now = DateTimeOffset.UtcNow;
        var guid = Guid.NewGuid();
        var yyyy = now.ToString("yyyy");
        var mm = now.ToString("MM");

        var directory = Path.Combine(_options.BasePath, yyyy, mm);
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, $"{guid}{extension}");
        try
        {
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            await content.CopyToAsync(fileStream, ct);
        }
        catch
        {
            try { File.Delete(filePath); } catch { /* best-effort cleanup */ }
            throw;
        }

        var fileId = $"{yyyy}-{mm}-{guid}{extension}";
        _logger.LogInformation("Saved media file {FileId} ({MimeType}, {Size} bytes)", fileId, mimeType, content.Length);
        return fileId;
    }

    public async Task<Stream> GetStreamAsync(string fileId, CancellationToken ct)
    {
        var path = ResolvePath(fileId);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Media file not found: {fileId}");

        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public Task<string> GetPathAsync(string fileId, CancellationToken ct)
    {
        var path = ResolvePath(fileId);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Media file not found: {fileId}");

        return Task.FromResult(path);
    }

    public Task<bool> DeleteAsync(string fileId, CancellationToken ct)
    {
        var path = ResolvePath(fileId);
        if (!File.Exists(path))
            return Task.FromResult(false);

        File.Delete(path);
        _logger.LogInformation("Deleted media file {FileId}", fileId);
        return Task.FromResult(true);
    }

    public Task<string> GetSignedUrlAsync(string fileId, TimeSpan expiry, CancellationToken ct)
    {
        var expiryEpoch = DateTimeOffset.UtcNow.Add(expiry).ToUnixTimeSeconds();
        var token = ComputeHmac(fileId, expiryEpoch);
        var url = $"/api/media/{fileId}?token={token}&expires={expiryEpoch}";
        return Task.FromResult(url);
    }

    private static readonly Regex FileIdPattern = new(@"^\d{4}-\d{2}-[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}\.\w+$", RegexOptions.Compiled);

    private string ResolvePath(string fileId)
    {
        if (!FileIdPattern.IsMatch(fileId))
            throw new ArgumentException($"Invalid fileId format: {fileId}");

        // fileId format: {yyyy}-{MM}-{guid}.{ext}
        var parts = fileId.Split('-', 3);
        var yyyy = parts[0];
        var mm = parts[1];
        var guidAndExt = parts[2];

        var path = Path.GetFullPath(Path.Combine(_options.BasePath, yyyy, mm, guidAndExt));
        var basePath = Path.GetFullPath(_options.BasePath);

        if (!path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Path traversal detected in fileId: {fileId}");

        return path;
    }

    private string ComputeHmac(string fileId, long expiryEpoch)
    {
        var key = Encoding.UTF8.GetBytes(_options.SigningKey ?? throw new InvalidOperationException("SigningKey is not configured"));
        var message = Encoding.UTF8.GetBytes($"{fileId}:{expiryEpoch}");
        var hash = HMACSHA256.HashData(key, message);
        return Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static bool ValidateMagicBytes(string mimeType, byte[] header, int bytesRead)
    {
        if (!MagicBytes.TryGetValue(mimeType, out var signatures))
            return false;

        foreach (var (magic, offset) in signatures)
        {
            if (offset + magic.Length > bytesRead)
                return false;

            if (header.AsSpan(offset, magic.Length).SequenceEqual(magic))
                return true;
        }

        return false;
    }
}
