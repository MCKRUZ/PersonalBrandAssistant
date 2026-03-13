using FluentValidation;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.UpdateContent;

public sealed class UpdateContentCommandValidator : AbstractValidator<UpdateContentCommand>
{
    public UpdateContentCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x)
            .Must(x => x.Title is not null || x.Body is not null ||
                        x.TargetPlatforms is not null || x.Metadata is not null)
            .WithMessage("At least one field must be provided for update.");
    }
}
