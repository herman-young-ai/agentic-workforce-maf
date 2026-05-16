# ADR-007: Identity, Authentication, and RBAC

**Status:** Accepted
**Date:** 2026-05-10
**Decision Makers:** Architecture team
**Research:** [R07-response-identity-auth.md](../098-research/R07-response-identity-auth.md)

---

## Context

The Agentic Workforce Platform needs authentication for human users (bank employees via SPA/CLI), programmatic consumers (external systems via API key), and agent workloads (service-to-service via Managed Identity). We're migrating to Entra ID to align with ICE/Avalanche standards.

## Decision

**Entra ID (Microsoft.Identity.Web) + API Key dual scheme + Managed Identity for workloads. Two-dimensional role model: platform roles (Entra ID) + project roles (per-project DB table).**

### Authentication: Dual scheme via PolicyScheme

Both Entra ID JWT and API Key accepted on the same `[Authorize]` attribute:

```csharp
// Program.cs
builder.Services.AddAuthentication(o => {
    o.DefaultScheme = "JwtOrApiKey";
    o.DefaultChallengeScheme = "JwtOrApiKey";
})
.AddMicrosoftIdentityWebApi(jwtOpts => {
    builder.Configuration.Bind("AzureAd", jwtOpts);
    jwtOpts.TokenValidationParameters.RoleClaimType = "roles";
}, identityOpts => builder.Configuration.Bind("AzureAd", identityOpts))
.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>("ApiKey", o => {
    o.HeaderName = "X-Api-Key";
})
.AddPolicyScheme("JwtOrApiKey", "JWT or API Key", o => {
    o.ForwardDefaultSelector = ctx =>
        ctx.Request.Headers.ContainsKey("X-Api-Key") ? "ApiKey" : JwtBearerDefaults.AuthenticationScheme;
});
```

### Authorization: Two-Dimensional Role Model

#### Platform Roles (Entra ID App Roles — global)

Control what you can do on the platform itself, independent of any project:

| Role | Value | What They Do | Who |
|------|-------|-------------|-----|
| Platform Admin | `platform_admin` | Agent catalog CRUD, template management, model governance, user management, platform config, emergency stop, DB operations, cross-project audit access | Platform engineering team. 2-3 people. |
| Member | `member` | Can be assigned to projects. Baseline authenticated user. Sees only projects they're assigned to. | Everyone with Entra ID access. |

Declared in Entra ID app registration manifest:

```json
"appRoles": [
  {
    "id": "11111111-1111-1111-1111-111111111111",
    "allowedMemberTypes": ["User", "Application"],
    "displayName": "Platform Admin",
    "value": "platform_admin",
    "isEnabled": true
  },
  {
    "id": "22222222-2222-2222-2222-222222222222",
    "allowedMemberTypes": ["User", "Application"],
    "displayName": "Member",
    "value": "member",
    "isEnabled": true
  }
]
```

#### Project Roles (per-project, stored in `project_members` table)

Control what you can do within a specific project. A user can have different roles on different projects.

| Role | Can Run | Can Approve | Can Manage | Can View | Who |
|------|---------|-------------|------------|----------|-----|
| **Owner** | Yes | Yes | Yes (team, budget, settings, delete) | Yes | Created the project. 1 per project. Can transfer. |
| **Operator** | Yes | Yes | No | Yes | Day-to-day users. Run executions, chat with agents, approve at gates. |
| **Reviewer** | **No** | **Yes** | No | Yes | Approves results but cannot trigger executions. Segregation of duties. |
| **Viewer** | No | No | No | Yes | Read-only. Stakeholders, auditors, management. |

```sql
-- project_members table
| project_id | user_id | role     |
|------------|---------|----------|
| prj-001    | herman  | owner    |
| prj-001    | thabiso | operator |
| prj-001    | ockert  | reviewer |
| prj-002    | herman  | owner    |
| prj-002    | thabiso | viewer   |
```

#### Segregation of Duties

Critical for regulated banking (Maker-Checker patterns). The platform enforces:

- **Reviewer** role exists specifically so the person who approves is not the person who triggered
- On approval endpoints: `if (gate.TriggeredBy == currentUser.Id) throw new SegregationOfDutiesException()`
- Maps to TRD requirement: *"the same user cannot simultaneously hold conflicting roles on the same artefact"*

```
Operator triggers: "Run KYC screening for ABC Ltd"
    → Agents execute (Maker)
    → Results produced
    → Gate fires: "Approval required — PEP match found"
    → Reviewer approves or rejects (Checker) ← MUST be a different person
```

#### Platform Admin Override

Platform Admin can access all projects for audit and emergency purposes, but does not automatically get project roles. This is an explicit override in the authorization handler, logged for audit.

### Authorization Policies

```csharp
builder.Services.AddAuthorization(options =>
{
    // Platform-level
    options.AddPolicy("PlatformAdmin", p => p.RequireRole("platform_admin"));

    // Project-level (checked via IAuthorizationHandler against project_members)
    options.AddPolicy("ProjectOwner",    p => p.Requirements.Add(new ProjectRoleRequirement("owner")));
    options.AddPolicy("ProjectOperator", p => p.Requirements.Add(new ProjectRoleRequirement("operator")));
    options.AddPolicy("ProjectReviewer", p => p.Requirements.Add(new ProjectRoleRequirement("reviewer")));
    options.AddPolicy("ProjectViewer",   p => p.Requirements.Add(new ProjectRoleRequirement("viewer")));
});

// The handler checks: user has the required project role OR user is platform_admin
builder.Services.AddSingleton<IAuthorizationHandler, ProjectRoleAuthorizationHandler>();
```

