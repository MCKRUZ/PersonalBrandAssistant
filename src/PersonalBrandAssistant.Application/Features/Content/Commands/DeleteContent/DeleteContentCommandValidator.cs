using FluentValidation;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.DeleteContent;

public sealed class DeleteContentCommandValidator : AbstractValidator<DeleteContentCommand>
{
    public DeleteContentCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
