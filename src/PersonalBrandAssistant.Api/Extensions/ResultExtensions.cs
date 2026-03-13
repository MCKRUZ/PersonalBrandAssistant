using Microsoft.AspNetCore.Mvc;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Api.Extensions;

public static class ResultExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
            return Results.Ok(result.Value);

        return result.ErrorCode switch
        {
            ErrorCode.ValidationFailed => Results.Problem(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                Title = "Validation Failed",
                Status = StatusCodes.Status400BadRequest,
                Detail = string.Join("; ", result.Errors),
                Extensions = { ["errors"] = result.Errors },
            }),
            ErrorCode.NotFound => Results.Problem(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                Title = "Not Found",
                Status = StatusCodes.Status404NotFound,
                Detail = result.Errors.FirstOrDefault() ?? "Resource not found.",
            }),
            ErrorCode.Conflict => Results.Problem(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                Title = "Conflict",
                Status = StatusCodes.Status409Conflict,
                Detail = result.Errors.FirstOrDefault() ?? "A conflict occurred.",
            }),
            ErrorCode.Unauthorized => Results.Problem(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
                Title = "Unauthorized",
                Status = StatusCodes.Status401Unauthorized,
                Detail = result.Errors.FirstOrDefault() ?? "Unauthorized.",
            }),
            _ => Results.Problem(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
                Title = "Internal Server Error",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "An unexpected error occurred.",
            }),
        };
    }

    public static IResult ToCreatedHttpResult<T>(this Result<T> result, string routePrefix)
    {
        if (!result.IsSuccess)
            return result.ToHttpResult();

        return Results.Created($"{routePrefix}/{result.Value}", result.Value);
    }
}
