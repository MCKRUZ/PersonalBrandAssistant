using FluentValidation.TestHelper;
using PBA.Application.Features.Ideas.Commands;
using PBA.Application.Features.Ideas.Validators;
using Xunit;

namespace PBA.Application.Tests.Features.Ideas.Validators;

public class SaveIdeaValidatorTests
{
    private readonly SaveIdeaValidator _validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var command = new SaveIdea.Command { IdeaId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyGuid_Fails()
    {
        var command = new SaveIdea.Command { IdeaId = Guid.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.IdeaId);
    }

    [Fact]
    public void Validate_NullNotes_Passes()
    {
        var command = new SaveIdea.Command
        {
            IdeaId = Guid.NewGuid(),
            Notes = null
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void Validate_EmptyTags_Passes()
    {
        var command = new SaveIdea.Command
        {
            IdeaId = Guid.NewGuid(),
            Tags = []
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Tags);
    }
}
