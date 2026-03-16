using MediatR;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.GenerateDraft;

public sealed record GenerateDraftCommand(Guid ContentId) : IRequest<Result<string>>;
