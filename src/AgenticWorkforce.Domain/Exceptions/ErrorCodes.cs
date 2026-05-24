namespace AgenticWorkforce.Domain.Exceptions;

/// <summary>
/// Machine-readable error codes returned in the API problem details "code" field.
/// Format: {CATEGORY}_{NOUN}_{STATE}
/// Clients switch on these — never parse the human-readable message.
/// Adopted from SecurityBff reference, extended for agentic platform.
/// </summary>
public static class ErrorCodes
{
    // -- Authentication (AUTH_*) --
    public const string AuthUnauthorized      = "AUTH_UNAUTHORIZED";
    public const string AuthTokenExpired      = "AUTH_TOKEN_EXPIRED";
    public const string AuthTokenInvalid      = "AUTH_TOKEN_INVALID";
    public const string AuthScopeInsufficient = "AUTH_SCOPE_INSUFFICIENT";
    public const string AuthApiKeyInvalid     = "AUTH_API_KEY_INVALID";
    public const string AuthApiKeyExpired     = "AUTH_API_KEY_EXPIRED";

    // -- Authorisation (AUTHZ_*) --
    public const string AuthzForbidden        = "AUTHZ_FORBIDDEN";
    public const string AuthzResourceDenied   = "AUTHZ_RESOURCE_DENIED";
    public const string AuthzProjectDenied    = "AUTHZ_PROJECT_DENIED";
    public const string AuthzInsufficientRole = "AUTHZ_INSUFFICIENT_ROLE";

    // -- Validation (VAL_*) --
    public const string ValValidationError    = "VAL_VALIDATION_ERROR";
    public const string ValMissingField       = "VAL_MISSING_FIELD";
    public const string ValInvalidFormat      = "VAL_INVALID_FORMAT";
    public const string ValInvalidValue       = "VAL_INVALID_VALUE";

    // -- Resource (RES_*) --
    public const string ResNotFound           = "RES_NOT_FOUND";
    public const string ResAlreadyExists      = "RES_ALREADY_EXISTS";
    public const string ResConflict           = "RES_CONFLICT";
    public const string ResGone               = "RES_GONE";

    // -- Rate limiting (RATE_*) --
    public const string RateLimited           = "RATE_LIMITED";
    public const string RateQuotaExceeded     = "RATE_QUOTA_EXCEEDED";

    // -- Business logic (BIZ_*) --
    public const string BizOperationNotAllowed = "BIZ_OPERATION_NOT_ALLOWED";
    public const string BizInvalidState        = "BIZ_INVALID_STATE";
    public const string BizPreconditionFailed  = "BIZ_PRECONDITION_FAILED";

    // -- Agent (AGENT_*) --
    public const string AgentBudgetExceeded    = "AGENT_BUDGET_EXCEEDED";
    public const string AgentExecutionFailed   = "AGENT_EXECUTION_FAILED";
    public const string AgentNotFound          = "AGENT_NOT_FOUND";
    public const string AgentToolDenied        = "AGENT_TOOL_DENIED";
    public const string SandboxUnavailable     = "AGENT_SANDBOX_UNAVAILABLE";

    // -- Workflow (WF_*) --
    public const string WfExecutionFailed      = "WF_EXECUTION_FAILED";
    public const string WfNodeFailed           = "WF_NODE_FAILED";
    public const string WfApprovalRequired     = "WF_APPROVAL_REQUIRED";

    // -- Audit (AUDIT_*) --
    public const string AuditBackpressure      = "AUDIT_BACKPRESSURE";
    public const string AuditLockTimeout       = "AUDIT_LOCK_TIMEOUT";
    public const string AuditChainCorrupted    = "AUDIT_CHAIN_CORRUPTED";

    // -- System (SYS_*) --
    public const string SysInternalError       = "SYS_INTERNAL_ERROR";
    public const string SysDatabaseError       = "SYS_DATABASE_ERROR";
    public const string SysServiceUnavailable  = "SYS_SERVICE_UNAVAILABLE";
    public const string SysExternalService     = "SYS_EXTERNAL_SERVICE_ERROR";
}
