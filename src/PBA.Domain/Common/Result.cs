namespace PBA.Domain.Common;

// Intentionally a class, not a record: Result uses inheritance (Result<T> : Result) and
// record equality semantics with polymorphic hierarchies cause subtle bugs. Value equality
// is not meaningful for a result envelope — reference identity is sufficient.
public class Result
{
    public bool IsSuccess { get; }
    public IReadOnlyList<string> Errors { get; }
    public ResultFailureType FailureType { get; }

    protected Result(bool isSuccess, IReadOnlyList<string>? errors = null, ResultFailureType failureType = ResultFailureType.None)
    {
        IsSuccess = isSuccess;
        Errors = errors ?? [];
        FailureType = isSuccess ? ResultFailureType.None : failureType;
    }

    public static Result Success() => new(true);
    public static Result Fail(params string[] errors) => new(false, errors, ResultFailureType.General);
    public static Result ValidationFailure(IReadOnlyList<string> errors) => new(false, errors, ResultFailureType.Validation);
    public static Result Unauthorized(string reason) => new(false, [reason], ResultFailureType.Unauthorized);
    public static Result Forbidden(string reason) => new(false, [reason], ResultFailureType.Forbidden);
    public static Result ContentBlocked(string reason) => new(false, [reason], ResultFailureType.ContentBlocked);
    public static Result NotFound(string reason) => new(false, [reason], ResultFailureType.NotFound);
    public static Result PermissionRequired(string reason) => new(false, [reason], ResultFailureType.PermissionRequired);
    public static Result GovernanceBlocked(string reason) => new(false, [reason], ResultFailureType.GovernanceBlocked);
    public static Result Conflict(string reason) => new(false, [reason], ResultFailureType.Conflict);
}

public sealed class Result<T> : Result
{
    public T? Value { get; }

    private Result(bool isSuccess, T? value = default, IReadOnlyList<string>? errors = null, ResultFailureType failureType = ResultFailureType.None)
        : base(isSuccess, errors, failureType)
    {
        Value = value;
    }

    public static Result<T> Success(T value) => new(true, value);
    public new static Result<T> Fail(params string[] errors) => new(false, errors: errors, failureType: ResultFailureType.General);
    public new static Result<T> ValidationFailure(IReadOnlyList<string> errors) => new(false, errors: errors, failureType: ResultFailureType.Validation);
    public new static Result<T> Unauthorized(string reason) => new(false, errors: [reason], failureType: ResultFailureType.Unauthorized);
    public new static Result<T> Forbidden(string reason) => new(false, errors: [reason], failureType: ResultFailureType.Forbidden);
    public new static Result<T> ContentBlocked(string reason) => new(false, errors: [reason], failureType: ResultFailureType.ContentBlocked);
    public new static Result<T> NotFound(string reason) => new(false, errors: [reason], failureType: ResultFailureType.NotFound);
    public new static Result<T> PermissionRequired(string reason) => new(false, errors: [reason], failureType: ResultFailureType.PermissionRequired);
    public new static Result<T> GovernanceBlocked(string reason) => new(false, errors: [reason], failureType: ResultFailureType.GovernanceBlocked);
    public new static Result<T> Conflict(string reason) => new(false, errors: [reason], failureType: ResultFailureType.Conflict);

    public static implicit operator Result<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Success(value);
    }
}

public enum ResultFailureType
{
    None = 0,
    General,
    Validation,
    Unauthorized,
    Forbidden,
    ContentBlocked,
    NotFound,
    PermissionRequired,
    GovernanceBlocked,
    Conflict
}
