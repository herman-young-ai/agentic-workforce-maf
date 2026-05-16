# R17: Security Architecture — Authentication, Authorisation, Access Management, and Threat Mitigation

## Prompt for claude.ai

---

You are a senior security architect designing the complete security architecture for the **Agentic Workforce Platform** — an AI agent orchestration system for a dual-regulated bank (Investec — FCA/PRA in UK, SARB/PA in South Africa), built in C# / ASP.NET Core 9, deployed on Azure Container Apps.

This platform runs AI agents that read/write files, execute shell commands, call external APIs, make financial decisions, and produce reports used by regulators. Security is not a feature — it's a survival requirement.

### What the Platform Does

- Deploys teams of AI agents (Claude, GPT) to execute tasks within projects
- Agents can: read/write files, execute shell commands in sandboxed containers, search the web, query databases, call internal bank APIs
- Humans create projects, design workflows, approve agent outputs, upload documents for analysis
- The system accumulates knowledge (learnings, decisions, context) that shapes future agent behaviour
- All actions are audited with 7-year immutable retention for regulatory compliance
- Data spans two jurisdictions: South Africa (POPIA) and United Kingdom (UK GDPR, FCA, PRA)

### Current Authentication Design

**Dual-scheme auth:**
- Entra ID JWT Bearer (for SPA and CLI users via MSAL)
- API Key (`X-Api-Key` header, HMAC-SHA256 hashed with per-key salt) for programmatic consumers
- Both accepted on the same `[Authorize]` via `AddPolicyScheme` with `ForwardDefaultSelector`

**Platform roles** (Entra ID App Roles): `platform_admin`, `member`
**Project roles** (per-project DB table): `owner`, `operator`, `reviewer`, `viewer`

**Workload identity:** User-Assigned Managed Identity on Container Apps — no stored secrets for Azure service auth (Foundry, Key Vault, Storage, ACR, Dynamic Sessions).

**SSE auth:** Short-lived Redis token (30s TTL, single-use via GETDEL) exchanged from a JWT for EventSource connections that can't set auth headers.

### Current Security Measures

- Entra ID for human auth, Managed Identity for service-to-service
- Role-based access with hierarchical platform roles + per-project roles
- Segregation of duties: `triggered_by != approved_by` on approval gates
- ACA Dynamic Sessions with Hyper-V isolation for agent code execution
- Network egress disabled by default on sandboxes (Azure Firewall allowlist)
- Audit pipeline: every LLM call and tool invocation recorded to immutable Blob WORM + Eventhouse
- SHA-256 hash chain on audit records for tamper detection
- PCD restricted paths (principles, guardrails) — human-only write

### What I Need You to Design

Perform a comprehensive security review and design covering ALL of the following areas. For each area, identify threats, define controls, and provide implementation guidance.

---

**AREA 1: API Security**

Design the API security layer:

1. **Rate limiting** — per-user, per-API-key, per-endpoint-group. What limits? How enforced? What happens when exceeded?
2. **Input validation** — request body validation, path parameter validation, query parameter bounds. Max body size, max header size, max URL length.
3. **Output sanitisation** — what gets filtered from API responses? PII in error messages? Stack traces? Internal IDs?
4. **CORS** — configuration for production (SPA origin only) vs development (localhost). What methods, headers, credentials?
5. **Security headers** — complete list of HTTP security headers to set (HSTS, CST, X-Frame-Options, Referrer-Policy, Permissions-Policy, etc.)
6. **Request logging** — what to log, what to redact (auth tokens, API keys, PII). Correlation IDs across requests.
7. **Idempotency** — how idempotency keys work, where cached (Redis), TTL, what happens on replay.
8. **API versioning** — how breaking changes are handled without breaking existing consumers.

**AREA 2: Authentication Deep Dive**

Go deeper than our current design:

1. **JWT validation** — what claims to validate beyond standard (aud, iss, exp)? Signature algorithm pinning? Key rotation handling?
2. **API key lifecycle** — creation, rotation (overlapping validity), revocation, expiry monitoring, automated alerts for expiring keys. Hash algorithm choice (HMAC-SHA256 vs Argon2id). Prefix format for identification.
3. **SSE token** — is 30s TTL sufficient? What about race conditions? Should it be bound to a specific resource (project_id)?
4. **Session management** — how to handle JWT refresh, session timeout, concurrent sessions, forced logout (admin revokes access).
5. **Service-to-service auth** — Managed Identity is set, but how does the Worker authenticate to the BFF API? How does the BFF verify the Worker's identity?
6. **Impersonation** — can a Platform Admin act on behalf of a user? If so, how is it logged?
7. **Token theft mitigation** — what if a JWT or API key is stolen? Detection, response, damage limitation.

**AREA 3: Authorisation Deep Dive**

Go deeper than our current role model:

1. **Resource-level authorisation** — how exactly does the `ProjectRoleAuthorizationHandler` work? What's the lookup pattern? Caching strategy? Performance at scale?
2. **Field-level authorisation** — should viewers see cost data? Should operators see other operators' API keys? Should reviewers see the full PCD?
3. **Temporal authorisation** — can access be time-bound? (e.g., reviewer access expires after 30 days)
4. **Delegation** — can an owner delegate their owner permissions temporarily (e.g., on leave)?
5. **Audit of authorisation decisions** — every deny should be logged. Every admin override should be logged with reason.
6. **Cross-project data leakage** — how to prevent agent context from one project leaking into another via shared platform learnings, shared agent catalog, or shared model context.
7. **Privilege escalation paths** — can an operator modify a workflow to skip approval gates? Can an agent modify its own constraints? Can a user create a project with higher privileges than they hold?