### Workload Identity: User-Assigned Managed Identity

```csharp
// Single TokenCredential registered, reused across all Azure SDK clients
builder.Services.AddSingleton<TokenCredential>(_ =>
    new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ManagedIdentityClientId = builder.Configuration["AzureAd:ManagedIdentityClientId"]
    }));

// All Azure clients use the same MI — no stored secrets
builder.Services.AddSingleton(sp =>
    new AzureOpenAIClient(new Uri(endpoint), sp.GetRequiredService<TokenCredential>()));
builder.Services.AddSingleton(sp =>
    new SecretClient(new Uri(kvUri), sp.GetRequiredService<TokenCredential>()));
```

RBAC role assignments on the Managed Identity:

| Resource | Role |
|----------|------|
| Azure AI Foundry / OpenAI | Cognitive Services OpenAI User |
| Key Vault (RBAC mode) | Key Vault Secrets User |
| Storage (Blob) | Storage Blob Data Owner |
| Container Registry | AcrPull |
| Dynamic Sessions | Azure ContainerApps Session Executor |

### SSE Token Exchange

Browser `EventSource` can't set Authorization headers. Pattern:

1. Authenticated client calls `POST /api/sse/token` with JWT
2. Server mints a single-use token, stores in Redis with 30s TTL
3. Client opens `EventSource("/api/streams/projects/{id}?t={token}")`
4. Server validates + atomically deletes token from Redis (`GETDEL`)

### API Key Security

- Keys hashed with **Argon2id** (time=3, memory=19MB, parallelism=1 — OWASP minimum). Per-key salt embedded in the hash output (standard Argon2id format) — no separate HMAC key or salt stored in Key Vault.
- **Prefix lookup gate**: extract the 8-char public prefix from the submitted key, look up in `api_keys` table (indexed on `key_prefix`). If no match → reject immediately without hashing. Only compute Argon2id for known prefixes. This prevents resource exhaustion attacks via fabricated keys.
- **Why Argon2id over HMAC-SHA256:** HMAC-SHA256 is fast (~1B hashes/sec on GPU) — suitable for MACs but too fast for password-like secrets. Argon2id provides memory-hard resistance to GPU/ASIC brute-force.
- Key format: `awp_live_` + 32 random bytes (base62) — prefix identifies environment (`awp_live_`, `awp_test_`)
- Public prefix (8 chars) for identification; Argon2id hash for verification
- Verification via `Argon2.Verify()` (library handles constant-time comparison internally)
- Scopes stored as JSON (operations allowed)
- Expiry (max 1 year) and revocation tracked; `last_used_at` and `last_used_ip` updated on each use
- Keys unused >90 days flagged for review; alerts at 30d, 7d, 1d before expiry
- Rotation: new key created with overlapping validity (7-day grace); old key marked `rotating`
- API key holders are mapped to a User record with `member` platform role and explicit project role assignments

> **Decision change (2026-05-11, R17 security review):** Upgraded from HMAC-SHA256 to Argon2id for API key hashing. See [R17-response-security-design.md](../098-research/R17-response-security-design.md) §Area 2.2 for rationale.

## Infrastructure as Code

```bicep
resource appRegistration 'Microsoft.Graph/applications@v1.0' = {
  displayName: 'agentic-workforce-api'
  identifierUris: ['api://${clientId}']
  appRoles: [
    { id: guid1, value: 'platform_admin', displayName: 'Platform Admin', ... }
    { id: guid2, value: 'member',         displayName: 'Member',         ... }
  ]
}

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${uami.id}': {} } }
  properties: {
    configuration: {
      secrets: []  // NO secrets — all via Managed Identity
    }
  }
}
```

## Key Packages

```xml
<PackageReference Include="Microsoft.Identity.Web" Version="4.9.0" />
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="9.0.4" />
<PackageReference Include="Azure.Identity" Version="1.21.0" />
```

## Consequences

- Custom JWT auth is fully replaced by Entra ID — no more `JWT_SECRET` env var
- API keys remain for programmatic consumers that can't do OAuth
- All Azure service auth via Managed Identity — zero secrets in config
- Two-dimensional role model: platform roles (2) in Entra ID + project roles (4) in DB
- Segregation of duties enforced at the approval gate level — Reviewer cannot run, triggerer cannot approve their own execution
- Platform Admin override is explicit and audited — not a silent permission grant
- Role claim type must be explicitly set to `"roles"` or `[Authorize(Roles=...)]` silently fails
- SSE token exchange pattern carried forward but now backed by Redis `GETDEL`
- Entra ID app registration is IaC via Bicep `Microsoft.Graph` extension

### Principle Compliance

- **P18 Idempotency:** API key creation accepts `Idempotency-Key` — retrying returns the same key ID without generating a duplicate. Project member role assignments are upserts — same role twice is a no-op. SSE tokens are inherently non-idempotent (each call = new token), which is acceptable for ephemeral single-use tokens.
- **P19 Bounded Resource Usage:** Argon2id verification has a per-request timeout (3s). Failed login attempts rate-limited (5/min/IP, then 15-min lockout). SSE token cache has a hard cap on outstanding tokens per user (10). API key creation limited to 20 keys per user. Max 50 members per project.
- **P20 Version Everything:** Entra ID app registration manifest versioned in source control. API key format versioned via prefix convention (`awp_live_` for v1). Role model changes require documented migration.
