using FluentValidation.TestHelper;
using PBA.Application.Features.Feed.Dtos;
using PBA.Application.Features.Feed.Validators;
using Xunit;

namespace PBA.Application.Tests.Features.Feed.Validators;

public class BatchActRequestValidatorTests
{
    private readonly BatchActRequestValidator _validator = new();

    [Fact]
    public void Validate_EmptyIds_HasError()
    {
        var request = new BatchActRequest { Ids = [], Action = "approve" };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Ids);
    }

    [Fact]
    public void Validate_NullIds_HasError()
    {
        var request = new BatchActRequest { Ids = null!, Action = "approve" };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Ids);
    }

    [Fact]
    public void Validate_EmptyAction_HasError()
    {
        var request = new BatchActRequest { Ids = [Guid.NewGuid()], Action = "" };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Action);
    }

    [Fact]
    public void Validate_ValidRequest_NoErrors()
    {
        var request = new BatchActRequest { Ids = [Guid.NewGuid()], Action = "approve" };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
