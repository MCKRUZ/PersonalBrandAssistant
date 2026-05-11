using FluentValidation;
using PBA.Application.Features.Content.Dtos;

namespace PBA.Application.Features.Content.Validators;

public class CrossPostRequestValidator : AbstractValidator<CrossPostRequest>
{
    public CrossPostRequestValidator()
    {
        RuleFor(x => x.TargetPlatform).IsInEnum();
    }
}
