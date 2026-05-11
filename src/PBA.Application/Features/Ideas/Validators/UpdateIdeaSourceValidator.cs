using FluentValidation;
using PBA.Application.Features.Ideas.Commands;

namespace PBA.Application.Features.Ideas.Validators;

public class UpdateIdeaSourceValidator : AbstractValidator<UpdateIdeaSource.Command>
{
    public UpdateIdeaSourceValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name)
            .MaximumLength(200)
            .When(x => x.Name is not null);
        RuleFor(x => x.PollIntervalMinutes)
            .InclusiveBetween(5, 1440)
            .When(x => x.PollIntervalMinutes.HasValue);
    }
}
