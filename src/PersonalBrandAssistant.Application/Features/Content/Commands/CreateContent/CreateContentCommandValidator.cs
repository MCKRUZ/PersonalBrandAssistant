using FluentValidation;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.CreateContent;

public sealed class CreateContentCommandValidator : AbstractValidator<CreateContentCommand>
{
    public CreateContentCommandValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(100_000);
        RuleFor(x => x.ContentType).IsInEnum();
    }
}
