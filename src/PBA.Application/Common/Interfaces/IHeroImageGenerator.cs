using PBA.Domain.Common;

namespace PBA.Application.Common.Interfaces;

/// <summary>
/// Generates a hero image for a blog post via the user's self-hosted ComfyUI instance.
/// </summary>
public interface IHeroImageGenerator
{
    /// <summary>
    /// Generates and saves a hero image for the given post.
    /// </summary>
    /// <returns>The saved file path on success, or a failure describing why generation did not occur.</returns>
    Task<Result<string>> GenerateAsync(BlogPostMeta post, CancellationToken ct);
}
