# R17 Response: Security Architecture — Complete Design

**Date:** 2026-05-11
**Prompt:** [R17-prompt-security-design.md](R17-prompt-security-design.md)
**Depends on:** [ADR-007](../002-architecture/ADR-007-identity-auth.md), [ADR-008](../002-architecture/ADR-008-audit-compliance.md), [ADR-006](../002-architecture/ADR-006-container-isolation.md), [ADR-014](../002-architecture/ADR-014-knowledge-memory.md), [ADR-012](../002-architecture/ADR-012-aspire-integration.md), [API Design](../002-architecture/004-api-design.md)

> **Scope note:** This document defines security controls that are **net-new** beyond existing ADRs. Where an ADR already specifies a mechanism (hash chain, audit pipeline, SSE token exchange, segregation of duties), this document references it and adds only the security-specific delta. See the cross-reference table at end.

---

## AREA 1: API Security

### Threat Table

| # | Threat | Likelihood | Impact |
|---|--------|-----------|--------|
| 1.1 | Brute-force / credential stuffing via API | H | H |
| 1.2 | Oversized payload DoS | M | H |
| 1.3 | Injection via path/query parameters | M | H |
| 1.4 | Stack trace / internal ID leakage | M | M |
| 1.5 | CORS misconfiguration allows cross-origin attack | L | H |
| 1.6 | Missing security headers enables clickjacking/XSS | M | H |
| 1.7 | Replay attacks on mutating endpoints | M | M |
| 1.8 | No perimeter WAF — DDoS at application layer | M | H |

### Controls

#### 1. Rate Limiting — `System.Threading.RateLimiting`

```csharp
builder.Services.AddRateLimiter(o =>
{
    string PartitionKey(HttpContext ctx) =>
        ctx.User?.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon";

    // Read endpoints (GET)
    o.AddPolicy("read", ctx =>
        RateLimitPartition.GetTokenBucketLimiter(PartitionKey(ctx),
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 200, ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                TokensPerPeriod = 200, QueueLimit = 0
            }));
    // Write endpoints (POST/PUT/PATCH/DELETE)
    o.AddPolicy("write", ctx =>
        RateLimitPartition.GetTokenBucketLimiter(PartitionKey(ctx),
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 50, ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                TokensPerPeriod = 50, QueueLimit = 0
            }));
    // LLM-triggering endpoints (execute, chat, workflow run)
    o.AddPolicy("llm-trigger", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(ctx),
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1), PermitLimit = 10
            }));
    // Auth endpoints (SSE token mint, API key creation)
    o.AddPolicy("auth", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(ctx),
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1), PermitLimit = 5
            }));

    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = 429;
        ctx.HttpContext.Response.Headers.RetryAfter = "60";
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = 429, Title = "Rate limit exceeded" }, ct);
    };
});
```

| Endpoint Group | Limit | Window | Notes |
|---------------|-------|--------|-------|
| Read (GET) | 200 req | 1 min | Supersedes SRS `requests_per_minute: 60` (prototype value) |
| Write (POST/PUT/DELETE) | 50 req | 1 min | |
| LLM-triggering (execute, chat) | 10 req | 1 min | |
| Auth (token exchange) | 5 req | 1 min | |
| SSE token mint | 10 req | 1 min | |

> **Design note:** These limits supersede the flat `60 req/min` from the Python prototype's `security.yaml`. Tiered limits are appropriate for the production platform where read, write, and LLM-triggering endpoints have fundamentally different cost profiles.

#### 2. Input Validation

```csharp
builder.Services.Configure<KestrelServerOptions>(o =>
{
    o.Limits.MaxRequestBodySize = 10 * 1024 * 1024;    // 10 MB
    o.Limits.MaxRequestHeadersTotalSize = 32 * 1024;    // 32 KB
    o.Limits.MaxRequestLineSize = 8 * 1024;             // 8 KB URL
    o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
});

// File upload endpoint override
app.MapPost("/api/v1/projects/{projectId:guid}/documents", ...)
   .DisableRequestSizeLimit()  // handled by IFormFile validation
   .RequireAuthorization("ProjectOperator");
```

- All request DTOs use `[Required]`, `[MaxLength]`, `[Range]` attributes
- Path params: GUID format validated via route constraints `{projectId:guid}`
- Query params: `[Range(1, 100)]` for `limit`, `[Range(0, int.MaxValue)]` for `offset` (per [API Design §5](../002-architecture/004-api-design.md))
- File uploads: max 50 MB per file, allowlisted MIME types only (PDF, DOCX, XLSX, CSV, TXT, MD, JSON)

#### 3. Output Sanitisation

```csharp
public sealed class SanitisedProblemDetailsFactory : ProblemDetailsFactory
{
    public override ProblemDetails CreateProblemDetails(HttpContext ctx,
        int? statusCode, string? title, string? type, string? detail, string? instance)
    {
        return new ProblemDetails
        {
            Status = statusCode ?? 500,
            Title = title ?? "An error occurred",
            Type = type,
            // NEVER include: stack traces, internal exception messages, connection strings
            // Correlation ID only — user can reference for support
            Extensions = { ["traceId"] = ctx.TraceIdentifier }
        };
    }
}
```

- Stack traces: **never** in production (`ASPNETCORE_ENVIRONMENT=Production`)
- Internal DB PKs (UUIDv7): exposed directly — no human-readable prefix scheme. UUIDs are opaque and non-sequential enough for external use.
- PII in error messages: stripped — validation errors reference field names, not values
- Error codes: see [API Design §4](../002-architecture/004-api-design.md) for the canonical error code table

#### 4. CORS

```csharp
builder.Services.AddCors(o =>
{
    o.AddPolicy("Production", p => p
        .WithOrigins("https://workforce.investec.com")
        .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
        .WithHeaders("Authorization", "Content-Type", "X-Api-Key", "X-Idempotency-Key", "X-Request-Id")
        .AllowCredentials()
        .SetPreflightMaxAge(TimeSpan.FromHours(1)));

    o.AddPolicy("Development", p => p
        .WithOrigins("https://localhost:5173", "https://localhost:3000")
        .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
        .WithHeaders("Authorization", "Content-Type", "X-Api-Key", "X-Idempotency-Key", "X-Request-Id")
        .AllowCredentials());
});
```

#### 5. Security Headers Middleware

```csharp
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h.StrictTransportSecurity = "max-age=63072000; includeSubDomains; preload";
    h.XContentTypeOptions = "nosniff";
    h.XFrameOptions = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
    h["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self' wss://*.investec.com; frame-ancestors 'none'";
    h["X-Permitted-Cross-Domain-Policies"] = "none";
    h["Cross-Origin-Embedder-Policy"] = "require-corp";
    h["Cross-Origin-Opener-Policy"] = "same-origin";
    h["Cross-Origin-Resource-Policy"] = "same-origin";
    await next();
});
```

#### 6. Request Logging with Redaction

```csharp
builder.Services.AddHttpLogging(o =>
{
    o.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath
                    | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
    o.RequestHeaders.Add("X-Request-Id");
    // Redact sensitive headers
    o.RequestHeaders.Remove("Authorization");
    o.RequestHeaders.Remove("X-Api-Key");
    o.RequestHeaders.Remove("Cookie");
});
```

- Correlation ID: `X-Request-Id` header (client-provided or server-generated UUID)
- Propagated via `Activity.Current.TraceId` through OpenTelemetry
- PII in request/response bodies: **never logged** — only metadata (method, path, status, duration, user ID)
- SSE `/stream` paths: query strings MUST be redacted in access logs (contain single-use tokens)

#### 7. Idempotency

