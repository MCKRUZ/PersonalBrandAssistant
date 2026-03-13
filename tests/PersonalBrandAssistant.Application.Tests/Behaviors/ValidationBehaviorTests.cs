using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Moq;
using PersonalBrandAssistant.Application.Common.Behaviors;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Tests.Behaviors;

public class ValidationBehaviorTests
{
    public sealed record TestRequest(string Name) : IRequest<Result<string>>;

    [Fact]
    public async Task Handle_NoValidators_CallsNext()
    {
        var behavior = new ValidationBehavior<TestRequest, Result<string>>(
            Enumerable.Empty<IValidator<TestRequest>>());

        var called = false;
        var result = await behavior.Handle(
            new TestRequest("test"),
            ct => { called = true; return Task.FromResult(Result<string>.Success("ok")); },
            CancellationToken.None);

        Assert.True(called);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_ValidationPasses_CallsNext()
    {
        var validator = new Mock<IValidator<TestRequest>>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var behavior = new ValidationBehavior<TestRequest, Result<string>>(new[] { validator.Object });

        var result = await behavior.Handle(
            new TestRequest("test"),
            ct => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_ValidationFails_ReturnsValidationFailure()
    {
        var validator = new Mock<IValidator<TestRequest>>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("Name", "Name is required")
            }));

        var behavior = new ValidationBehavior<TestRequest, Result<string>>(new[] { validator.Object });

        var result = await behavior.Handle(
            new TestRequest(""),
            ct => Task.FromResult(Result<string>.Success("should not reach")),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
        Assert.Contains("Name is required", result.Errors);
    }
}
