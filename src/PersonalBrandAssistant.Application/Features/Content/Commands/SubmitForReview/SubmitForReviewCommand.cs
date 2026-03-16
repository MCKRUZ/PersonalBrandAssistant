using MediatR;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.SubmitForReview;

public sealed record SubmitForReviewCommand(Guid ContentId) : IRequest<Result<Unit>>;
