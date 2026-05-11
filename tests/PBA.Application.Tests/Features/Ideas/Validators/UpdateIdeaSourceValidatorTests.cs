using FluentValidation.TestHelper;
using PBA.Application.Features.Ideas.Commands;
using PBA.Application.Features.Ideas.Validators;
using Xunit;

namespace PBA.Application.Tests.Features.Ideas.Validators;

public class UpdateIdeaSourceValidatorTests
{
    private readonly UpdateIdeaSourceValidator _validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var command = new UpdateIdeaSource.Command
        {
            Id = Guid.NewGuid(),
            Name = "Updated Name"
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyId_Fails()
    {
        var command = new UpdateIdeaSource.Command { Id = Guid.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void Validate_NameExceeds200_WhenProvided_Fails()
    {
        var command = new UpdateIdeaSource.Command
        {
            Id = Guid.NewGuid(),
            Name = new string('a', 201)
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_PollIntervalOutOfRange_WhenProvided_Fails()
    {
        var command = new UpdateIdeaSource.Command
        {
            Id = Guid.NewGuid(),
            PollIntervalMinutes = 3
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.PollIntervalMinutes);
    }
}
