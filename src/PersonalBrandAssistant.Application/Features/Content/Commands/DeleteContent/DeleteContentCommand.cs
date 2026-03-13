using MediatR;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.DeleteContent;

public sealed record DeleteContentCommand(Guid Id) : IRequest<Result<Unit>>;
