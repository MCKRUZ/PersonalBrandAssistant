using FluentValidation;
using PBA.Application.Features.Feed.Dtos;

namespace PBA.Application.Features.Feed.Validators;

public class ActOnFeedItemRequestValidator : AbstractValidator<ActOnFeedItemRequest>
{
    public static readonly string[] KnownActions =
        ["approve", "dismiss", "view", "create-content"];

    public ActOnFeedItemRequestValidator()
    {
        RuleFor(x => x.Action)
            .NotEmpty()
            .Must(a => KnownActions.Contains(a))
            .WithMessage("Action must be one of: " + string.Join(", ", KnownActions));
    }
}
