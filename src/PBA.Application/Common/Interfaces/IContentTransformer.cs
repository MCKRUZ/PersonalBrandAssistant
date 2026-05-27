namespace PBA.Application.Common.Interfaces;

using PBA.Domain.Entities;
using PBA.Domain.Enums;

public interface IContentTransformer
{
    Task<string> TransformAsync(Content content, Platform platform, CancellationToken ct);
}