```csharp
public sealed class IdempotencyMiddleware(RequestDelegate next, IConnectionMultiplexer redis, ILogger<IdempotencyMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.Request.Method is not ("POST" or "PUT" or "PATCH")) { await next(ctx); return; }
        if (!ctx.Request.Headers.TryGetValue("X-Idempotency-Key", out var key)) { await next(ctx); return; }

        IDatabase? db;
        try
        {
            db = redis.GetDatabase();
        }
        catch (Exception ex)
        {
            // Fail-open for GET-like operations; fail-CLOSED for mutating writes
            // Financial operations (create project, dispatch tasks) must not double-execute
            var isMutating = ctx.Request.Method is "POST" or "PUT";
            if (isMutating && ctx.GetEndpoint()?.Metadata.GetMetadata<IdempotencyRequiredAttribute>() is not null)
            {
                log.LogError(ex, "Redis unavailable for idempotency check on mutating endpoint — failing CLOSED");
                ctx.Response.StatusCode = 503;
                await ctx.Response.WriteAsJsonAsync(
                    new ProblemDetails { Status = 503, Title = "Service temporarily unavailable" });
                return;
            }
            // Non-critical writes (PATCH updates) and reads: fail-open per SRS SR-030
            log.LogWarning(ex, "Redis unavailable for idempotency check — failing open");
            await next(ctx);
            return;
        }

        var cacheKey = $"idempotency:{ctx.User.FindFirst("sub")?.Value}:{key}";

        var cached = await db.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            var prior = JsonSerializer.Deserialize<IdempotentResponse>(cached!);
            ctx.Response.StatusCode = prior!.StatusCode;
            ctx.Response.Headers["X-Idempotent-Replayed"] = "true";
            await ctx.Response.WriteAsync(prior.Body);
            return;
        }
        // Capture response, cache for 24h
        await next(ctx);
        // ... capture and SET with 24h TTL, NX flag
    }
}
```

- TTL: 24 hours
- Key format: `idempotency:{userId}:{clientKey}` — scoped to user
- `NX` flag on SET prevents race conditions
- **Tiered fail behaviour on Redis outage**: mutating endpoints marked with `[IdempotencyRequired]` (create project, dispatch tasks, workflow run) **fail closed** (503) to prevent double-execution. Non-critical writes and reads **fail open** per SRS SR-030.
- Replayed responses include `X-Idempotent-Replayed: true` header

#### 8. API Versioning

Versioning strategy defined in [API Design §6](../002-architecture/004-api-design.md). Security-relevant additions:

- `Sunset` header on deprecated versions: `Sunset: Sat, 01 Nov 2026 00:00:00 GMT`
- Minimum 90-day deprecation window (per [API Design §6](../002-architecture/004-api-design.md))
- API version changes require change management approval (see Area 9)

### Gaps

| Gap | Mitigation |
|-----|-----------|
| No WAF in front of ACA | Deploy Azure Front Door with WAF policies. **TRD mandates APIM** — see Area 6 for full network architecture. |
| No APIM gateway | TRD requires Azure API Management as Group-standard gateway. APIM provides: OAuth validation at edge, subscription key management, threat protection policies, rate limiting at perimeter. **Must be added to network architecture.** |

---

## AREA 2: Authentication Deep Dive

### Threat Table

| # | Threat | Likelihood | Impact |
|---|--------|-----------|--------|
| 2.1 | JWT forged with weak/wrong algorithm | L | H |
| 2.2 | Stolen JWT used from different location | M | H |
| 2.3 | API key leaked in logs/repos | M | H |
| 2.4 | SSE token race condition / replay | L | M |
| 2.5 | Service impersonation (Worker spoofed) | L | H |
| 2.6 | Stale access after role revocation | M | H |

### Controls

#### 1. JWT Validation — Hardened (delta over ADR-007)

ADR-007 establishes the dual-scheme `JwtOrApiKey` pattern. This section adds hardening parameters not in the ADR:

```csharp
// Additions to ADR-007's TokenValidationParameters
jwtOpts.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);           // tight (default is 5 min)
jwtOpts.TokenValidationParameters.ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 }; // pin RS256
```

- **New validations beyond ADR-007**: `tid` (tenant ID must match), algorithm pinning (RS256 only — blocks `alg: none` and HMAC confusion), tight clock skew (30s vs default 300s)
- Key rotation: `Microsoft.Identity.Web` auto-fetches from OIDC metadata endpoint; 24h JWKS cache with background refresh — no manual key management needed

#### 2. API Key Lifecycle

> **Decision change (supersedes ADR-007 §API Key Security):** Upgrade from HMAC-SHA256 to **Argon2id** for key hashing. ADR-007 has been updated to reflect this.

| Phase | Detail |
|-------|--------|
| **Format** | `awp_live_` + 32 random bytes (base62) = `awp_live_a1b2c3...` (prefix identifies environment) |
| **Storage** | Argon2id hash. Per-key salt embedded in hash output (Argon2id standard format). time=3, memory=19MB, parallelism=1 (OWASP minimum). No separate HMAC key in Key Vault. |
| **Verification** | **Prefix lookup first**: extract the 8-char public prefix from the submitted key, look up in `api_keys` table (indexed). If no matching prefix → reject immediately (no hash computation). Only if prefix matches → compute Argon2id and verify against stored hash. This prevents resource exhaustion from invalid key submissions. |
| **Creation** | Owner/Admin creates via API. Key shown once. Scopes + expiry set at creation. |
| **Rotation** | New key created with overlapping validity (7-day grace). Old key marked `rotating`, alert fires. |
| **Revocation** | Immediate. Cached hashes evicted via Redis pub/sub. |
| **Expiry** | Max 1 year. Alert at 30d, 7d, 1d before expiry. |
| **Monitoring** | `last_used_at` and `last_used_ip` updated on each use. Keys unused >90 days flagged for review. |

**Why Argon2id over HMAC-SHA256:** HMAC-SHA256 is fast (~1B hashes/sec on GPU) — suitable for MACs but too fast for password-like secrets. Argon2id provides memory-hard resistance to GPU/ASIC brute-force.

**DoS mitigation:** Argon2id allocates 19MB per verification. Without the prefix lookup gate, an attacker could exhaust server memory by submitting requests with fabricated keys. The prefix lookup (O(1) indexed query) rejects unknown keys before any hashing occurs — only valid prefixes trigger the expensive Argon2id path.

#### 3. SSE Token — Enhanced (delta over ADR-007)

ADR-007 establishes the GETDEL pattern with 30s TTL. Additions:

- **Bind to resource**: token payload includes `{userId}:{projectId}:{nonce}` — server validates project access on connection
- **Rate limit**: max 10 SSE token mints per user per minute (see Area 1 rate limits)

#### 4. Session Management

- JWT lifetime: **1 hour** (Entra ID default for access tokens)
- Refresh: MSAL handles silently via refresh token (14-day sliding window)
- Forced logout: revoke refresh tokens via Microsoft Graph API `revokeSignInSessions`
- Concurrent login sessions: **allowed** (users may use SPA + CLI simultaneously) — no limit
- API key auth: stateless — revocation is key deletion

> **Terminology note:** "Session" here means user login session (OAuth). Not to be confused with MAF agent sessions (transport — see Principles) or Dynamic Sessions (sandbox containers — see ADR-006).

#### 5. Service-to-Service Auth (BFF ↔ Worker)

```csharp
// Worker authenticates to BFF using Managed Identity
// Worker requests a token scoped to the BFF's app registration
var token = await credential.GetTokenAsync(
    new TokenRequestContext(new[] { $"api://{bffClientId}/.default" }));
httpClient.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", token.Token);
```

- Worker has its own Managed Identity (separate from BFF per ADR-012)
- BFF validates the token's `oid` / `azp` claim matches the Worker's MI client ID
- No shared secrets — pure Entra ID token exchange
- **Note:** Primary BFF↔Worker communication is via Redis Streams (XADD/XREADGROUP per ADR-012) and shared PostgreSQL — direct HTTP is only for specific admin operations

#### 6. Impersonation

- **No impersonation.** Platform Admins access projects via their admin override (logged), not by assuming another user's identity.
- Admin override is detected by the `ProjectRoleAuthorizationHandler` (logs the access) and the audit middleware (records the action).
- **Reason capture**: admin overrides on mutating endpoints (POST/PUT/PATCH/DELETE) require an `X-Admin-Reason` header. The audit middleware rejects mutating requests from `platform_admin` users accessing non-owned projects without this header (403 + `ADMIN_REASON_REQUIRED` error code). Read-only access (GET) is logged without a reason.
- If future need arises: implement `X-On-Behalf-Of` header with OBO flow, double-logged

#### 7. Token Theft Mitigation

