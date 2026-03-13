using FluentValidation.TestHelper;
using PersonalBrandAssistant.Application.Features.Content.Commands.CreateContent;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Tests.Features.Content.Validators;

public class CreateContentCommandValidatorTests
{
    private readonly CreateContentCommandValidator _validator = new();

    [Fact]
    public void Validate_EmptyBody_HasError()
    {
        var command = new CreateContentCommand(ContentType.BlogPost, "");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Body);
    }

    [Fact]
    public void Validate_ValidCommand_NoErrors()
    {
        var command = new CreateContentCommand(ContentType.BlogPost, "Valid body");
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
