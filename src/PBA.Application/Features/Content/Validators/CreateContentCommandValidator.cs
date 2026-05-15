using FluentValidation;
using PBA.Application.Features.Content.Commands;

namespace PBA.Application.Features.Content.Validators;

public class CreateContentCommandValidator : AbstractValidator<CreateContent.Command>
{
    public CreateContentCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContentType).IsInEnum();
        RuleFor(x => x.PrimaryPlatform).IsInEnum();
    }
}
