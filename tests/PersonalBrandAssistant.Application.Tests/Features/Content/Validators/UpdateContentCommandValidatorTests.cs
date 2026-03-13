using FluentValidation.TestHelper;
using PersonalBrandAssistant.Application.Features.Content.Commands.UpdateContent;

namespace PersonalBrandAssistant.Application.Tests.Features.Content.Validators;

public class UpdateContentCommandValidatorTests
{
    private readonly UpdateContentCommandValidator _validator = new();

    [Fact]
    public void Validate_EmptyId_HasError()
    {
        var command = new UpdateContentCommand(Guid.Empty, Title: "Title");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void Validate_NoFieldsProvided_HasError()
    {
        var command = new UpdateContentCommand(Guid.NewGuid());
        var result = _validator.TestValidate(command);
        Assert.False(result.IsValid);
    }
}
