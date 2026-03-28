using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Services.BlogServices;

internal sealed class GitHubPublishService : IGitHubPublishService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BlogPublishOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GitHubPublishService> _logger;

    public GitHubPublishService(
        IHttpClientFactory httpClientFactory,
        IOptions<BlogPublishOptions> options,
        IConfiguration configuration,
        ILogger<GitHubPublishService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Result<GitCommitResult>> CommitBlogPostAsync(
        BlogPublishRequest request, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("GitHubApi");
        var pat = _configuration["GitHub:PersonalAccessToken"];

        if (string.IsNullOrWhiteSpace(pat))
            return Result<GitCommitResult>.Failure(
                Application.Common.Errors.ErrorCode.ValidationFailed,
                "GitHub Personal Access Token is not configured");

        var path = request.TargetPath.TrimStart('/');
        var apiUrl = $"/repos/{_options.RepoOwner}/{_options.RepoName}/contents/{path}";

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", pat);

        // Check if file already exists (to get sha for update)
        string? existingSha = null;
        var getResponse = await client.GetAsync(apiUrl, ct);
        if (getResponse.StatusCode == HttpStatusCode.OK)
        {
            var existing = await getResponse.Content.ReadFromJsonAsync<GitHubFileResponse>(JsonOptions, ct);
            existingSha = existing?.Sha;
        }

        var contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(request.Html));
        var commitMessage = $"blog: publish {System.IO.Path.GetFileNameWithoutExtension(request.TargetPath)}";

        var payload = new GitHubPutFileRequest
        {
            Message = commitMessage,
            Content = contentBase64,
            Branch = _options.Branch,
            Sha = existingSha,
            Committer = new GitHubCommitter
            {
                Name = _options.AuthorName,
                Email = _options.AuthorEmail
            }
        };

        var putResponse = await client.PutAsJsonAsync(apiUrl, payload, JsonOptions, ct);

        if (!putResponse.IsSuccessStatusCode)
        {
            var errorBody = await putResponse.Content.ReadAsStringAsync(ct);
            var statusCode = (int)putResponse.StatusCode;

            var errorMessage = statusCode switch
            {
                401 => "GitHub authentication failed — check Personal Access Token",
                403 => "GitHub authorization denied — token may lack repo contents permission",
                422 => $"GitHub rejected the request — {errorBody}",
                _ => $"GitHub API returned {statusCode}: {errorBody}"
            };

            _logger.LogError("GitHub commit failed: {StatusCode} for {Path}", statusCode, path);
            return Result<GitCommitResult>.Failure(
                Application.Common.Errors.ErrorCode.ExternalServiceError, errorMessage);
        }

        var result = await putResponse.Content.ReadFromJsonAsync<GitHubPutFileResponse>(JsonOptions, ct);
        var commitSha = result?.Commit?.Sha ?? "unknown";
        var commitUrl = result?.Commit?.HtmlUrl ?? string.Empty;

        _logger.LogInformation(
            "Blog post committed to GitHub: {Path} (sha: {Sha})", path, commitSha);

        return Result<GitCommitResult>.Success(
            new GitCommitResult(commitSha, commitUrl, true, null));
    }

    public async Task<bool> VerifyDeploymentAsync(string blogPostUrl, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("BlogVerification");
        var initialDelay = TimeSpan.FromSeconds(_options.DeployVerificationInitialDelaySeconds);
        var maxRetries = _options.DeployVerificationMaxRetries;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            var delay = initialDelay * Math.Pow(2, attempt);
            _logger.LogDebug(
                "Deploy verification attempt {Attempt}/{Max} for {Url} — waiting {Delay}s",
                attempt + 1, maxRetries, blogPostUrl, delay.TotalSeconds);

            await Task.Delay(delay, ct);

            try
            {
                var response = await client.GetAsync(blogPostUrl, ct);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    _logger.LogInformation("Blog deployment verified: {Url}", blogPostUrl);
                    return true;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex,
                    "Network error during deploy verification attempt {Attempt}", attempt + 1);
            }
        }

        _logger.LogWarning("Deploy verification failed after {Max} retries for {Url}", maxRetries, blogPostUrl);
        return false;
    }

    // GitHub API DTOs
    private sealed class GitHubPutFileRequest
    {
        public string Message { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? Branch { get; set; }
        public string? Sha { get; set; }
        public GitHubCommitter? Committer { get; set; }
    }

    private sealed class GitHubCommitter
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    private sealed class GitHubFileResponse
    {
        public string? Sha { get; set; }
    }

    private sealed class GitHubPutFileResponse
    {
        public GitHubCommitInfo? Commit { get; set; }
    }

    private sealed class GitHubCommitInfo
    {
        public string? Sha { get; set; }
        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}
