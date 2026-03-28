using MediatR;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.GenerateOutline;

public sealed record GenerateOutlineCommand(Guid ContentId) : IRequest<Result<string>>;
