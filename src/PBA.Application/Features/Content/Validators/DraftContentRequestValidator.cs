using FluentValidation;
using PBA.Application.Features.Content.Dtos;

namespace PBA.Application.Features.Content.Validators;

public class DraftContentRequestValidator : AbstractValidator<DraftContentRequest>
{
    private static readonly HashSet<string> ValidActions = ["draft", "refine", "shorten", "expand", "changeTone"];

    public DraftContentRequestValidator()
    {
        RuleFor(x => x.Action)
            .NotEmpty()
            .Must(a => ValidActions.Contains(a))
            .WithMessage("Action must be one of: draft, refine, shorten, expand, changeTone");

        RuleFor(x => x.Instructions).MaximumLength(2000).When(x => x.Instructions is not null);
        RuleFor(x => x.ToneName).MaximumLength(200).When(x => x.ToneName is not null);
    }
}
