using FluentValidation;
using PBA.Application.Features.Feed.Dtos;

namespace PBA.Application.Features.Feed.Validators;

public class BatchActRequestValidator : AbstractValidator<BatchActRequest>
{
    public BatchActRequestValidator()
    {
        RuleFor(x => x.Ids).NotEmpty();
        RuleFor(x => x.Action)
            .NotEmpty()
            .Must(a => ActOnFeedItemRequestValidator.KnownActions.Contains(a))
            .WithMessage("Action must be one of: " + string.Join(", ", ActOnFeedItemRequestValidator.KnownActions));
    }
}