| Signal | Detection | Response |
|--------|-----------|----------|
| JWT used from new IP/geo | Log Analytics alert on `SignInLogs` | Conditional Access: block + notify |
| API key used from unexpected IP | `last_used_ip` vs registered CIDRs | Auto-revoke + alert |
| Impossible travel | Two uses >500km apart within 30min | Revoke + incident |
| Bulk data access | >100 requests/min from single key | Rate limit + alert |

- **Damage limitation**: API keys scoped to specific projects + operations. Stolen key can't escalate to platform admin.
- **Short JWT lifetime** (1h) limits exposure window
- **Conditional Access Policies** in Entra ID: require compliant device, block legacy auth

---

## AREA 3: Authorisation Deep Dive

### Threat Table

| # | Threat | Likelihood | Impact |
|---|--------|-----------|--------|
| 3.1 | Viewer accesses restricted data (cost, PCD internals) | M | M |
| 3.2 | Cross-project data leakage via shared learnings | M | H |
| 3.3 | Privilege escalation via workflow manipulation | M | H |
| 3.4 | Stale project role after removal | M | M |
| 3.5 | Operator skips approval gate | L | H |

### Controls

#### 1. ProjectRoleAuthorizationHandler — Implementation

ADR-007 declares policies and registration. This is the canonical implementation:

```csharp
public sealed class ProjectRoleAuthorizationHandler(
    IProjectMemberRepository repo,
    IMemoryCache cache,
    ILogger<ProjectRoleAuthorizationHandler> log)
    : AuthorizationHandler<ProjectRoleRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ProjectRoleRequirement requirement)
    {
        var userId = context.User.FindFirst("sub")?.Value;
        var httpCtx = (context.Resource as HttpContext)!;
        var projectId = httpCtx.GetRouteValue("projectId")?.ToString();
        if (userId is null || projectId is null) return;

        // Platform admin override — succeeds but audit-logged
        // Reason capture happens at the API endpoint level (audit middleware),
        // not here — the authorization handler doesn't have access to the request body.
        if (context.User.IsInRole("platform_admin"))
        {
            log.LogWarning("Admin override: {UserId} accessing {ProjectId} as {Role}",
                userId, projectId, requirement.MinimumRole);
            context.Succeed(requirement);
            return;
        }

        // Cache: 5 min per user+project, evicted on role change via Redis pub/sub
        // Cache expiry clamped to role.ExpiresAt so stale grants don't survive in cache
        var cacheKey = $"proj-role:{userId}:{projectId}";
        var role = await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            var r = await repo.GetRoleAsync(userId, Guid.Parse(projectId));
            var maxCache = TimeSpan.FromMinutes(5);
            if (r?.ExpiresAt is { } exp)
            {
                var remaining = exp - DateTimeOffset.UtcNow;
                entry.AbsoluteExpirationRelativeToNow = remaining > TimeSpan.Zero
                    ? (remaining < maxCache ? remaining : maxCache)
                    : TimeSpan.Zero; // already expired — cache will evict immediately
            }
            else
            {
                entry.AbsoluteExpirationRelativeToNow = maxCache;
            }
            return r;
        });

        if (role is null) { LogDeny(userId, projectId, requirement); return; }

        // Belt-and-suspenders: check temporal expiry even if cache TTL should have handled it
        if (role.ExpiresAt.HasValue && role.ExpiresAt < DateTimeOffset.UtcNow)
        {
            cache.Remove(cacheKey); // force eviction
            LogDeny(userId, projectId, requirement);
            return;
        }

        if (RoleHierarchy.Satisfies(role.Name, requirement.MinimumRole))
            context.Succeed(requirement);
        else
            LogDeny(userId, projectId, requirement);
    }
}
```

- **Role hierarchy**: `owner > operator > reviewer > viewer` — evaluated at authorisation time via `RoleHierarchy.Satisfies()` (not via claims inflation)
- **Cache**: 5-minute `IMemoryCache` per user+project. Evicted on role change via Redis pub/sub notification.
- **Scale**: O(1) DB lookup on cache miss (indexed on `(project_id, user_id)`). At 1000 projects x 10 users = 10K cache entries — trivial.

> **Supersedes R07's `MissionMembershipHandler`** — that handler checked binary membership with the old `mission` terminology. Use this handler only.

#### 2. Field-Level Authorisation

