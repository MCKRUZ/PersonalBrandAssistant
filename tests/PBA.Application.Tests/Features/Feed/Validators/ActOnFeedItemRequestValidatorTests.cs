using FluentValidation.TestHelper;
using PBA.Application.Features.Feed.Dtos;
using PBA.Application.Features.Feed.Validators;
using Xunit;

namespace PBA.Application.Tests.Features.Feed.Validators;

public class ActOnFeedItemRequestValidatorTests
{
    private readonly ActOnFeedItemRequestValidator _validator = new();

    [Fact]
    public void Validate_EmptyAction_HasError()
    {
        var request = new ActOnFeedItemRequest { Action = "" };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Action);
    }

    [Fact]
    public void Validate_NullAction_HasError()
    {
        var request = new ActOnFeedItemRequest { Action = null! };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Action);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("dismiss")]
    [InlineData("view")]
    [InlineData("edit")]
    [InlineData("schedule")]
    [InlineData("create-content")]
    public void Validate_ValidAction_NoError(string action)
    {
        var request = new ActOnFeedItemRequest { Action = action };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Action);
    }

    [Fact]
    public void Validate_UnknownAction_HasError()
    {
        var request = new ActOnFeedItemRequest { Action = "unknown-action" };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Action);
    }
}
