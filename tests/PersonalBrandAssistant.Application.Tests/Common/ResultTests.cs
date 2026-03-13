using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Tests.Common;

public class ResultTests
{
    [Fact]
    public void Success_SetsValueAndIsSuccess()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Equal(ErrorCode.None, result.ErrorCode);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Failure_SetsErrorCodeAndMessages()
    {
        var result = Result<int>.Failure(ErrorCode.InternalError, "Something went wrong");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InternalError, result.ErrorCode);
        Assert.Single(result.Errors);
        Assert.Equal("Something went wrong", result.Errors[0]);
    }

    [Fact]
    public void NotFound_SetsNotFoundErrorCode()
    {
        var result = Result<string>.NotFound("Item missing");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
        Assert.Contains("Item missing", result.Errors);
    }

    [Fact]
    public void ValidationFailure_SetsValidationFailedCodeAndAllErrors()
    {
        var errors = new[] { "Field A required", "Field B invalid" };
        var result = Result<string>.ValidationFailure(errors);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void Conflict_SetsConflictErrorCode()
    {
        var result = Result<string>.Conflict("Already exists");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Conflict, result.ErrorCode);
        Assert.Contains("Already exists", result.Errors);
    }
}
