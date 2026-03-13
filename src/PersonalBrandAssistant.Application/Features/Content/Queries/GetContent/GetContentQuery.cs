using MediatR;
using PersonalBrandAssistant.Application.Common.Models;
using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;

namespace PersonalBrandAssistant.Application.Features.Content.Queries.GetContent;

public sealed record GetContentQuery(Guid Id) : IRequest<Result<ContentEntity>>;
