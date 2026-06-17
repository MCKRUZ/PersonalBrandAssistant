using PBA.Domain.Enums;

namespace PBA.Application.Common.Interfaces;

public sealed record DigestInput(int Index, string Title, string Summary, int Score, string? Url);

public sealed record DigestItemCopy(int Index, string WhyItMatters);

public sealed record DigestCopy(string Title, string Intro, IReadOnlyList<DigestItemCopy> Items);

public interface IDigestWriter
{
    /// <summary>
    /// Writes a brand-voice brief (no em-dashes) over the top items. <paramref name="kind"/> selects the
    /// framing: the general brief or the Microsoft-focused brief. Returns null if the model output cannot
    /// be parsed.
    /// </summary>
    Task<DigestCopy?> WriteAsync(
        IReadOnlyList<DigestInput> items, DigestKind kind = DigestKind.Main, CancellationToken ct = default);
}
