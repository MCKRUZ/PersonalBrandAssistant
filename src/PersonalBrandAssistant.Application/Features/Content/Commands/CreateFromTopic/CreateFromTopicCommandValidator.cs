using FluentValidation;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.CreateFromTopic;

public sealed class CreateFromTopicCommandValidator : AbstractValidator<CreateFromTopicCommand>
{
    public CreateFromTopicCommandValidator()
    {
        RuleFor(x => x.Topic).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ContentType).IsInEnum();
    }
}
