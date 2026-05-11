using FluentValidation;
using PBA.Application.Features.Ideas.Commands;

namespace PBA.Application.Features.Ideas.Validators;

public class SaveIdeaValidator : AbstractValidator<SaveIdea.Command>
{
    public SaveIdeaValidator()
    {
        RuleFor(x => x.IdeaId).NotEmpty();
    }
}
