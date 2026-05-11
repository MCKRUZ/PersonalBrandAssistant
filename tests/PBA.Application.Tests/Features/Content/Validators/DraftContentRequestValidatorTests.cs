using FluentValidation.TestHelper;
using PBA.Application.Features.Content.Dtos;
using PBA.Application.Features.Content.Validators;
using Xunit;

namespace PBA.Application.Tests.Features.Content.Validators;

public class DraftContentRequestValidatorTests
{
    private readonly DraftContentRequestValidator _validator = new();

    [Fact]
    public void Validate_InvalidAction_HasError()
    {
        var request = new DraftContentRequest { Action = "invalid_action" };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Action);
    }

    [Fact]
    public void Validate_EmptyAction_HasError()
    {
        var request = new DraftContentRequest { Action = "" };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Action);
    }

    [Fact]
    public void Validate_InstructionsExceeds2000Chars_HasError()
    {
        var request = new DraftContentRequest
        {
            Action = "draft",
            Instructions = new string('x', 2001)
        };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Instructions);
    }

    [Fact]
    public void Validate_ToneNameExceeds200Chars_HasError()
    {
        var request = new DraftContentRequest
        {
            Action = "changeTone",
            ToneName = new string('x', 201)
        };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.ToneName);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("refine")]
    [InlineData("shorten")]
    [InlineData("expand")]
    [InlineData("changeTone")]
    public void Validate_ValidAction_NoErrors(string action)
    {
        var request = new DraftContentRequest { Action = action };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