**AREA 4: Agent Security (the AI-specific threats)**

This is the unique challenge — agents are autonomous actors with tool access:

1. **Prompt injection** — how to defend against:
   - Direct injection (user crafts malicious input)
   - Indirect injection (uploaded document contains instructions that override agent behaviour)
   - Learning poisoning (agent extracts a malicious "learning" that shapes future agent behaviour)
   - PCD poisoning (agent writes to writable PCD sections to influence future runs)
2. **Agent tool abuse** — how to prevent:
   - An agent reading files outside its scope (FileScope/path restrictions)
   - An agent executing destructive shell commands (command allowlist/blocklist?)
   - An agent exfiltrating data via web search queries or tool outputs
   - An agent calling internal APIs it shouldn't have access to
3. **Agent output validation** — how to validate:
   - Agent-generated code before execution (static analysis? sandbox-only?)
   - Agent-generated PCD updates before application (path allowlist? diff review?)
   - Agent-extracted learnings before storage (quality filter? confidence threshold?)
4. **Agent identity and accountability** — tracing every action to a specific agent, execution, and task. Can an agent impersonate another agent?
5. **Model safety** — content filtering for agent inputs/outputs. Azure AI Content Safety integration. What categories to filter? What to do on violation?
6. **Guardrail enforcement** — how are PCD guardrails actually enforced at runtime? Are they just prompt text, or is there a programmatic enforcement layer?

**AREA 5: Data Protection**

1. **Data classification** — what data classification scheme? What classification per entity? (PCD, learnings, artifacts, documents, audit records, LLM call content)
2. **Encryption at rest** — Azure-managed vs customer-managed keys (CMK). Which resources get CMK? Key rotation schedule.
3. **Encryption in transit** — TLS 1.2+ everywhere. Certificate management. Internal service communication encryption.
4. **PII handling** — where does PII enter the system? (uploaded documents, chat messages, agent outputs). How is it detected, classified, and protected? PII in LLM prompts sent to Foundry (data leaves Azure for Claude).
5. **Data residency** — SA data stays in SA North, UK data stays in UK South. How enforced per project? What about shared platform learnings that cross jurisdictions?
6. **Data retention** — 7 years for audit. What about project data after archival? PCD? Learnings? Documents? What's the retention policy per entity type?
7. **Right to erasure** — if a data subject requests deletion under POPIA/GDPR, how do we handle it when audit records are in WORM storage?
8. **Data minimisation** — what data do we NOT need to store? Can we hash/tokenise PII in learnings?

**AREA 6: Network Security**

1. **Container Apps Environment** — internal vs external ingress. Private endpoints. VNet integration.
2. **Dynamic Sessions sandbox** — network egress control. Azure Firewall FQDN allowlist. What endpoints are allowed?
3. **Database** — private endpoint only. No public access. Firewall rules.
4. **Redis** — private endpoint. TLS. Authentication.
5. **Blob Storage** — private endpoint. No public access. Firewall rules.
6. **Foundry/model endpoints** — how secured? Private endpoint or public? IP restrictions?
7. **Service-to-service** — BFF ↔ Worker communication. Through Redis? Direct HTTP? How authenticated?

**AREA 7: Secrets Management**

1. **Key Vault** — what goes in Key Vault vs what's in Aspire config? Access via Managed Identity only?
2. **Secret rotation** — which secrets rotate? How often? Automated or manual?
3. **Development secrets** — how do developers get secrets locally? User Secrets? .env files? Azure Key Vault references?
4. **CI/CD secrets** — how does the pipeline access secrets? Service connection? Pipeline variables?

**AREA 8: Operational Security**

1. **Emergency stop** — what exactly happens? Which tasks are cancelled? Which are allowed to complete? How is it triggered and by whom?
2. **Break-glass access** — if the platform admin is unavailable, how does someone get emergency access?
3. **Incident response** — what's the response plan for: compromised API key, prompt injection attack detected, data breach, agent producing harmful content?
4. **Penetration testing** — what should be tested? Agent prompt injection, API auth bypass, privilege escalation, data exfiltration via agent tools.
5. **Security monitoring** — what alerts should fire? Failed auth attempts, unusual agent behaviour, cost anomalies, hash chain gaps, bulk data access.

**AREA 9: Compliance Controls**

1. **FCA/PRA requirements** — what specific controls satisfy FG16/5 (cloud outsourcing), SS1/23 (model risk management), PS21/3 (operational resilience)?
2. **SARB/PA requirements** — what controls satisfy the Joint Standard on Outsourcing, POPIA s72 (cross-border transfers)?
3. **Privileged access review** — the bank's Access Management Standard requires twice-yearly review. How is this implemented for platform_admin roles and API keys?
4. **Change management** — how are agent prompt changes, model version changes, and workflow changes controlled and approved?
5. **Third-party risk** — Anthropic and Microsoft are third parties. What controls are in place for their service disruption, data handling, and model behaviour changes?

### Output Format

For each area, provide:

1. **Threat table** — specific threats with likelihood/impact rating (H/M/L)
2. **Controls** — what control mitigates each threat, with implementation detail
3. **C# code sketches** where relevant (middleware, handlers, validators) — 10-20 lines each
4. **Configuration** — specific values (rate limits, TTLs, header values, firewall rules)
5. **Gaps** — anything we SHOULD do but CAN'T with the current architecture

Produce a **Security Controls Matrix** at the end: a single table mapping every control to its threat, implementation, owner, and verification method.

Keep total response under 6000 words. Tables and code preferred over prose.

---

## After Research

Save claude.ai's response as: `docs/098-research/R17-response-security-design.md`
