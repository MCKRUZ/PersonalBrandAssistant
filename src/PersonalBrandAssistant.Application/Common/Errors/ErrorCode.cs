namespace PersonalBrandAssistant.Application.Common.Errors;

public enum ErrorCode
{
    None,
    ValidationFailed,
    NotFound,
    Conflict,
    Unauthorized,
    InternalError,
    RateLimited,
    ExternalServiceError,
}
