using FluentValidation;
using PBA.Application.Features.Feed.Dtos;

namespace PBA.Application.Features.Feed.Validators;

public class BatchDismissRequestValidator : AbstractValidator<BatchDismissRequest>
{
    public BatchDismissRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
    }
}
