using FluentValidation;

namespace PersonalBrandAssistant.Application.Features.Settings.Commands.UpdateAutonomySettings;

public sealed class UpdateAutonomySettingsCommandValidator
    : AbstractValidator<UpdateAutonomySettingsCommand>
{
    public UpdateAutonomySettingsCommandValidator()
    {
        RuleFor(x => x.GlobalLevel).IsInEnum();
        RuleFor(x => x.MaxAutoPostsPerDay).InclusiveBetween(0, 100);
        RuleFor(x => x.DefaultTone).NotEmpty().MaximumLength(50);
    }
}
