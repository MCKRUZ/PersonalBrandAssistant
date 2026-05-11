using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using PBA.Api.Extensions;
using PBA.Domain.Common;
using Xunit;

namespace PBA.Api.Tests.Extensions;

public class ResultExtensionsTests
{
    [Fact]
    public void Success_MapsToOkWithValue()
    {
        var result = Result<string>.Success("hello");

        var apiResult = result.ToApiResult();

        var okResult = Assert.IsType<Ok<string>>(apiResult);
        Assert.Equal("hello", okResult.Value);
    }

    [Fact]
    public void ValidationFailure_MapsToBadRequestWithErrors()
    {
        var errors = new List<string> { "Name is required", "Email is invalid" };
        var result = Result<string>.ValidationFailure(errors);

        var apiResult = result.ToApiResult();

        var badRequest = Assert.IsType<BadRequest<IReadOnlyList<string>>>(apiResult);
        Assert.Equal(2, badRequest.Value!.Count);
    }

    [Fact]
    public void NotFound_MapsToNotFoundWithMessage()
    {
        var result = Result<string>.NotFound("Item not found");

        var apiResult = result.ToApiResult();

        var notFound = Assert.IsType<NotFound<string?>>(apiResult);
        Assert.Equal("Item not found", notFound.Value);
    }

    [Fact]
    public void GeneralFailure_MapsToProblem()
    {
        var result = Result<string>.Fail("Something went wrong");

        var apiResult = result.ToApiResult();

        Assert.IsAssignableFrom<ProblemHttpResult>(apiResult);
    }

    [Fact]
    public void Unauthorized_MapsTo401()
    {
        var result = Result<string>.Unauthorized("Not authenticated");

        var apiResult = result.ToApiResult();

        Assert.IsType<UnauthorizedHttpResult>(apiResult);
    }

    [Fact]
    public void Forbidden_MapsToForbid()
    {
        var result = Result<string>.Forbidden("Access denied");

        var apiResult = result.ToApiResult();

        Assert.IsType<ForbidHttpResult>(apiResult);
    }

    [Fact]
    public void ContentBlocked_MapsToForbid()
    {
        var result = Result<string>.ContentBlocked("Content blocked");

        var apiResult = result.ToApiResult();

        Assert.IsType<ForbidHttpResult>(apiResult);
    }

    [Fact]
    public void PermissionRequired_MapsToForbid()
    {
        var result = Result<string>.PermissionRequired("Permission needed");

        var apiResult = result.ToApiResult();

        Assert.IsType<ForbidHttpResult>(apiResult);
    }

    [Fact]
    public void GovernanceBlocked_MapsToForbid()
    {
        var result = Result<string>.GovernanceBlocked("Governance check failed");

        var apiResult = result.ToApiResult();

        Assert.IsType<ForbidHttpResult>(apiResult);
    }
}
