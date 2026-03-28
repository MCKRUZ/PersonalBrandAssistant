using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Models;

public record PlatformContent(
    string Text,
    string? Title,
    ContentType ContentType,
    IReadOnlyList<MediaFile> Media,
    IReadOnlyDictionary<string, string> Metadata);

public record MediaFile(string FileId, string MimeType, string? AltText);
