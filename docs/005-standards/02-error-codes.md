# Error Code Registry

Machine-readable error codes in API ProblemDetails `code` field.
Format: `{CATEGORY}_{NOUN}_{STATE}`. Clients switch on these — never parse the message.

Source of truth: `AgenticWorkforce.Domain.Exceptions.ErrorCodes`

## Response Shape (RFC 9457)

```json
{
  "status": 404,
  "title": "Project 'abc-123' was not found.",
  "code": "RES_NOT_FOUND",
  "traceId": "0HMJL..."
}
```

## Code Registry

### Authentication (AUTH_*)

| Code | HTTP | When |
|------|------|------|
| `AUTH_UNAUTHORIZED` | 401 | No valid token or API key |
| `AUTH_TOKEN_EXPIRED` | 401 | JWT `exp` claim passed |
| `AUTH_TOKEN_INVALID` | 401 | Bad signature, wrong audience |
| `AUTH_SCOPE_INSUFFICIENT` | 401 | Missing required `scp` claim |
| `AUTH_API_KEY_INVALID` | 401 | API key hash doesn't match |
| `AUTH_API_KEY_EXPIRED` | 401 | API key past `ExpiresAt` |

### Authorisation (AUTHZ_*)

| Code | HTTP | When |
|------|------|------|
| `AUTHZ_FORBIDDEN` | 403 | Role insufficient for this action |
| `AUTHZ_RESOURCE_DENIED` | 403 | BOLA — not a member of this project |
| `AUTHZ_PROJECT_DENIED` | 403 | No project-level role |
| `AUTHZ_INSUFFICIENT_ROLE` | 403 | Project role too low (e.g., Viewer trying to approve) |

### Validation (VAL_*)

| Code | HTTP | When |
|------|------|------|
| `VAL_VALIDATION_ERROR` | 422 | General validation failure |
| `VAL_MISSING_FIELD` | 422 | Required field absent |
| `VAL_INVALID_FORMAT` | 422 | Wrong format (e.g., not a valid email) |
| `VAL_INVALID_VALUE` | 422 | Out of allowed range |

### Resource (RES_*)

| Code | HTTP | When |
|------|------|------|
| `RES_NOT_FOUND` | 404 | Entity missing |
| `RES_ALREADY_EXISTS` | 409 | Duplicate (e.g., same email) |
| `RES_CONFLICT` | 409 | State conflict (optimistic concurrency) |
| `RES_GONE` | 410 | Soft-deleted entity |

### Rate Limiting (RATE_*)

| Code | HTTP | When |
|------|------|------|
| `RATE_LIMITED` | 429 | Too many requests |
| `RATE_QUOTA_EXCEEDED` | 429 | Usage quota hit |

### Business Logic (BIZ_*)

| Code | HTTP | When |
|------|------|------|
| `BIZ_OPERATION_NOT_ALLOWED` | 400 | Domain rule violation |
| `BIZ_INVALID_STATE` | 400 | Invalid state transition |
| `BIZ_PRECONDITION_FAILED` | 400 | Precondition not met |

### Agent (AGENT_*)

| Code | HTTP | When |
|------|------|------|
| `AGENT_BUDGET_EXCEEDED` | 402 | Cost budget exhausted |
| `AGENT_EXECUTION_FAILED` | 500 | Agent threw unrecoverable error |
| `AGENT_NOT_FOUND` | 404 | Agent name not in catalog |
| `AGENT_TOOL_DENIED` | 403 | Tool not in agent's manifest |

### Workflow (WF_*)

| Code | HTTP | When |
|------|------|------|
| `WF_EXECUTION_FAILED` | 500 | Workflow execution error |
| `WF_NODE_FAILED` | 500 | Individual node failure |
| `WF_APPROVAL_REQUIRED` | 202 | Waiting for human approval |

### Audit (AUDIT_*)

| Code | HTTP | When |
|------|------|------|
| `AUDIT_BACKPRESSURE` | 503 | Audit channel full, agent halted |
| `AUDIT_LOCK_TIMEOUT` | 503 | Hash chain lock acquisition failed |
| `AUDIT_CHAIN_CORRUPTED` | 500 | Sequence gap or hash mismatch detected |

### System (SYS_*)

| Code | HTTP | When |
|------|------|------|
| `SYS_INTERNAL_ERROR` | 500 | Unexpected exception |
| `SYS_DATABASE_ERROR` | 500 | PostgreSQL failure |
| `SYS_SERVICE_UNAVAILABLE` | 503 | Dependency down |
| `SYS_EXTERNAL_SERVICE_ERROR` | 502 | Third-party API failure |

## Adding New Codes

1. Add constant to `AgenticWorkforce.Domain.Exceptions.ErrorCodes`
2. Add exception class to `AppException.cs` if needed
3. Add row to this document
4. Update `GlobalExceptionHandler` if the mapping isn't covered by `AppException`
