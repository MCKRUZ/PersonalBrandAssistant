namespace PBA.Application.Common.Models;

public record PlatformCapabilities(
    int MaxCharacters,
    bool SupportsMarkdown,
    bool SupportsHtml,
    bool SupportsImages,
    bool SupportsScheduling,
    bool SupportsThreads,
    IReadOnlyList<string> SupportedMediaTypes
);
