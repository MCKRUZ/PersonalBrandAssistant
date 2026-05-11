using FluentValidation.TestHelper;
using PBA.Application.Features.Content.Dtos;
using PBA.Application.Features.Content.Validators;
using PBA.Domain.Enums;
using Xunit;

namespace PBA.Application.Tests.Features.Content.Validators;

public class CrossPostRequestValidatorTests
{
    private readonly CrossPostRequestValidator _validator = new();

    [Fact]
    public void Validate_InvalidPlatform_HasError()
    {
        var request = new CrossPostRequest { TargetPlatform = (Platform)999 };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.TargetPlatform);
    }

    [Fact]
    public void Validate_ValidPlatform_NoErrors()
    {
        var request = new CrossPostRequest { TargetPlatform = Platform.LinkedIn };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
