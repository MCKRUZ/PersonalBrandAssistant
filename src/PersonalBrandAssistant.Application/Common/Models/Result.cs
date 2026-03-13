using PersonalBrandAssistant.Application.Common.Errors;

namespace PersonalBrandAssistant.Application.Common.Models;

public class Result<T>
{
    private Result(T? value, bool isSuccess, ErrorCode errorCode, IReadOnlyList<string> errors)
    {
        Value = value;
        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        Errors = errors;
    }

    public T? Value { get; }
    public bool IsSuccess { get; }
    public ErrorCode ErrorCode { get; }
    public IReadOnlyList<string> Errors { get; }

    public static Result<T> Success(T value) =>
        new(value, true, ErrorCode.None, []);

    public static Result<T> Failure(ErrorCode errorCode, params string[] errors) =>
        new(default, false, errorCode, errors);

    public static Result<T> NotFound(string message) =>
        Failure(ErrorCode.NotFound, message);

    public static Result<T> ValidationFailure(IEnumerable<string> errors) =>
        new(default, false, ErrorCode.ValidationFailed, errors.ToList().AsReadOnly());

    public static Result<T> Conflict(string message) =>
        Failure(ErrorCode.Conflict, message);
}

public static class Result
{
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(ErrorCode errorCode, params string[] errors) =>
        Result<T>.Failure(errorCode, errors);

    public static Result<T> NotFound<T>(string message) => Result<T>.NotFound(message);

    public static Result<T> ValidationFailure<T>(IEnumerable<string> errors) =>
        Result<T>.ValidationFailure(errors);

    public static Result<T> Conflict<T>(string message) => Result<T>.Conflict(message);
}
