namespace PBA.Application.Common.Models;

public record ImageReference(
    string OriginalPath,
    string AbsoluteUrl,
    string? AltText
);
