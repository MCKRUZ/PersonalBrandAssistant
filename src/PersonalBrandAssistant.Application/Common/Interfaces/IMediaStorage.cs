namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IMediaStorage
{
    Task<string> SaveAsync(Stream content, string fileName, string mimeType, CancellationToken ct);
    Task<Stream> GetStreamAsync(string fileId, CancellationToken ct);
    Task<string> GetPathAsync(string fileId, CancellationToken ct);
    Task<bool> DeleteAsync(string fileId, CancellationToken ct);
    Task<string> GetSignedUrlAsync(string fileId, TimeSpan expiry, CancellationToken ct);
}
