namespace PBA.Application.Common.Interfaces;

using PBA.Application.Common.Models;
using PBA.Domain.Enums;

public interface IPlatformFormatter
{
    Platform Platform { get; }
    Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct);
}
