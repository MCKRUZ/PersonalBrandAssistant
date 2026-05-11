using FluentValidation;
using PBA.Application.Features.Content.Dtos;

namespace PBA.Application.Features.Content.Validators;

public class ScheduleContentRequestValidator : AbstractValidator<ScheduleContentRequest>
{
    public ScheduleContentRequestValidator()
    {
        RuleFor(x => x.ScheduledAt)
            .GreaterThan(DateTimeOffset.UtcNow)
            .WithMessage("ScheduledAt must be in the future");
    }
}
