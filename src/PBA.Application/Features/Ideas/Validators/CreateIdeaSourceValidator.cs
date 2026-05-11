using FluentValidation;
using PBA.Application.Features.Ideas.Commands;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Ideas.Validators;

public class CreateIdeaSourceValidator : AbstractValidator<CreateIdeaSource.Command>
{
    public CreateIdeaSourceValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FeedUrl)
            .NotEmpty()
            .When(x => x.Type == IdeaSourceType.RSS)
            .WithMessage("FeedUrl is required for RSS sources");
        RuleFor(x => x.PollIntervalMinutes)
            .InclusiveBetween(5, 1440);
    }
}
