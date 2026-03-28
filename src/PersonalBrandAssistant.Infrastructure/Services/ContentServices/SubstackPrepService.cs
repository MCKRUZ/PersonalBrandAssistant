using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;

public class SubstackPrepService : ISubstackPrepService
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<SubstackPrepService> _logger;

    public SubstackPrepService(IApplicationDbContext db, ILogger<SubstackPrepService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<SubstackPreparedContent>> PrepareAsync(Guid contentId, CancellationToken ct)
    {
        var content = await _db.Contents.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == contentId, ct);

        if (content is null)
            return Result<SubstackPreparedContent>.NotFound("Content not found");

        var title = content.Title ?? "Untitled";
        var body = content.Body ?? "";

        var cleanBody = MarkdownSanitizer.StripHtml(body);
        var demotedBody = MarkdownSanitizer.DemoteHeadings(cleanBody);

        var subtitle = MarkdownSanitizer.ExtractFirstParagraph(body);
        var plainText = MarkdownSanitizer.ToPlainText(body);
        var previewText = MarkdownSanitizer.TruncateAtWordBoundary(plainText, 200);

        var seoDescription = MarkdownSanitizer.TruncateAtWordBoundary(plainText, 160);

        var tags = content.Metadata?.Tags?.ToArray() ?? [];
        string? sectionName = null;

        string? canonicalUrl = content.BlogPostUrl;

        return Result<SubstackPreparedContent>.Success(new SubstackPreparedContent(
            title, subtitle, demotedBody, seoDescription, tags, sectionName, previewText, canonicalUrl));
    }

    public async Task<Result<SubstackPublishConfirmation>> MarkPublishedAsync(
        Guid contentId, string? substackUrl, CancellationToken ct)
    {
        var content = await _db.Contents
            .FirstOrDefaultAsync(c => c.Id == contentId, ct);

        if (content is null)
            return Result<SubstackPublishConfirmation>.NotFound("Content not found");

        if (!string.IsNullOrWhiteSpace(content.SubstackPostUrl))
        {
            return Result<SubstackPublishConfirmation>.Success(
                new SubstackPublishConfirmation(contentId, content.SubstackPostUrl, WasAlreadyPublished: true));
        }

        content.SubstackPostUrl = substackUrl;

        var platformStatus = await _db.ContentPlatformStatuses
            .FirstOrDefaultAsync(s => s.ContentId == contentId && s.Platform == PlatformType.Substack, ct);

        if (platformStatus is not null)
        {
            platformStatus.Status = PlatformPublishStatus.Published;
            platformStatus.PostUrl = substackUrl;
            platformStatus.PublishedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            _db.ContentPlatformStatuses.Add(new ContentPlatformStatus
            {
                ContentId = contentId,
                Platform = PlatformType.Substack,
                Status = PlatformPublishStatus.Published,
                PostUrl = substackUrl,
                PublishedAt = DateTimeOffset.UtcNow,
            });
        }

        if (!string.IsNullOrWhiteSpace(substackUrl))
        {
            var existingDetection = await _db.SubstackDetections
                .AnyAsync(d => d.SubstackUrl == substackUrl, ct);

            if (!existingDetection)
            {
                var contentHash = Convert.ToHexStringLower(
                    SHA256.HashData(Encoding.UTF8.GetBytes(content.Body ?? "")));

                _db.SubstackDetections.Add(new SubstackDetection
                {
                    ContentId = contentId,
                    RssGuid = $"manual:{contentId}",
                    Title = content.Title ?? "Untitled",
                    SubstackUrl = substackUrl,
                    PublishedAt = DateTimeOffset.UtcNow,
                    DetectedAt = DateTimeOffset.UtcNow,
                    Confidence = MatchConfidence.High,
                    ContentHash = contentHash,
                });
            }
        }

        if (content.TargetPlatforms.Contains(PlatformType.PersonalBlog))
        {
            _db.UserNotifications.Add(new UserNotification
            {
                Type = nameof(NotificationType.BlogReady),
                Message = $"Blog post '{content.Title}' published on Substack. Ready for blog scheduling.",
                ContentId = contentId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await _db.SaveChangesAsync(ct);

        return Result<SubstackPublishConfirmation>.Success(
            new SubstackPublishConfirmation(contentId, substackUrl, WasAlreadyPublished: false));
    }
}
