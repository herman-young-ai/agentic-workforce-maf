#pragma warning disable RCS1194

namespace AgenticWorkforce.Domain.Exceptions;

/// <summary>
/// Base exception for all domain exceptions. Carries a machine-readable error code
/// and HTTP status code. The GlobalExceptionHandler maps these to ProblemDetails.
/// Adopted from SecurityBff reference architecture.
/// </summary>
public class AppException(string code, string message, int statusCode = 400)
    : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

// -- Authentication (401) --

public class UnauthorizedException(string? detail = null)
    : AppException(ErrorCodes.AuthUnauthorized, detail ?? "Authentication is required.", 401);

public class TokenExpiredException()
    : AppException(ErrorCodes.AuthTokenExpired, "The access token has expired.", 401);

public class TokenInvalidException(string? detail = null)
    : AppException(ErrorCodes.AuthTokenInvalid, detail ?? "The access token is invalid.", 401);

// -- Authorisation (403) --

public class ForbiddenException(string? detail = null)
    : AppException(ErrorCodes.AuthzForbidden, detail ?? "You do not have permission to perform this action.", 403);

public class ResourceAccessDeniedException(string resource, object id)
    : AppException(ErrorCodes.AuthzResourceDenied, $"Access to {resource} '{id}' is denied.", 403);

// -- Validation (422) --

public class ValidationException(string detail)
    : AppException(ErrorCodes.ValValidationError, detail, 422);

// -- Resource (404/409) --

public class NotFoundException(string resource, object id)
    : AppException(ErrorCodes.ResNotFound, $"{resource} '{id}' was not found.", 404);

public class ConflictException(string resource, string detail)
    : AppException(ErrorCodes.ResConflict, $"{resource}: {detail}", 409);

public class AlreadyExistsException(string resource, string detail)
    : AppException(ErrorCodes.ResAlreadyExists, $"{resource}: {detail}", 409);

// -- Business logic (400) --

public class BusinessRuleException(string detail)
    : AppException(ErrorCodes.BizOperationNotAllowed, detail, 400);

public class InvalidStateException(string detail)
    : AppException(ErrorCodes.BizInvalidState, detail, 400);

// -- Rate limiting (429) --

public class RateLimitException()
    : AppException(ErrorCodes.RateLimited, "Too many requests. Please retry after 60 seconds.", 429);

// -- Agent & Audit (platform-specific) --

public class BudgetExceededException(string scope, string scopeId, decimal limitUsd)
    : AppException(ErrorCodes.AgentBudgetExceeded,
        $"Budget exhausted for {scope} '{scopeId}' (limit: ${limitUsd:F2}). Agent execution halted.", 402);

public class AuditBackpressureException(string detail)
    : AppException(ErrorCodes.AuditBackpressure, detail, 503);

public class AuditLockTimeoutException(string detail)
    : AppException(ErrorCodes.AuditLockTimeout, detail, 503);

public class AgentExecutionException(string agentName, string detail)
    : AppException(ErrorCodes.AgentExecutionFailed,
        $"Agent '{agentName}' execution failed: {detail}", 500);

/// <summary>
/// Raised by Phase 7 sandbox tool stubs when called. The real ACA Dynamic Sessions
/// integration lands in Phase 11; until then every sandbox tool surfaces this
/// exception so the agent loop sees the unavailability as a tool error rather
/// than a hallucinable "not available" string (Principle 8 — fail loud).
/// </summary>
public class SandboxUnavailableException(string toolName)
    : AppException(ErrorCodes.SandboxUnavailable,
        $"Sandbox tool '{toolName}' is not yet available — ACA Dynamic Sessions wiring lands in Phase 11.", 503);

#pragma warning restore RCS1194
