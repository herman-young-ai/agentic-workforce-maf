# API Design Standards

## URL Structure

```
/api/v{version}/{resource}[/{id}][/{sub-resource}]
```

- Lowercase kebab-case: `/api/v1/workflow-definitions`
- Plural resource names: `/projects`, `/tasks`, `/sessions`
- No trailing slashes

## HTTP Methods

| Method | Use | Idempotent | Body |
|--------|-----|------------|------|
| GET | Read | Yes | No |
| POST | Create, trigger action | No | Yes |
| PATCH | Partial update | No | Yes |
| DELETE | Remove (soft delete) | Yes | No |
| PUT | Full replace (rare — prefer PATCH) | Yes | Yes |

## Response Shapes

### Single Resource

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Security Audit Q3",
  "status": "Active",
  "createdAt": "2026-05-15T10:30:00Z"
}
```

### Paginated Collection

```json
{
  "items": [...],
  "page": 1,
  "pageSize": 20,
  "totalCount": 142,
  "totalPages": 8,
  "hasNextPage": true,
  "hasPreviousPage": false
}
```

### Error (RFC 9457 ProblemDetails)

```json
{
  "status": 404,
  "title": "Project '3fa85f64' was not found.",
  "code": "RES_NOT_FOUND",
  "traceId": "0HMJL..."
}
```

## Status Codes

| Code | Meaning | When |
|------|---------|------|
| 200 | OK | Successful read or update |
| 201 | Created | Successful creation (with `Location` header) |
| 202 | Accepted | Async operation started (workflow dispatch) |
| 204 | No Content | Successful delete |
| 400 | Bad Request | Business rule violation |
| 401 | Unauthorized | Missing or invalid auth |
| 402 | Payment Required | Budget exhausted |
| 403 | Forbidden | Insufficient role |
| 404 | Not Found | Resource doesn't exist |
| 409 | Conflict | Duplicate or state conflict |
| 422 | Unprocessable Entity | Validation error |
| 429 | Too Many Requests | Rate limited (with `Retry-After` header) |
| 500 | Internal Server Error | Unexpected failure |
| 503 | Service Unavailable | Dependency down (audit, Redis, etc.) |

## JSON Conventions

- **camelCase** field names: `projectId`, `createdAt`
- **ISO 8601 UTC** timestamps: `"2026-05-15T10:30:00Z"`
- **Boolean prefixes**: `isActive`, `hasNextPage`, `requiresApproval`
- **`id`** for primary key, **`{resource}Id`** for foreign keys
- **Enums as strings**: `"status": "Active"` (not `"status": 1`)

## Authentication

- `[Authorize]` on every endpoint by default
- `[AllowAnonymous]` only on health checks, with explicit comment
- Bearer JWT in `Authorization` header
- API key in `X-API-Key` header (for agent/service auth)

## Pagination & Filtering

- Query params: `?page=1&pageSize=20&status=Active&sort=createdAt:desc`
- Filters AND'd together
- Allow-listed sort fields per resource (prevent arbitrary column access)
- Default: `page=1`, `pageSize=20`, max `pageSize=100`

## Idempotency

- `X-Idempotency-Key` header for POST/PATCH operations
- Server stores result keyed by `{userId}:{idempotencyKey}` with 24h TTL
- Replay returns cached response without re-executing

## Rate Limiting

- Global: 600 req/min per user (sliding window by OID claim)
- Strict: 10 req/min per IP (for auth and mutation endpoints)
- Response: 429 with `Retry-After: 60` header

## Versioning

- URL path versioning: `/api/v1/`, `/api/v2/`
- Breaking changes = new version
- Non-breaking additions (new fields, new endpoints) = same version
- Sunset header when deprecating: `Sunset: Sat, 01 Jan 2028 00:00:00 GMT`
