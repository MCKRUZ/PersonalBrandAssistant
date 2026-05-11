using FluentValidation;
using PBA.Application.Features.Content.Dtos;

namespace PBA.Application.Features.Content.Validators;

public class UpdateContentRequestValidator : AbstractValidator<UpdateContentRequest>
{
    public UpdateContentRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200).When(x => x.Title is not null);
        RuleFor(x => x.Body).MaximumLength(100_000).When(x => x.Body is not null);
        RuleFor(x => x.ContentType).IsInEnum().When(x => x.ContentType.HasValue);
        RuleFor(x => x.PrimaryPlatform).IsInEnum().When(x => x.PrimaryPlatform.HasValue);
        RuleFor(x => x.LastUpdatedAt).NotEqual(default(DateTimeOffset));
    }
}
