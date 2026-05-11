using PBA.Domain.Common;

namespace PBA.Api.Extensions;

public static class ResultExtensions
{
    public static IResult ToApiResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
            return Results.Ok(result.Value);

        return MapFailure(result);
    }

    public static IResult ToApiResult(this Result result)
    {
        if (result.IsSuccess)
            return Results.Ok();

        return MapFailure(result);
    }

    private static IResult MapFailure(Result result) =>
        result.FailureType switch
        {
            ResultFailureType.Validation => Results.BadRequest(result.Errors),
            ResultFailureType.NotFound => Results.NotFound(result.Errors.FirstOrDefault()),
            ResultFailureType.Unauthorized => Results.Unauthorized(),
            ResultFailureType.Forbidden => Results.Forbid(),
            ResultFailureType.ContentBlocked => Results.Forbid(),
            ResultFailureType.PermissionRequired => Results.Forbid(),
            ResultFailureType.GovernanceBlocked => Results.Forbid(),
            _ => Results.Problem(result.Errors.FirstOrDefault()),
        };
}
