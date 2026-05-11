using FluentValidation.TestHelper;
using PBA.Application.Features.Ideas.Commands;
using PBA.Application.Features.Ideas.Validators;
using Xunit;

namespace PBA.Application.Tests.Features.Ideas.Validators;

public class CreateIdeaValidatorTests
{
    private readonly CreateIdeaValidator _validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var command = new CreateIdea.Command { Title = "Valid title" };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyTitle_Fails()
    {
        var command = new CreateIdea.Command { Title = "" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void Validate_TitleExceeds500_Fails()
    {
        var command = new CreateIdea.Command { Title = new string('a', 501) };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void Validate_UrlExceeds2000_Fails()
    {
        var command = new CreateIdea.Command
        {
            Title = "Valid",
            Url = "https://example.com/" + new string('a', 2000)
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Url);
    }

    [Fact]
    public void Validate_InvalidUrl_Fails()
    {
        var command = new CreateIdea.Command { Title = "Valid", Url = "not-a-url" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Url);
    }

    [Fact]
    public void Validate_ValidUrl_Passes()
    {
        var command = new CreateIdea.Command
        {
            Title = "Valid",
            Url = "https://example.com/article"
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Url);
    }
}
