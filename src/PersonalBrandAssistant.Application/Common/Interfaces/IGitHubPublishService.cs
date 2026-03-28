using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IGitHubPublishService
{
    Task<Result<GitCommitResult>> CommitBlogPostAsync(BlogPublishRequest request, CancellationToken ct);
    Task<bool> VerifyDeploymentAsync(string blogPostUrl, CancellationToken ct);
}
