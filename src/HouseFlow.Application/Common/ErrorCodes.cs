namespace HouseFlow.Application.Common;

/// <summary>
/// Machine-readable error codes returned in the <c>code</c> extension of the API's
/// ProblemDetails responses, so the frontend can branch without parsing English messages.
/// Mirrored in specs/openapi.yaml (<c>ErrorResponse.code</c>) and <c>ApiErrorCodes</c> (src/HouseFlow.Web/Api/Dtos.cs).
/// </summary>
public static class ErrorCodes
{
    public const string InvalidCredentials = "invalid_credentials";
    public const string AccountRestricted = "account_restricted";
    public const string InvalidRefreshToken = "invalid_refresh_token";
    public const string EmailTaken = "email_taken";
    public const string ExportRateLimited = "export_rate_limited";
    public const string InvitationInvalid = "invitation_invalid";
    public const string InvitationEmailMismatch = "invitation_email_mismatch";
    public const string InvitationAlreadyPending = "invitation_already_pending";
    public const string InvitationLimitReached = "invitation_limit_reached";
    public const string AlreadyMember = "already_member";
    public const string OwnInvitation = "own_invitation";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";

    /// <summary>401 without a more specific code: missing/expired access token, or an account that no longer exists.</summary>
    public const string Unauthorized = "unauthorized";

    /// <summary>400 without a more specific code: model validation, or a business rule on the input.</summary>
    public const string ValidationFailed = "validation_failed";

    /// <summary>400 on account deletion: the password confirmation is wrong.</summary>
    public const string WrongPassword = "wrong_password";

    /// <summary>429 from the request rate limiter (header Retry-After).</summary>
    public const string RateLimited = "rate_limited";
}

/// <summary>An exception carrying one of <see cref="ErrorCodes"/>.</summary>
public interface ICodedException
{
    string ErrorCode { get; }
}

/// <summary>Authentication refused (HTTP 401) with a machine code (bad credentials, restricted account…).</summary>
public class AuthenticationFailedException : UnauthorizedAccessException, ICodedException
{
    public string ErrorCode { get; }

    public AuthenticationFailedException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// The caller's own account (the JWT's <c>sub</c>) no longer exists — deleted while its access
    /// token, stateless, is still valid for up to 15 minutes. That is an authentication failure
    /// (401), not a missing resource (404): the client must drop the session.
    /// </summary>
    public static AuthenticationFailedException AccountNotFound() =>
        new(ErrorCodes.Unauthorized, "The account no longer exists");
}

/// <summary>Business rule violated by the request (HTTP 400) with a machine code.</summary>
public class BusinessRuleException : InvalidOperationException, ICodedException
{
    public string ErrorCode { get; }

    public BusinessRuleException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>Conflict with the current state (HTTP 409) with a machine code (email taken, already member…).</summary>
public class ConflictException : InvalidOperationException, ICodedException
{
    public string ErrorCode { get; }

    public ConflictException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}
