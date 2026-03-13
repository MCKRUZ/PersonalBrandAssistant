using MediatR;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;
using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;

namespace PersonalBrandAssistant.Application.Features.Content.Queries.ListContent;

public sealed record ListContentQuery(
    ContentType? ContentType = null,
    ContentStatus? Status = null,
    int PageSize = 20,
    string? Cursor = null) : IRequest<Result<PagedResult<ContentEntity>>>;
