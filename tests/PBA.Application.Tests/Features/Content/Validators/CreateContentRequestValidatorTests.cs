using FluentValidation.TestHelper;
using PBA.Application.Features.Content.Dtos;
using PBA.Application.Features.Content.Validators;
using PBA.Domain.Enums;
using Xunit;

namespace PBA.Application.Tests.Features.Content.Validators;

public class CreateContentRequestValidatorTests
{
    private readonly CreateContentRequestValidator _validator = new();

    [Fact]
    public void Validate_EmptyTitle_HasError()
    {
        var request = new CreateContentRequest
        {
            Title = "",
            ContentType = ContentType.BlogPost,
            PrimaryPlatform = Platform.Blog
        };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void Validate_TitleExceeds200Chars_HasError()
    {
        var request = new CreateContentRequest
        {
            Title = new string('x', 201),
            ContentType = ContentType.BlogPost,
            PrimaryPlatform = Platform.Blog
        };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void Validate_InvalidContentType_HasError()
    {
        var request = new CreateContentRequest
        {
            Title = "Valid Title",
            ContentType = (ContentType)999,
            PrimaryPlatform = Platform.Blog
        };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.ContentType);
    }

    [Fact]
    public void Validate_InvalidPlatform_HasError()
    {
        var request = new CreateContentRequest
        {
            Title = "Valid Title",
            ContentType = ContentType.BlogPost,
            PrimaryPlatform = (Platform)999
        };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.PrimaryPlatform);
    }

    [Fact]
    public void Validate_ValidRequest_NoErrors()
    {
        var request = new CreateContentRequest
        {
            Title = "Valid Title",
            ContentType = ContentType.BlogPost,
            PrimaryPlatform = Platform.Blog
        };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
