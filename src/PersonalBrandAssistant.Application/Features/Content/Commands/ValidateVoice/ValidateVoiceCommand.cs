using MediatR;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.ValidateVoice;

public sealed record ValidateVoiceCommand(Guid ContentId) : IRequest<Result<BrandVoiceScore>>;
