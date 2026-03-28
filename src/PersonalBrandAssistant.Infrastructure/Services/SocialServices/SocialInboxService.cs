using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Services.SocialServices;

public sealed class SocialInboxService : ISocialInboxService
{
    private readonly IApplicationDbContext _db;
    private readonly IEnumerable<ISocialEngagementAdapter> _adapters;
    private readonly ISidecarClient _sidecar;
    private readonly ILogger<SocialInboxService> _logger;

    public SocialInboxService(
        IApplicationDbContext db,
        IEnumerable<ISocialEngagementAdapter> adapters,
        ISidecarClient sidecar,
        ILogger<SocialInboxService> logger)
    {
        _db = db;
        _adapters = adapters;
        _sidecar = sidecar;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<SocialInboxItem>>> GetItemsAsync(
        InboxFilterDto filter, CancellationToken ct)
    {
        var query = _db.SocialInboxItems.AsQueryable();

        if (filter.Platform.HasValue)
            query = query.Where(i => i.Platform == filter.Platform.Value);

        if (filter.IsRead.HasValue)
            query = query.Where(i => i.IsRead == filter.IsRead.Value);

        var items = await query
            .OrderByDescending(i => i.ReceivedAt)
            .Take(Math.Clamp(filter.Limit, 1, 100))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<SocialInboxItem>>(items.AsReadOnly());
    }

    public async Task<Result<Unit>> MarkReadAsync(Guid id, CancellationToken ct)
    {
        var item = await _db.SocialInboxItems.FindAsync([id], ct);
        if (item is null)
            return Result.NotFound<Unit>("Inbox item not found");

        item.IsRead = true;
        await _db.SaveChangesAsync(ct);
        return Result.Success(Unit.Value);
    }

    public async Task<Result<string>> DraftReplyAsync(Guid id, CancellationToken ct)
    {
        var item = await _db.SocialInboxItems.FindAsync([id], ct);
        if (item is null)
            return Result.NotFound<string>("Inbox item not found");

        var prompt = $"""
            You are replying to a {item.Platform} {item.ItemType} from {item.AuthorName}.

            Original message:
            {item.Content}

            Write a helpful, professional reply that:
            - Addresses the specific content of their message
            - Matches {item.Platform} conventions
            - Is friendly and authentic
            - Is 1-3 sentences

            Reply with ONLY the reply text, no explanation.
            """;

        if (!_sidecar.IsConnected)
        {
            item.DraftReply = "Thanks for reaching out! I appreciate your message.";
            await _db.SaveChangesAsync(ct);
            return Result.Success(item.DraftReply);
        }

        var response = new System.Text.StringBuilder();
        await foreach (var evt in _sidecar.SendTaskAsync(prompt, null, null, ct))
        {
            if (evt is ChatEvent { Text: not null } chat)
                response.Append(chat.Text);
        }

        item.DraftReply = response.Length > 0
            ? response.ToString().Trim()
            : "Thanks for reaching out! I appreciate your message.";

        await _db.SaveChangesAsync(ct);
        return Result.Success(item.DraftReply);
    }

    public async Task<Result<Unit>> SendReplyAsync(Guid id, string replyText, CancellationToken ct)
    {
        var item = await _db.SocialInboxItems.FindAsync([id], ct);
        if (item is null)
            return Result.NotFound<Unit>("Inbox item not found");

        var adapter = _adapters.FirstOrDefault(a => a.Platform == item.Platform);
        if (adapter is null)
            return Result.Failure<Unit>(ErrorCode.InternalError,
                $"No engagement adapter for platform {item.Platform}");

        var result = await adapter.SendReplyAsync(item.PlatformItemId, replyText, ct);
        if (!result.IsSuccess)
            return Result.Failure<Unit>(ErrorCode.InternalError,
                string.Join("; ", result.Errors));

        item.DraftReply = replyText;
        item.ReplySent = true;
        await _db.SaveChangesAsync(ct);

        return Result.Success(Unit.Value);
    }
}