| Field/Section | Owner | Operator | Reviewer | Viewer |
|--------------|-------|----------|----------|--------|
| Cost data (token counts, spend) | Yes | Yes | No | No |
| API keys (other users') | No | No | No | No |
| API keys (own) | Yes | Yes | N/A | N/A |
| PCD — principles/guardrails | Read/Write | Read | Read | No |
| PCD — context/objectives | Read/Write | Read/Write | Read | Read |
| Audit records (project) | Yes | Yes | Yes | Yes |
| Agent prompts (system) | Yes | No | No | No |

Implemented via response DTO projections — not by filtering at the DB level:

```csharp
public ProjectDto ToDto(string role) => new(
    Id, Name, Status,
    Cost: role is "owner" or "operator" ? Cost : null,
    Pcd: role is "viewer" ? PcdSummaryOnly() : FullPcd()
);
```

#### 3. Temporal Authorisation

```sql
ALTER TABLE project_members ADD COLUMN expires_at TIMESTAMPTZ NULL;

-- Reviewer access expires after 30 days
INSERT INTO project_members (project_id, user_id, role, expires_at)
VALUES ('prj-001', 'auditor-1', 'reviewer', NOW() + INTERVAL '30 days');
```

- `ProjectRoleAuthorizationHandler` checks `expires_at` — treat expired as no-role
- Background job: daily scan for expired memberships → log + notify owner

#### 4. Delegation

- Owner can grant temporary `owner` role to another user with `expires_at` set
- Original owner retains their role — two concurrent owners allowed during delegation
- Delegation is audit-logged with `delegated_by`, `reason`, `expires_at`
- Auto-reverts on expiry — no manual step needed

#### 5. Authorisation Audit

- **Every deny**: logged at `Warning` level with `userId`, `projectId`, `requiredRole`, `actualRole`
- **Every admin override**: logged at `Warning` with mandatory `reason` field
- Deny patterns aggregated in Eventhouse — alert on >10 denies/hour from same user (possible enumeration)

#### 6. Cross-Project Data Leakage Prevention

| Vector | Control |
|--------|---------|
| Shared learnings | Learnings tagged with `project_id`. Cross-project promotion requires `platform_admin` approval. Promoted learnings stripped of project-specific context. (See ADR-014 §Platform Knowledge Promotion) |
| Agent catalog | Agents are templates — instantiated per project with isolated context. No shared state between instances. |
| Model context | Each agent execution gets a fresh context window. No conversation history shared across projects. |
| Vector store | pgvector embeddings partitioned by `project_id`. Similarity search includes `WHERE project_id = @pid` filter. |
| Redis cache | All cache keys prefixed with `project:{projectId}:` — no cross-contamination. |

#### 7. Privilege Escalation Prevention

| Path | Control |
|------|---------|
| Operator modifies workflow to skip gate | Workflow schema validated server-side. Approval gates with `required: true` cannot be removed by operators — only owners. Gate removal is audit-logged. |
| Agent modifies own constraints | Agent cannot write to PCD `principles` or `guardrails` paths — enforced by path allowlist in `PcdUpdateTool`. (See ADR-014 §Human Direction vs Agent Discovery) |
| User creates project with elevated privileges | Project creator gets `owner` role — cannot assign themselves `platform_admin`. Platform roles are Entra ID only. |
| API key scope escalation | Scopes set at creation, immutable. New key required for different scopes. |
| Redis queue flooding (cost attack) | KEDA scaling has `maxReplicaCount` cap. Budget enforcement (ADR-009) halts execution before cost spirals. |

---

## AREA 4: Agent Security

### Threat Table

| # | Threat | Likelihood | Impact |
|---|--------|-----------|--------|
| 4.1 | Direct prompt injection via user input | H | H |
| 4.2 | Indirect injection via uploaded document | H | H |
| 4.3 | Learning poisoning (malicious knowledge persisted) | M | H |
| 4.4 | Agent reads files outside project scope | M | H |
| 4.5 | Agent exfiltrates data via search queries | M | H |
| 4.6 | Agent executes destructive shell commands | M | H |
| 4.7 | PCD poisoning — agent alters future behaviour | M | H |

### Controls

#### 1. Prompt Injection Defence — Layered

```
┌─── Defence Layers ───────────────────────────────────┐
│ L1: Input sanitisation (strip control chars, limit)  │
│ L2: System prompt hardening (instruction hierarchy)  │
│ L3: Output validation (structured output parsing)    │
│ L4: Canary token detection (heuristic, defence-in-   │
│     depth only — trivially bypassed in isolation)    │
│ L5: Human-in-the-loop for high-risk actions          │
└──────────────────────────────────────────────────────┘
```

> **Important:** No single layer defeats prompt injection. L4 (canary detection below) is a heuristic signal — it catches naive attacks but is trivially bypassed by encoding, synonyms, or indirect phrasing. The real defences are L2 (system prompt hierarchy enforced by the model), L3 (structured output parsing that rejects free-form overrides), and L5 (human approval gates on high-risk tool calls). L4 exists to catch low-effort attacks and generate audit signals.

```csharp
public sealed class PromptInjectionGuard
{
    // L4: Heuristic canary detection — defence-in-depth signal, NOT a primary control.
    // Catches naive attacks. Sophisticated attacks bypass this trivially.
    // Primary defences: L2 (system prompt hardening) + L5 (HITL gates).
    private static readonly string[] Canaries = [
        "ignore previous instructions", "ignore all instructions",
        "system prompt:", "you are now", "disregard",
        "forget your instructions", "new persona"
    ];

    public static bool ContainsSuspiciousPatterns(string input)
    {
        var lower = input.ToLowerInvariant();
        return Canaries.Any(c => lower.Contains(c));
    }
}

// In document upload pipeline:
public async Task<Document> IngestDocumentAsync(Stream file, string fileName)
{
    var text = await _extractor.ExtractTextAsync(file, fileName);
    if (PromptInjectionGuard.ContainsSuspiciousPatterns(text))
    {
        _logger.LogWarning("Injection pattern detected in uploaded document: {FileName}", fileName);
        // Flag for human review, do NOT auto-reject (false positives)
        return new Document(text, Flagged: true, FlagReason: "Potential prompt injection");
    }
    return new Document(text, Flagged: false);
}
```

**Learning poisoning defence:**

> **Decision change (updates ADR-014):** Agent-extracted learnings now start in `pending` status, not `active`. This adds a human gate to the knowledge extraction pipeline. ADR-014 has been updated to add `pending` to the learning status enum.

- Agent-extracted learnings go to `pending` state — require human promotion to `active`
- Confidence threshold: agent must self-rate confidence ≥0.8 to even propose a learning
- Learnings include `source_execution_id` — traceable to the exact run that produced them
- `pending` learnings surface as a review task (linked to the Task primitive per Principle 1)
- Periodic review: project owner reviews `pending` learnings; auto-expire after 30 days if unreviewed

**PCD poisoning defence:**
- PCD paths split: `readonly` (principles, guardrails) vs `writable` (context, notes) — per ADR-014
- Agent can only write to `writable` paths via `PcdUpdateTool` with path allowlist
- All PCD writes produce a diff stored in audit — human can review and revert

#### 2. Agent Tool Abuse Prevention

```csharp
public sealed class SandboxedFileReadTool(HttpClient httpClient, string poolEndpoint, string sessionId)
{
    [Description("Read a file from the project workspace")]
    public async Task<string> ReadFileAsync(
        [Description("Relative path within the project workspace")] string relativePath)
    {
        // Path traversal prevention — validate BEFORE sending to sandbox
        if (relativePath.Contains("..") || Path.IsPathRooted(relativePath))
            throw new SecurityException("Access denied: path traversal attempt");

        // Delegate to Dynamic Sessions API — file read happens INSIDE the sandbox,
        // not on the host filesystem. The sandbox's own filesystem is isolated per-project.
        var response = await httpClient.PostAsJsonAsync(
            $"{poolEndpoint}/code/execute?identifier={sessionId}&api-version=2025-10-02-preview",
            new { properties = new { codeInputType = "InlineShell", executionType = "Synchronous",
                                     code = $"cat '{relativePath}'" } });

        var result = await response.Content.ReadFromJsonAsync<SessionResult>();
        if (result!.ExitCode != 0)
            throw new FileNotFoundException($"File not found or unreadable: {relativePath}");
        return result.Stdout;
    }
}
```

> **Critical:** File operations MUST delegate to the Dynamic Sessions REST API (per ADR-006). Agents never access the host filesystem. The sandbox provides per-project isolation via Hyper-V — the host path traversal check is defence-in-depth on the relative path argument before it reaches the sandbox.

**Shell command controls:**
- All commands execute in Dynamic Sessions (Hyper-V isolated) — never on host (see ADR-006)
- Session identifier generated server-side via HMAC (ADR-006) — **never from LLM or user input**
- Command blocklist for destructive operations: `rm -rf /`, `dd`, `mkfs`, `shutdown` → blocked
- Network egress disabled by default — agent cannot `curl` arbitrary endpoints
- Max execution time: 5 minutes per command, 30 minutes per task

**Data exfiltration prevention:**
- Web search queries logged and auditable
- Search query content scanned for patterns matching project data (account numbers, names)
- Outbound network restricted to allowlisted FQDNs only (via Azure Firewall — see Area 6)

**API access control:**
- Each agent instance has a tool manifest — only tools in the manifest are callable
- Tool manifest is set at project creation by owner — agents cannot modify
- Internal API calls use the project's scoped credentials, not platform credentials

#### 3. Agent Output Validation

| Output Type | Validation | Enforcement |
|------------|-----------|-------------|
| Code for execution | Sandbox-only execution (Dynamic Sessions). No static analysis pre-execution — sandbox IS the control. | Hard |
| PCD updates | Path allowlist + diff logged + human review for `readonly` paths | Hard |
| Learnings | `pending` state → human promotion. Confidence threshold. Dedup check (ADR-014 §Deduplication). | Soft (human gate) |
| Reports/artifacts | Content Safety scan before delivery to user | Soft (flag + deliver) |
| Tool call arguments | Schema validation via MAF `FunctionInvokingChatClient` | Hard |

#### 4. Agent Identity & Accountability

- Every agent action tagged with: `agentName`, `agentInstanceId`, `executionId`, `taskId`, `projectId`
- Agent identity is server-assigned — agents cannot set their own identity claims
- No inter-agent communication except through the orchestrator (Durable Task) — agents cannot impersonate each other
- Full trace: `user → project → execution → task → agent → tool call → audit record`

#### 5. Model Safety

**IChatClient pipeline position** (integrates with ADR-009 and ADR-008 middleware):

```
IChatClient pipeline — execution order (outermost called first):

  1. BudgetEnforcingChatClient       (ADR-009) — fail fast if over budget
  2. AuditingChatClient              (ADR-008) — capture input hash before model call
  3. FunctionInvokingChatClient      (.UseFunctionInvocation) — tool calls
  4. ContentSafetyChatClient         ← NEW (this document) — scan output post-model
  5. OpenTelemetry (.UseOpenTelemetry)
  6. Inner (model call)

  .AsBuilder()
    .Use<BudgetEnforcingChatClient>()     // 1 — outermost (first .Use = first called)
    .Use<AuditingChatClient>()            // 2
    .UseFunctionInvocation()              // 3
    .Use<ContentSafetyChatClient>()       // 4
    .UseOpenTelemetry()                   // 5
    .Build(sp)                            // 6 — innermost (model)
```

```csharp
// Content Safety middleware in IChatClient pipeline
public sealed class ContentSafetyChatClient(IChatClient inner, ContentSafetyClient safety)
    : DelegatingChatClient(inner)
{
    // Per-category severity thresholds — lower = more restrictive
    private static readonly Dictionary<TextCategory, int> Thresholds = new()
    {
        [TextCategory.Hate] = 2,
        [TextCategory.Violence] = 4,
        [TextCategory.Sexual] = 2,
        [TextCategory.SelfHarm] = 2,
    };

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken ct)
    {
        var response = await base.GetResponseAsync(messages, options, ct);
        var text = string.Join("\n", response.Messages.Select(m => m.Text));
        var result = await safety.AnalyzeTextAsync(new AnalyzeTextOptions(text));

        var violation = result.Value.CategoriesAnalysis.FirstOrDefault(c =>
            Thresholds.TryGetValue(c.Category, out var threshold) && c.Severity >= threshold);

        if (violation is not null)
        {
            // Block — do not deliver to user
            throw new ContentSafetyViolationException(violation.Category, violation.Severity, result.Value);
        }
        return response;
    }
}
```

- Categories: Hate (≥2 block), Violence (≥4 block), Sexual (≥2 block), Self-harm (≥2 block)
- Violations: audit-logged, execution paused, owner notified
- **Input scanning**: enabled at L1 (input sanitisation layer). User messages and uploaded document text are scanned before reaching the model. This catches harmful content early and generates audit signals. Input scanning uses the same `ContentSafetyClient` but with higher tolerance thresholds (severity ≥4 for all categories) to avoid over-blocking legitimate business content.
- **Output scanning**: stricter thresholds (per-category as above) — this is where the model may generate harmful content unprompted.

#### 6. Guardrail Enforcement

PCD guardrails are **not just prompt text** — they have a programmatic enforcement layer:

```csharp
public sealed class GuardrailEnforcementMiddleware
{
    // Guardrails extracted from PCD at execution start
    // Enforced as tool-call interceptors
    public async Task<bool> ValidateToolCallAsync(string toolName, JsonElement args, Guardrail[] rules)
    {
        foreach (var rule in rules.Where(r => r.AppliesTo(toolName)))
        {
            if (!rule.Evaluate(args))
            {
                _audit.LogGuardrailViolation(toolName, rule.Id, args);
                return false; // Tool call blocked
            }
        }
        return true;
    }
}
```

- Guardrail types: `max_cost_per_execution`, `blocked_tools`, `required_approval_tools`, `max_file_size`, `allowed_domains`
- Evaluated **before** tool execution — not after
- Violations are hard blocks (not warnings)

> **Future: OPA/Rego policy engine.** The TRD (10.2, FR-43) requires a runtime policy-as-code layer for jurisdictional and data-scope decisions. The current guardrail interceptor is a stepping stone. When OPA is integrated, the `Guardrail.Evaluate()` method should delegate to OPA for complex policy decisions (jurisdiction, data classification, tool+data combinations). See Gaps §G2.

---

## AREA 5: Data Protection

### Threat Table

| # | Threat | Likelihood | Impact |
|---|--------|-----------|--------|
| 5.1 | PII in LLM prompts sent to third-party (Anthropic) | H | H |
| 5.2 | Cross-jurisdiction data transfer (SA↔UK) | M | H |
| 5.3 | WORM storage prevents right-to-erasure compliance | M | H |
| 5.4 | Unclassified data handled at wrong protection level | M | M |

### Controls

#### 1. Data Classification Scheme

| Classification | Description | Examples |
|---------------|-------------|---------|
| **Restricted** | Regulatory, PII, financial data | Audit records, uploaded client documents, LLM call content with PII |
| **Confidential** | Internal business data | PCD, agent prompts, learnings, cost data, workflow definitions |
| **Internal** | Operational data | Metrics, logs (redacted), agent catalog, templates |
| **Public** | No sensitivity | API documentation, platform version |

| Entity | Classification | Encryption | Residency |
|--------|---------------|-----------|-----------|
| Audit records | Restricted | CMK | Per-region (ADR-008) |
| Uploaded documents | Restricted | CMK | Per-project region |
| LLM call content (full text) | Restricted | CMK | Per-project region (in WORM only — not operational DB) |
| PCD | Confidential | CMK | Per-project region |
| Learnings | Confidential | CMK | Per-project region (until promoted) |
| Artifacts/reports | Confidential | CMK | Per-project region |
| Agent catalog | Internal | Azure-managed | Global |
| Templates | Internal | Azure-managed | Global |

#### 2. Encryption at Rest — CMK

Per ADR-004 (CMK via Key Vault Premium). Security-specific additions:

| Resource | Key Type | Rotation | Notes |
|----------|---------|----------|-------|
| PostgreSQL Flexible Server | CMK (Azure Key Vault RSA 2048) | Auto-rotate yearly | ADR-004 |
| Blob Storage (audit evidence) | CMK (Key Vault) | Auto-rotate yearly | ADR-008 |
| Redis Cache | **Decision required — see below** | See below | BRD NFR-08 mandates CMK for customer data. Basic/Standard tiers do not support CMK. |

**Redis CMK decision (requires ADR or formal risk acceptance):**

Redis caches: authorisation role lookups, idempotency responses, SSE tokens, SignalR backplane messages, and the emergency stop kill switch. Assessment of cached data classification:

| Cached Data | Classification | Contains PII? |
|------------|---------------|---------------|
| Role lookups (`proj-role:{userId}:{projectId}`) | Internal | No (user IDs only, not names) |
| Idempotency responses | Confidential | Potentially (response bodies) |
| SSE tokens (30s TTL, single-use) | Internal | No |
| SignalR messages | Confidential | Potentially (event payloads) |
| Emergency stop flag | Internal | No |

**Option A: Redis Enterprise (E10)** — CMK supported, ~$450/mo per region. Full BRD NFR-08 compliance.
**Option B: Azure Cache for Redis Standard + risk acceptance** — ~$55/mo per region. Compensating controls: TLS 1.2 in transit, private endpoint (no public access), Entra ID authentication, 30-day max TTL on cached data. Formal risk acceptance required from CISO, documenting that cached content is transient and does not include Restricted-classified data (audit records and uploaded documents are never cached in Redis).

**Recommendation:** Option B with risk acceptance. Redis content is transient (seconds to hours), operationally scoped, and protected by private endpoint + TLS. The cost delta (~$400/mo/region) is not justified for Internal/Confidential transient cache data. Ensure idempotency response caching strips any PII before storage, or move idempotency to PostgreSQL for CMK coverage.
| Event Hubs | CMK (Key Vault) | Auto-rotate yearly | ADR-008 |

#### 3. Encryption in Transit

- TLS 1.2 minimum everywhere; TLS 1.3 preferred
- ACA enforces HTTPS ingress — HTTP redirected
- Internal service communication: mTLS via ACA service mesh (Envoy sidecar)
- Database connections: `sslmode=verify-full` with CA certificate pinning

#### 4. PII Handling

```csharp
// PII detection middleware — scans agent outputs before storage
public sealed class PiiDetectionService(TextAnalyticsClient client)
{
    public async Task<PiiScanResult> ScanAsync(string text)
    {
        var result = await client.RecognizePiiEntitiesAsync(text);
        var entities = result.Value.Where(e => e.ConfidenceScore > 0.8).ToList();
        if (entities.Count == 0) return PiiScanResult.Clean;

        var redacted = result.Value.RedactedText;
        return new PiiScanResult(entities.Select(e => e.Category.ToString()).ToList(), redacted);
    }
}
```

- PII entry points: uploaded documents, chat messages, agent outputs
- Detection: Azure AI Language PII detection (SA ID numbers, names, emails, phone numbers)
- **Foundry/Claude**: Data Processing Agreement in place. Anthropic's Foundry offering = zero retention, no training. PII still minimised before sending.
- Learnings: PII stripped or tokenised before storage. Original context in audit only.

#### 5. Data Residency

- `project.region` field set at creation: `sa-north` or `uk-south`
- All project data (tasks, PCD, documents, artifacts, learnings) stored in region-specific PostgreSQL + Blob
- **Shared platform learnings**: promoted learnings are jurisdiction-tagged. Cross-jurisdiction promotion requires `platform_admin` review + legal sign-off flag
- Enforced at infrastructure level: separate storage accounts per region, no GRS/geo-replication (ADR-008)
- **Note on backup**: ADR-004 specifies geo-redundant backup (35-day PITR). Geo-backup replication to a paired region is acceptable for disaster recovery as it is encrypted and not operationally accessible — distinct from GRS replication which creates a live readable replica.

> **Gap: additional jurisdictions.** BRD BR-23 lists 7 operating jurisdictions (UK, SA, Channel Islands, Switzerland, India, Mauritius, UAE). This design covers only UK and SA. The `project.region` enum must be extended, and region-specific storage/policy infrastructure provisioned, before deploying teams in other jurisdictions.

#### 6. Data Retention

| Entity | Active | Archived | Deleted |
|--------|--------|----------|---------|
| Audit records | 7 years (WORM — ADR-008) | N/A (immutable) | Never |
| Project data | While project active | 1 year after archival | Soft-delete, 30-day recovery |
| PCD | While project active | With project | With project |
| Learnings (active) | Indefinite | With project | Retractable (soft-delete per ADR-014 §Retraction) |
| Documents | While project active | 1 year after archival | Hard-delete on request |
| LLM call content (full text) | 7 years (in WORM only) | N/A | Never |

#### 7. Right to Erasure

- **Audit records (WORM)**: cannot delete. **Mitigation**: pseudonymise — replace PII with tokens in a mapping table. Delete the mapping = PII is irrecoverable. Document this approach in DPIA.
- **Project data**: standard soft-delete → hard-delete after 30-day grace
- **Learnings**: retract (mark as retracted, exclude from retrieval per ADR-014) — do not hard-delete as they may be referenced by audit records
- **Process**: Data Subject Access Request (DSAR) workflow built into platform — owner-triggered, platform_admin-approved

#### 8. Data Minimisation

- Do NOT store: full prompt/response text in operational DB (only in audit WORM — per ADR-008)
- Do NOT store: raw uploaded documents after text extraction (store extracted text only, original in Blob with retention)
- Hash PII in learnings where the learning value doesn't depend on the PII
- Token counts stored as integers, not reconstructable content

---

## AREA 6: Network Security

### Architecture

```
Internet → Azure Front Door (WAF)
              → Azure API Management (APIM)     ← Group-standard gateway (TRD)
                  → ACA Environment (VNet-integrated)
                       ├── BFF App (external ingress via APIM only)
                       ├── Worker App (internal only — no public ingress)
                       ├── Dynamic Sessions Pool (Hyper-V)
                       │     └── Egress → Azure Firewall → Allowlist
                       └── Private Endpoints:
                             ├── PostgreSQL Flexible Server
                             ├── Redis Cache
                             ├── Blob Storage (audit)
                             ├── Event Hubs
                             ├── Key Vault
                             └── Azure AI Foundry
```

### Controls

| Component | Configuration |
|-----------|--------------|
| **Azure Front Door** | WAF policy (OWASP 3.2 ruleset + bot protection). DDoS Protection Standard. **SignalR/WebSocket:** Front Door supports WebSocket natively. WAF rules must exclude the `/hubs/project` path from request body inspection (binary frames trigger false positives). Configure `ws://` → `wss://` upgrade enforcement. |
| **APIM** | Group-standard API gateway. OAuth 2.0 validation at edge. Subscription keys for external consumers. Threat protection policies. Rate limiting at perimeter (coarse) + application-level (fine). **SignalR:** APIM requires WebSocket pass-through configuration (`<set-backend-service>` with WebSocket enabled). SSE endpoints (`/api/v1/projects/{id}/events/stream`) must bypass APIM response buffering (`Transfer-Encoding: chunked`). |
| **ACA Environment** | VNet-integrated, workload profiles enabled. BFF: external ingress (HTTPS only, via APIM). Worker: internal only (no public ingress). |
| **Dynamic Sessions** | `EgressEnabled: false` default. When enabled: Azure Firewall FQDN allowlist (see below). Session ID generated server-side via HMAC (ADR-006). |
| **PostgreSQL** | Private endpoint only. Public access disabled. `pgbouncer` in front. `pgAudit` extension enabled (ADR-004). Firewall: deny all except ACA subnet. |
| **Redis** | Private endpoint. TLS 1.2 required. Access key + Managed Identity (Entra auth). No public access. Enterprise tier for CMK (see Area 5). |
| **Blob Storage** | Private endpoint. Public access disabled. Firewall: deny all except ACA subnet + trusted Azure services. |
| **Azure AI Foundry** | Private endpoint. No public endpoint. IP restriction to ACA outbound IPs as backup. |
| **BFF ↔ Worker** | **Redis Streams** (XADD/XREADGROUP) for work dispatch. Redis pub/sub for event fan-out to SignalR. Durable Task Scheduler for workflow durability. No direct HTTP between BFF and Worker for normal operations. (Per ADR-012) |

### Dynamic Sessions Firewall Allowlist

| FQDN | Purpose |
|------|---------|
| `*.github.com`, `*.githubusercontent.com` | Git clone |
| `registry.npmjs.org` | npm packages |
| `api.nuget.org` | NuGet packages |
| `pypi.org`, `files.pythonhosted.org` | Python packages |
| `*.investec.com` | Internal APIs (specific endpoints) |

All other outbound traffic **denied**.

### ACI Fallback Security

ADR-006 defines ACI as fallback for long-lived projects (>24h). ACI-specific security:
- Confidential Containers (AMD SEV-SNP) for workloads processing client data
- Azure Files SMB mount per project — encrypted at rest, private endpoint
- Network: NSG rules restricting egress to same FQDN allowlist as Dynamic Sessions
- No Hyper-V isolation in ACI — rely on Confidential Containers for isolation

---

## AREA 7: Secrets Management

### Controls

| Secret | Storage | Rotation | Access |
|--------|---------|----------|--------|
| DB connection string | Key Vault → Aspire reference | 90 days (automated) | Managed Identity |
| Redis access key | Key Vault (if not Entra auth) | 90 days | Managed Identity |
| Foundry API key | Not used (Managed Identity) | N/A | N/A |
| Event Hubs connection | Not used (Managed Identity) | N/A | N/A |
| Content Safety key | Key Vault | 90 days | Managed Identity |

> **Note:** API key hashing uses Argon2id with per-key salt embedded in the hash output (standard Argon2id format). There is no separate HMAC key or salt stored in Key Vault for API key verification.

**Key Vault access**: Managed Identity only. No access policies — RBAC mode (`Key Vault Secrets User` role).

**Development secrets**: `dotnet user-secrets` for local dev. Never `.env` files (gitignore risk). Developers access non-prod Key Vault via their Entra ID.

**CI/CD secrets**: Azure DevOps Service Connection with Workload Identity Federation (no stored secrets). Pipeline variables reference Key Vault directly.

### Supply Chain Security (TRD 9.4/9.5)

| Control | Implementation |
|---------|---------------|
| Container image signing | Notation (Notary v2) signatures on all images pushed to ACR. ACA verifies signatures on pull. |
| SBOM generation | `syft` generates SBOM at build time, attached to container image in ACR. |
| Dependency scanning | Defender for DevOps in ADO pipeline. CodeQL SAST on PR validation. |
| IaC scanning | Checkov on Bicep templates in PR validation pipeline. |
| Base image pinning | Dynamic Sessions base image pinned to digest, not tag. Monthly security patch cadence. |

---

## AREA 8: Operational Security

### 1. Emergency Stop

```csharp
public sealed class EmergencyStopService(
    IDurableTaskClient taskClient, IConnectionMultiplexer redis)
{
    public async Task ExecuteStopAsync(string triggeredBy, string reason, StopScope scope)
    {
        // 1. Set global/project kill switch in Redis
        await redis.GetDatabase().StringSetAsync(
            scope == StopScope.Global ? "emergency:global" : $"emergency:{scope.ProjectId}",
            JsonSerializer.Serialize(new { triggeredBy, reason, at = DateTimeOffset.UtcNow }));

        // 2. Cancel active orchestrations — filtered by scope
        var query = new OrchestrationQuery { RuntimeStatus = [OrchestrationRuntimeStatus.Running] };
        var instances = await taskClient.GetAllInstancesAsync(query);
        foreach (var inst in instances)
        {
            // Project-scoped stop: only terminate orchestrations for this project
            // Instance ID format: "{projectId}:{workflowName}:{runId}" (set at dispatch time)
            if (scope != StopScope.Global
                && !inst.InstanceId.StartsWith($"{scope.ProjectId}:"))
                continue;

            await taskClient.TerminateInstanceAsync(inst.InstanceId, reason);
        }

        // 3. Stop active Dynamic Sessions containers (prevents in-flight commands from continuing)
        var activeSessions = await _sessionTracker.GetActiveSessionsAsync(scope);
        foreach (var session in activeSessions)
            await _httpClient.DeleteAsync(
                $"{session.PoolEndpoint}/sessions/{session.SessionId}?api-version=2025-10-02-preview");

        // 4. Publish to all connected clients via SignalR
        // 5. Audit log the emergency stop
    }
}
```

- **Who**: `platform_admin` (global) or project `owner` (project-scoped). Per TRD 9.1, privileged operations require **PIM-activated roles** (just-in-time elevation).
- **What happens**: all running orchestrations terminated, new executions blocked (Redis kill switch checked on start), SSE clients notified
- **Recovery**: explicit `POST /api/v1/admin/emergency/resume` by `platform_admin` with confirmation body (see [API Design §1.29](../002-architecture/004-api-design.md))

### 2. Break-Glass Access

- Two `platform_admin` accounts minimum — no single point of failure
- Break-glass Entra ID account stored in physical safe (printed credentials)
- Break-glass account requires MFA via FIDO2 key (also in safe)
- Usage triggers immediate alert to security team
- Quarterly test of break-glass procedure

### 3. Incident Response

| Incident | Response |
|----------|----------|
| Compromised API key | Immediate revocation. Audit all actions by key in last 30 days. Notify key owner. Rotate any resources the key accessed. |
| Prompt injection detected | Flag execution. Quarantine outputs. Review uploaded documents. Add pattern to detection rules. |
| Data breach | Emergency stop. Isolate affected project. Notify DPO within 24h. Regulatory notification within 72h (GDPR) / ASAP (POPIA). Log operational risk event in Group ITSM. |
| Harmful agent content | Content Safety violation logged. Output quarantined. Execution paused. Owner notified. Review guardrails. |

### 4. Penetration Testing Scope

| Area | Test |
|------|------|
| Prompt injection | Multi-turn injection, document injection, learning poisoning |
| Auth bypass | JWT manipulation, API key brute-force, SSE token replay |
| Privilege escalation | Operator→Owner, cross-project access, workflow gate bypass |
| Data exfiltration | Agent tool abuse, search query exfiltration, file scope escape |
| Network | Sandbox egress bypass, private endpoint bypass |
| Session ID prediction | Dynamic Sessions identifier predictability (ADR-006 HMAC) |

### 5. Security Monitoring Alerts

| Alert | Threshold | Severity | Destination |
|-------|-----------|----------|-------------|
| Failed auth attempts | >10/min same user | High | Sentinel + platform alert |
| API key used from new IP | First-time IP for key | Medium | Sentinel |
| Agent cost anomaly | >2x average execution cost | Medium | Platform alert |
| Hash chain gap | Any missing sequence number | Critical | Sentinel + on-call page |
| Bulk file access | >50 files read in single execution | High | Platform alert |
| Guardrail violation | Any | High | Sentinel + platform alert |
| Content Safety violation | Severity ≥4 | Critical | Sentinel + on-call page |
| Emergency stop triggered | Any | Critical | Sentinel + on-call page |
| Unused API key | >90 days inactive | Low | Weekly report |

> **SIEM integration:** All alerts flow to the Group's **Microsoft Sentinel** instance (TRD technology stack) via diagnostic settings and Event Hubs. Sentinel provides cross-platform threat correlation, Defender for Cloud integration, and SOAR automated response playbooks.

---

## AREA 9: Compliance Controls

### Regulatory Mapping

| Requirement | Control | Evidence |
|-------------|---------|----------|
| **FG16/5** Cloud outsourcing | Azure FCA/PRA compliance. Data residency. Exit plan documented. APIM as API gateway. | SOC 2 Type II, ISO 27001 attestations |
| **SS1/23** Model risk management | Full audit trail of model inputs/outputs (ADR-008). Model version pinning. Human approval gates. Content Safety filtering. MRM inventory integration. | WORM audit records, hash chain, approval gate logs |
| **PS21/3** Operational resilience | Emergency stop. Multi-region capability. RTO/RPO defined. Dependency mapping. | Incident runbooks, DR test results |
| **Joint Standard** Outsourcing (SARB) | Contractual controls with Anthropic/Microsoft. Data residency SA North. Board approval for material outsourcing. | DPA agreements, Board resolutions |
| **POPIA s72** Cross-border | SA data in SA North (ZRS, no GRS). Cross-jurisdiction learning promotion requires legal review. | Infrastructure config, data flow diagrams |

### Privileged Access Review (Twice-Yearly)

```sql
-- Automated report for PAR
SELECT u.display_name, u.email,
       CASE WHEN u.platform_role = 'platform_admin' THEN 'Platform Admin' END AS platform_role,
       array_agg(DISTINCT pm.project_id || ':' || pm.role) AS project_roles,
       ak.key_prefix, ak.scopes, ak.expires_at, ak.last_used_at
FROM users u
LEFT JOIN project_members pm ON u.id = pm.user_id
LEFT JOIN api_keys ak ON u.id = ak.user_id AND ak.revoked_at IS NULL
WHERE u.platform_role = 'platform_admin'
   OR ak.id IS NOT NULL
GROUP BY u.id, ak.key_prefix, ak.scopes, ak.expires_at, ak.last_used_at;
```

- Report generated automatically on 1 Jan and 1 Jul
- Sent to CISO and platform owner for sign-off
- Unused keys (>90 days) auto-flagged for revocation
- Stale admin accounts flagged for removal
- PIM activation logs reviewed for anomalous patterns

### Change Management

| Change Type | Approval | Evidence |
|------------|----------|----------|
| Agent prompt change | PR review + dual approval (business owner + risk owner per BRD FR-05). Content hash pinned. | Git commit hash in audit |
| Model version change | `platform_admin` approval + regression test + MRM inventory update | ADR + test results |
| Workflow change | Project `owner` approval | Workflow version diff in audit |
| Guardrail change | `platform_admin` approval | PCD version diff |
| Template change (permissions/scopes/tools) | Dual approval (business owner + risk owner per BRD FR-05) | Git commit hash + approval record |
| Infrastructure change | Avalanche pipeline + reviewer approval | ADO pipeline logs |

### Third-Party Risk

| Third Party | Risk | Control |
|------------|------|---------|
| **Anthropic** (Claude) | Service disruption | Fallback to Azure OpenAI. No single-model dependency. |
| **Anthropic** | Data handling | Foundry = zero retention, no training. DPA in place. |
| **Anthropic** | Model behaviour change | Model version pinning. Regression test suite on version update. MRM classification. |
| **Microsoft** (Azure) | Service disruption | Multi-region deployment. Operational resilience testing. |
| **Microsoft** | Data handling | Azure FCA/PRA compliance. CMK encryption. |

### PIM-Activated Roles (TRD 9.1)

Privileged operations require just-in-time elevation via Entra ID Privileged Identity Management:

| Operation | PIM Role | Max Duration | Approval |
|-----------|---------|-------------|----------|
| Emergency stop | Platform Admin | 4 hours | Self-approve with MFA + reason |
| Template approval | Platform Admin | 2 hours | Peer approval required |
| Production deployment | Platform Admin | 2 hours | Peer approval required |
| Database operations | Platform Admin | 1 hour | Peer approval + ITSM ticket |

Standing `platform_admin` assignment: **not permitted in production**. All admin access is PIM-activated with time-bound elevation.

### MRM Integration (BRD BR-20)

Every agent template must be registered in the Group's Model Risk Management inventory:

| MRM Field | Source |
|-----------|--------|
| Model name + version | Agent catalog entry (`model_id`, `model_version`) |
| Risk classification | Set by MRM team (Low/Medium/High/Critical) |
| Use case description | Template `description` field |
| Data inputs/outputs | Template tool manifest + PCD scope |
| Validation results | Regression test suite results, content safety benchmarks |
| Review cadence | Per MRM classification (quarterly for High, annually for Low) |

---

## Known Gaps (Deferred)

| # | Gap | BRD/TRD Reference | Mitigation |
|---|-----|-------------------|-----------|
| G1 | DPIA workflow gate before production deployment of teams processing personal data | BRD NFR-13 | Manual DPIA process until automated gate is built |
| G2 | OPA/Rego policy-as-code engine for runtime jurisdiction/data-scope decisions | TRD 10.2, FR-43 | C# guardrail interceptors as stepping stone; OPA integration planned |
| G3 | Evidence pack generator (on-demand regulatory evidence assembly) | BRD BR-24, FR-37 | Manual KQL queries against Eventhouse until endpoint is built |
| G4 | Dual-control (four-eyes) runtime gates beyond single reviewer | BRD BR-19, FR-33 | **Planned (priority: high).** Required for the Maker-Checker-Supervisor (MCS) template — the most common regulated workflow pattern. Implementation: add optional `required_approvers: 2` field to approval gate definition. When set, the gate requires two independent approvals from different users before proceeding. Both approvers must satisfy `triggered_by != approved_by` and `approver_1 != approver_2`. Target: v1.1 release. |
| G5 | Data residency for 5 additional jurisdictions (CI, CH, IN, MU, AE) | BRD BR-23 | Deploy to UK/SA first; extend `project.region` enum as needed |
| G6 | Audit schema registry and contract tests for Group data platform export | TRD 11.3 | Event Hubs schema validation; formal registry deferred |

---

## Security Controls Matrix

| # | Control | Threat | Implementation | Owner | Verification |
|---|---------|--------|---------------|-------|-------------|
| C01 | Rate limiting (per-user, per-endpoint) | 1.1 Brute-force | `AddRateLimiter` middleware | Platform Eng | Pen test, load test |
| C02 | Input validation (size, type, bounds) | 1.2 Payload DoS, 1.3 Injection | Kestrel limits + DataAnnotations | Platform Eng | Automated API tests |
| C03 | Output sanitisation | 1.4 Info leakage | Custom `ProblemDetailsFactory` | Platform Eng | Code review |
| C04 | Security headers | 1.5, 1.6 CORS/XSS | Middleware | Platform Eng | Header scan (securityheaders.com) |
| C05 | Idempotency keys (fail-open) | 1.7 Replay | Redis-backed middleware, fail-open on outage | Platform Eng | Integration tests |
| C06 | JWT algorithm pinning + claim validation | 2.1 JWT forgery | `TokenValidationParameters` (delta over ADR-007) | Platform Eng | Security review |
| C07 | API key Argon2id hashing | 2.3 Key theft | `Konscious.Security.Cryptography` (updates ADR-007) | Platform Eng | Pen test |
| C08 | SSE token bind-to-resource | 2.4 SSE replay | Redis GETDEL + project binding (delta over ADR-007) | Platform Eng | Integration tests |
| C09 | Service-to-service MI auth | 2.5 Impersonation | Entra ID token exchange | Platform Eng | Infrastructure audit |
| C10 | Refresh token revocation | 2.6 Stale access | MS Graph `revokeSignInSessions` | Platform Eng | Manual test |
| C11 | Project role cache + eviction + temporal expiry | 3.1, 3.4 Authz bypass | `IMemoryCache` + Redis pub/sub + `expires_at` | Platform Eng | Integration tests |
| C12 | Field-level DTO projection | 3.1 Data exposure | Role-based `ToDto()` | Platform Eng | Unit tests |
| C13 | Cross-project data isolation | 3.2 Data leakage | `project_id` partitioning everywhere | Platform Eng | Pen test |
| C14 | Workflow gate immutability | 3.3, 3.5 Priv escalation | Server-side schema validation | Platform Eng | Code review |
| C15 | Prompt injection defence (5 layers) | 4.1, 4.2 Injection | L1-L5 layered defence | AI Eng | Red team exercise |
| C16 | Learning human gate (`pending` → `active`) | 4.3 Knowledge poisoning | Updates ADR-014 | AI Eng | Process audit |
| C17 | PCD path allowlist | 4.7 PCD poisoning | `PcdUpdateTool` path enforcement (per ADR-014) | AI Eng | Unit tests |
| C18 | Sandbox file scope | 4.4 File escape | Path traversal prevention | Platform Eng | Pen test |
| C19 | Dynamic Sessions isolation | 4.4, 4.5, 4.6 Agent abuse | Hyper-V + network egress disabled (ADR-006) | Platform Eng | Infrastructure audit |
| C20 | Tool manifest per agent | 4.6 Unauthorised API calls | Server-set manifest, immutable | AI Eng | Code review |
| C21 | Content Safety filtering | 4.1 Harmful content | Azure AI Content Safety in IChatClient pipeline | AI Eng | Red team exercise |
| C22 | Guardrail enforcement | 4.7 Constraint bypass | Programmatic pre-execution check (→ OPA future) | AI Eng | Unit tests |
| C23 | CMK encryption at rest | 5.1 Data exposure | Key Vault + auto-rotation (ADR-004) | Infra Eng | Azure Policy |
| C24 | PII detection + redaction | 5.1 PII in LLM calls | Azure AI Language PII | AI Eng | Sampling audit |
| C25 | Data residency enforcement | 5.2 Cross-border | Region-specific resources, no GRS (ADR-008) | Infra Eng | Infrastructure audit |
| C26 | Pseudonymised WORM records | 5.3 Right to erasure | Tokenised PII + deletable mapping | Platform Eng | DPIA review |
| C27 | Private endpoints everywhere | Network exposure | VNet integration + PE | Infra Eng | Azure Policy |
| C28 | Azure Firewall egress allowlist | 4.5 Data exfiltration | FQDN rules on sandbox (ADR-006) | Infra Eng | Firewall audit |
| C29 | Key Vault + MI only | Secret exposure | RBAC mode, no access policies | Infra Eng | Azure Policy |
| C30 | Supply chain security | Compromised dependencies | Notation signing, SBOM, Defender for DevOps, CodeQL | Platform Eng | Pipeline audit |
| C31 | Emergency stop | Runaway agents | Redis kill switch + orchestration terminate | Platform Eng | Quarterly drill |
| C32 | Break-glass account | Admin unavailability | Physical safe + FIDO2 | CISO | Quarterly test |
| C33 | Hash chain + Merkle root | Audit tampering | SHA-256 chain + daily anchor (ADR-008) | Platform Eng | Daily verification job |
| C34 | Privileged access review | Stale elevated access | Automated report, twice-yearly | CISO | Sign-off record |
| C35 | Model version pinning + MRM | Model behaviour drift | Pin in config, regression test, MRM inventory | AI Eng | Change record |
| C36 | Segregation of duties | Maker-checker bypass | `triggered_by != approved_by` (ADR-007) | Platform Eng | Audit query |
| C37 | PIM-activated roles | Standing privilege abuse | Entra PIM, time-bound elevation, peer approval | CISO | PIM audit logs |
| C38 | SIEM integration (Sentinel) | Undetected threats | Diagnostic settings → Event Hubs → Sentinel | Infra Eng | Alert testing |
| C39 | APIM gateway | Perimeter security | Azure APIM with WAF + Front Door | Infra Eng | Infrastructure audit |
| C40 | pgAudit DB-level logging | DB-level tampering | PostgreSQL `pgAudit` extension (ADR-004) | Infra Eng | Log review |

---

## Cross-Reference: R17 vs Existing ADRs

| ADR | What R17 References | What R17 Adds (Net-New) |
|-----|--------------------|-----------------------|
| ADR-007 | Dual-scheme auth, SSE token, role model, SoD | JWT hardening (RS256 pin, tight skew, tid), Argon2id upgrade, field-level authz, temporal roles, delegation, token theft signals |
| ADR-008 | WORM pipeline, hash chain, data residency | Right-to-erasure pseudonymisation, data classification scheme |
| ADR-006 | Dynamic Sessions, Hyper-V, HMAC session ID | ACI security properties, firewall FQDN allowlist details, pen test scope for session ID |
| ADR-014 | Knowledge taxonomy, retraction, promotion | `pending` learning status, learning human gate, PCD poisoning defence |
| ADR-012 | BFF+Worker separation, Redis Streams dispatch | Service-to-service auth (MI token exchange), KEDA cost attack prevention |
| ADR-004 | PostgreSQL, CMK, pgvector | pgAudit reference, Redis CMK gap, data minimisation rules |
| ADR-009 | IChatClient pipeline, BudgetEnforcing | ContentSafetyChatClient pipeline position |

---

*End of security architecture design. 40 controls across 9 areas + 6 known gaps. Implementation priority: C19 (sandbox isolation), C27 (private endpoints), C33 (hash chain), C15 (prompt injection defence), C37 (PIM), C38 (Sentinel) are foundational — implement first.*
