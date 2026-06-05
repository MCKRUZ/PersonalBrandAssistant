namespace PBA.Application.Common.Interfaces;

public sealed record DigestInput(int Index, string Title, string Summary, int Score, string? Url);

public sealed record DigestItemCopy(int Index, string WhyItMatters);

public sealed record DigestCopy(string Title, string Intro, IReadOnlyList<DigestItemCopy> Items);

public interface IDigestWriter
{
    /// <summary>
    /// Writes a brand-voice daily brief (no em-dashes) over the top items.
    /// Returns null if the model output cannot be parsed.
    /// </summary>
    Task<DigestCopy?> WriteAsync(IReadOnlyList<DigestInput> items, CancellationToken ct = default);
}
