using MediatR;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface ISocialInboxService
{
    Task<Result<IReadOnlyList<SocialInboxItem>>> GetItemsAsync(InboxFilterDto filter, CancellationToken ct);
    Task<Result<Unit>> MarkReadAsync(Guid id, CancellationToken ct);
    Task<Result<string>> DraftReplyAsync(Guid id, CancellationToken ct);
    Task<Result<Unit>> SendReplyAsync(Guid id, string replyText, CancellationToken ct);
}

public record InboxFilterDto(
    PlatformType? Platform = null,
    bool? IsRead = null,
    int Limit = 50);
