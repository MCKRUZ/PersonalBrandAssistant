using FluentValidation;
using PBA.Application.Features.Ideas.Commands;

namespace PBA.Application.Features.Ideas.Validators;

public class CreateIdeaValidator : AbstractValidator<CreateIdea.Command>
{
    public CreateIdeaValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Url).MaximumLength(2000);
        RuleFor(x => x.Url)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .When(x => !string.IsNullOrEmpty(x.Url))
            .WithMessage("Url must be a valid absolute URI");
    }
}
