using FluentValidation.TestHelper;
using PBA.Application.Features.Content.Dtos;
using PBA.Application.Features.Content.Validators;
using PBA.Domain.Enums;
using Xunit;

namespace PBA.Application.Tests.Features.Content.Validators;

public class UpdateContentRequestValidatorTests
{
    private readonly UpdateContentRequestValidator _validator = new();

    [Fact]
    public void Validate_BodyExceeds100KChars_HasError()
    {
        var request = new UpdateContentRequest
        {
            Body = new string('x', 100_001),
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Body);
    }

    [Fact]
    public void Validate_TitleExceeds200Chars_HasError()
    {
        var request = new UpdateContentRequest
        {
            Title = new string('x', 201),
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void Validate_EmptyTitle_HasError()
    {
        var request = new UpdateContentRequest
        {
            Title = "",
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void Validate_InvalidContentType_HasError()
    {
        var request = new UpdateContentRequest
        {
            ContentType = (ContentType)999,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.ContentType);
    }

    [Fact]
    public void Validate_InvalidPlatform_HasError()
    {
        var request = new UpdateContentRequest
        {
            PrimaryPlatform = (Platform)999,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.PrimaryPlatform);
    }

    [Fact]
    public void Validate_MissingLastUpdatedAt_HasError()
    {
        var request = new UpdateContentRequest
        {
            LastUpdatedAt = default
        };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.LastUpdatedAt);
    }

    [Fact]
    public void Validate_AllNullOptionalFields_NoErrors()
    {
        var request = new UpdateContentRequest
        {
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ValidRequest_NoErrors()
    {
        var request = new UpdateContentRequest
        {
            Body = "Some content body",
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
