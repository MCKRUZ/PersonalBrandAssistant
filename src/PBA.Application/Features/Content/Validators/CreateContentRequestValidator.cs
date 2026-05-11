using FluentValidation;
using PBA.Application.Features.Content.Dtos;

namespace PBA.Application.Features.Content.Validators;

public class CreateContentRequestValidator : AbstractValidator<CreateContentRequest>
{
    public CreateContentRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContentType).IsInEnum();
        RuleFor(x => x.PrimaryPlatform).IsInEnum();
    }
}
