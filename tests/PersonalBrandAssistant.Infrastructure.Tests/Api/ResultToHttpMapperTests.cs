using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Tests.Api;

public class ResultToHttpMapperTests
{
    [Fact]
    public void Success_MapsTo200WithValue()
    {
        var result = Result<string>.Success("hello");
        var httpResult = result.ToHttpResult();

        var okResult = Assert.IsType<Ok<string>>(httpResult);
        Assert.Equal("hello", okResult.Value);
    }

    [Fact]
    public void ValidationFailure_MapsTo400ProblemDetails()
    {
        var result = Result<string>.ValidationFailure(["Field is required", "Invalid format"]);
        var httpResult = result.ToHttpResult();

        var problemResult = Assert.IsType<ProblemHttpResult>(httpResult);
        Assert.Equal(StatusCodes.Status400BadRequest, problemResult.StatusCode);
        Assert.Equal("Validation Failed", problemResult.ProblemDetails.Title);
        Assert.Contains("errors", problemResult.ProblemDetails.Extensions.Keys);
    }

    [Fact]
    public void NotFound_MapsTo404ProblemDetails()
    {
        var result = Result<string>.NotFound("Item not found");
        var httpResult = result.ToHttpResult();

        var problemResult = Assert.IsType<ProblemHttpResult>(httpResult);
        Assert.Equal(StatusCodes.Status404NotFound, problemResult.StatusCode);
        Assert.Equal("Not Found", problemResult.ProblemDetails.Title);
        Assert.Equal("Item not found", problemResult.ProblemDetails.Detail);
    }

    [Fact]
    public void Conflict_MapsTo409ProblemDetails()
    {
        var result = Result<string>.Conflict("Version mismatch");
        var httpResult = result.ToHttpResult();

        var problemResult = Assert.IsType<ProblemHttpResult>(httpResult);
        Assert.Equal(StatusCodes.Status409Conflict, problemResult.StatusCode);
        Assert.Equal("Conflict", problemResult.ProblemDetails.Title);
    }

    [Fact]
    public void Unauthorized_MapsTo401ProblemDetails()
    {
        var result = Result<string>.Failure(ErrorCode.Unauthorized, "Not authorized");
        var httpResult = result.ToHttpResult();

        var problemResult = Assert.IsType<ProblemHttpResult>(httpResult);
        Assert.Equal(StatusCodes.Status401Unauthorized, problemResult.StatusCode);
    }

    [Fact]
    public void InternalError_MapsTo500ProblemDetails()
    {
        var result = Result<string>.Failure(ErrorCode.InternalError, "Something broke");
        var httpResult = result.ToHttpResult();

        var problemResult = Assert.IsType<ProblemHttpResult>(httpResult);
        Assert.Equal(StatusCodes.Status500InternalServerError, problemResult.StatusCode);
        Assert.Equal("An unexpected error occurred.", problemResult.ProblemDetails.Detail);
    }

    [Fact]
    public void AllErrors_IncludeRequiredRfc9457Fields()
    {
        var errorCodes = new[] { ErrorCode.ValidationFailed, ErrorCode.NotFound, ErrorCode.Conflict };

        foreach (var errorCode in errorCodes)
        {
            var result = Result<string>.Failure(errorCode, "test error");
            var httpResult = result.ToHttpResult();
            var problemResult = Assert.IsType<ProblemHttpResult>(httpResult);

            Assert.NotNull(problemResult.ProblemDetails.Type);
            Assert.NotNull(problemResult.ProblemDetails.Title);
            Assert.NotNull(problemResult.ProblemDetails.Status);
            Assert.NotNull(problemResult.ProblemDetails.Detail);
        }
    }

    [Fact]
    public void ToCreatedHttpResult_Success_Returns201WithLocation()
    {
        var id = Guid.NewGuid();
        var result = Result<Guid>.Success(id);
        var httpResult = result.ToCreatedHttpResult("/api/content");

        var createdResult = Assert.IsType<Created<Guid>>(httpResult);
        Assert.Equal($"/api/content/{id}", createdResult.Location);
    }

    [Fact]
    public void ToCreatedHttpResult_Failure_DelegatesToToHttpResult()
    {
        var result = Result<Guid>.NotFound("Not found");
        var httpResult = result.ToCreatedHttpResult("/api/content");

        var problemResult = Assert.IsType<ProblemHttpResult>(httpResult);
        Assert.Equal(StatusCodes.Status404NotFound, problemResult.StatusCode);
    }
}
