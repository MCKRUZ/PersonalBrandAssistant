using FluentValidation.TestHelper;
using PBA.Application.Features.Content.Commands;
using PBA.Application.Features.Content.Validators;
using PBA.Domain.Enums;
using Xunit;

namespace PBA.Application.Tests.Features.Content.Validators;

public class CreateContentCommandValidatorTests
{
    private readonly CreateContentCommandValidator _validator = new();

    [Fact]
    public void Validate_EmptyTitle_HasError()
    {
        var command = new CreateContent.Command("", ContentType.Blog, Platform.Blog, null, []);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void Validate_TitleExceeds200Chars_HasError()
    {
        var command = new CreateContent.Command(new string('x', 201), ContentType.Blog, Platform.Blog, null, []);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void Validate_InvalidContentType_HasError()
    {
        var command = new CreateContent.Command("Valid Title", (ContentType)999, Platform.Blog, null, []);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ContentType);
    }

    [Fact]
    public void Validate_InvalidPlatform_HasError()
    {
        var command = new CreateContent.Command("Valid Title", ContentType.Blog, (Platform)999, null, []);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.PrimaryPlatform);
    }

    [Fact]
    public void Validate_ValidCommand_NoErrors()
    {
        var command = new CreateContent.Command("Valid Title", ContentType.Blog, Platform.Blog, null, []);
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
