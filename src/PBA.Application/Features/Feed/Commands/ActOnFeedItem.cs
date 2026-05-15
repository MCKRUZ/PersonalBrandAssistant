using System.Text.Json;
using MediatR;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Content.Commands;
using PBA.Application.Features.Ideas.Commands;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Feed.Commands;

public static class ActOnFeedItem
{
    public record Command(Guid Id, string Action) : IRequest<Result<ActOnFeedItemResponse>>;

    public record ActOnFeedItemResponse(bool Success, string? NavigationTarget = null, Guid? TargetId = null);

    public sealed class Handler(IAppDbContext db, ISender sender) : IRequestHandler<Command, Result<ActOnFeedItemResponse>>
    {
        public async Task<Result<ActOnFeedItemResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var item = await db.FeedItems.FindAsync([request.Id], cancellationToken);
            if (item is null)
                return Result<ActOnFeedItemResponse>.NotFound($"Feed item {request.Id} not found");

            var action = request.Action.ToLowerInvariant();
            ActOnFeedItemResponse response;

            switch (item.Type, action)
            {
                case (FeedItemType.AgentDraft, "approve"):
                {
                    if (item.ActionTargetId is null)
                        return Result<ActOnFeedItemResponse>.ValidationFailure(
                            [$"Feed item {request.Id} has no ActionTargetId for approve action"]);
                    var subResult = await sender.Send(new ApproveContent.Command(item.ActionTargetId.Value), cancellationToken);
                    if (!subResult.IsSuccess)
                        return Result<ActOnFeedItemResponse>.Fail(subResult.Errors.ToArray());
                    item.IsRead = true;
                    item.IsActedOn = true;
                    response = new ActOnFeedItemResponse(true, "/content", item.ActionTargetId);
                    break;
                }
                case (FeedItemType.AgentDraft, "dismiss"):
                    item.IsRead = true;
                    item.IsActedOn = true;
                    response = new ActOnFeedItemResponse(true);
                    break;

                case (FeedItemType.TrendAlert, "view"):
                    item.IsRead = true;
                    response = new ActOnFeedItemResponse(true);
                    break;
                case (FeedItemType.TrendAlert, "dismiss"):
                    item.IsRead = true;
                    item.IsActedOn = true;
                    response = new ActOnFeedItemResponse(true);
                    break;

                case (FeedItemType.IdeaSuggestion, "create-content"):
                {
                    if (item.ActionTargetId is null)
                        return Result<ActOnFeedItemResponse>.ValidationFailure(
                            [$"Feed item {request.Id} has no ActionTargetId for create-content action"]);
                    var parseResult = ParseIdeaSuggestionData(item.Data);
                    if (!parseResult.IsSuccess)
                        return Result<ActOnFeedItemResponse>.ValidationFailure(parseResult.Errors);

                    var (contentType, platform) = parseResult.Value!;
                    var subResult = await sender.Send(
                        new CreateContentFromIdea.Command(item.ActionTargetId.Value, contentType, platform),
                        cancellationToken);
                    if (!subResult.IsSuccess)
                        return Result<ActOnFeedItemResponse>.Fail(subResult.Errors.ToArray());

                    item.IsRead = true;
                    item.IsActedOn = true;
                    response = new ActOnFeedItemResponse(true, $"/content/{subResult.Value}", subResult.Value);
                    break;
                }
                case (FeedItemType.IdeaSuggestion, "dismiss"):
                    item.IsRead = true;
                    item.IsActedOn = true;
                    response = new ActOnFeedItemResponse(true);
                    break;

                case (FeedItemType.AnalyticsHighlight, "view"):
                    item.IsRead = true;
                    response = new ActOnFeedItemResponse(true);
                    break;
                case (FeedItemType.AnalyticsHighlight, "dismiss"):
                    item.IsRead = true;
                    item.IsActedOn = true;
                    response = new ActOnFeedItemResponse(true);
                    break;

                case (FeedItemType.ApprovalRequest, "approve"):
                {
                    if (item.ActionTargetId is null)
                        return Result<ActOnFeedItemResponse>.ValidationFailure(
                            [$"Feed item {request.Id} has no ActionTargetId for approve action"]);
                    var subResult = await sender.Send(new ApproveContent.Command(item.ActionTargetId.Value), cancellationToken);
                    if (!subResult.IsSuccess)
                        return Result<ActOnFeedItemResponse>.Fail(subResult.Errors.ToArray());
                    item.IsRead = true;
                    item.IsActedOn = true;
                    response = new ActOnFeedItemResponse(true, "/content", item.ActionTargetId);
                    break;
                }
                case (FeedItemType.ApprovalRequest, "dismiss"):
                    item.IsRead = true;
                    item.IsActedOn = true;
                    response = new ActOnFeedItemResponse(true);
                    break;

                case (FeedItemType.SystemNotification, "view"):
                    item.IsRead = true;
                    response = new ActOnFeedItemResponse(true);
                    break;
                case (FeedItemType.SystemNotification, "dismiss"):
                    item.IsRead = true;
                    item.IsActedOn = true;
                    response = new ActOnFeedItemResponse(true);
                    break;

                default:
                    return Result<ActOnFeedItemResponse>.ValidationFailure(
                        [$"Unknown action '{request.Action}' for feed item type '{item.Type}'"]);
            }

            await db.SaveChangesAsync(cancellationToken);
            return response;
        }

        private static Result<(ContentType ContentType, Platform Platform)> ParseIdeaSuggestionData(string? data)
        {
            if (string.IsNullOrEmpty(data))
                return Result<(ContentType, Platform)>.ValidationFailure(["IdeaSuggestion Data is empty"]);

            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                if (!root.TryGetProperty("contentType", out var ctProp) ||
                    !Enum.TryParse<ContentType>(ctProp.GetString(), true, out var contentType))
                    return Result<(ContentType, Platform)>.ValidationFailure(["Invalid or missing contentType in Data"]);

                if (!root.TryGetProperty("primaryPlatform", out var ppProp) ||
                    !Enum.TryParse<Platform>(ppProp.GetString(), true, out var platform))
                    return Result<(ContentType, Platform)>.ValidationFailure(["Invalid or missing primaryPlatform in Data"]);

                return (contentType, platform);
            }
            catch (JsonException)
            {
                return Result<(ContentType, Platform)>.ValidationFailure(["Failed to parse IdeaSuggestion Data JSON"]);
            }
        }
    }
}
