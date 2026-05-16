# R07: Identity, Auth, and RBAC — Entra ID + Workload Identity

## Prompt for claude.ai

---

I am building an AI agent orchestration platform in C# / ASP.NET Core on Azure Container Apps for Investec bank. The platform needs authentication for human users, API consumers, and agent workloads. Currently the Python implementation uses custom JWT + API key auth. We want to migrate to Entra ID to align with the bank's identity standards.

Give me a **concise architectural pattern** with code sketches. Not theory — show me how to wire it.

### Our Auth Requirements

**Human users (interactive):**
- Bank employees access via React SPA and CLI
- Must use Entra ID (Azure AD) SSO — the bank already has a tenant
- Role-based access: viewer, user, admin, sysadmin (hierarchical)
- React SPA uses MSAL.js for token acquisition

**Programmatic consumers (API keys):**
- External systems and CI/CD call the REST API
- Need API key auth alongside Entra ID (some consumers can't do OAuth)
- API keys scoped to specific operations (read-only, execute, admin)
- Keys are hashed + salted in DB, prefixed for identification

**Agent workloads (service-to-service):**
- Agent runtime processes call Azure AI Foundry, Key Vault, Storage
- Must use Managed Identity or Workload Identity (no API keys for Azure services)
- Each agent execution should have scoped permissions (not god-mode)

**SSE/streaming auth:**
- SSE connections can't send auth headers after initial request
- Current pattern: exchange JWT for a short-lived SSE token (stored in Redis, 30s TTL, single-use)
- Need equivalent in the Entra ID world

**Current role model:**

| Role | Level | Permissions |
|---|---|---|
| viewer | 1 | Read missions, sessions, artifacts |
| user | 2 | + Create/run missions, chat with agents |
| admin | 3 | + Manage catalog, users, platform config |
| sysadmin | 4 | + DB operations, emergency stop, system accounts |

### Questions — Answer Each with Code

**Q1: ASP.NET Core + Entra ID setup**
Show the `Program.cs` auth configuration for:
- Entra ID JWT Bearer (for SPA and API consumers with OAuth tokens)
- API Key fallback (custom auth handler for `X-API-Key` header)
- Both schemes evaluated — if either succeeds, request is authenticated

**Q2: App Roles in Entra ID**
Show how to define the four roles (viewer, user, admin, sysadmin) as Entra ID App Roles in the app registration manifest, and how to check them in ASP.NET Core `[Authorize]` attributes.

**Q3: Workload Identity for Container Apps**
Show the pattern for:
- Container App has a User-Assigned Managed Identity
- That identity authenticates to Azure AI Foundry (for LLM calls)
- That identity authenticates to Key Vault (for secrets)
- No API keys stored in config — everything via DefaultAzureCredential

**Q4: SSE token exchange**
Show the pattern for exchanging an Entra ID access token for a short-lived SSE connection token, compatible with `EventSource` in the browser (which can't set Authorization headers — must use query param or cookie).

**Q5: Per-mission authorization**
Our missions have members with roles (owner, maintainer, viewer). Show the pattern for an authorization handler that checks:
- User is a member of the mission they're accessing, OR
- User is an admin/sysadmin (can access all missions)

**Q6: Dual auth scheme (Entra ID + API Key)**
Show the complete `AuthenticationBuilder` configuration that supports both schemes, with a policy that accepts either.

### Output Format

For each Q1-Q6:
- 10-20 line C# code sketch
- One paragraph on the pattern

Then:
- **Entra ID App Registration checklist**: what to configure in the Azure portal for this setup
- **Bicep snippet**: the Entra ID app registration + Container App managed identity in IaC

Keep total response under 2500 words. Code preferred over prose.

---

## After Research

Save claude.ai's response as: `docs/098-research/R07-response-identity-auth.md`
