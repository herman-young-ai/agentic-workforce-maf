# ADR-006: Container Isolation for Agent Code Execution

**Status:** Accepted
**Date:** 2026-05-10
**Decision Makers:** Architecture team
**Research:** [R06-response-container-isolation.md](../098-research/R06-response-container-isolation.md)

---

## Context

Agentic Workforce Platform agents execute code as part of their tasks — file read/write, shell commands, git operations, builds, tests, static analysis. This code execution must be sandboxed so agents cannot affect the host platform, other projects, or access resources outside their scope. The current Python implementation supports local, worktree, and Docker container execution modes.

## Decision

**Container-first execution: ALL agent tools that interact with external systems, execute code, access filesystems, or make network calls run inside ACA Dynamic Sessions by default. Only internal platform query tools (DB reads via our own service layer) are exempt. ACI as fallback for long-lived projects exceeding 24 hours.**

### Container-First Principle (Principle 22)

The BFA prototype ran all tools in-process by default, relying on `FileScope` (software-level guards) for isolation. This was insufficient:

1. **Logical isolation is bypassable.** A bug in FileScope, a missing check in a new tool, or a prompt injection that steered tool arguments could bypass software guards. The container boundary is OS-level — it cannot be bypassed by application bugs.
2. **In-process network calls bypass egress controls.** Tools like web search (Tavily), SonarQube, and Snyk making HTTP calls from the Worker process have unrestricted network access. Containerized, they go through Azure Firewall egress allowlist.
3. **Defense-in-depth requires physical isolation.** For a dual-regulated bank, logical isolation alone is not defensible to regulators. Container isolation is the minimum standard.

**Two execution domains:**

| Domain | Where | What | Examples |
|--------|-------|------|---------|
| **Platform** (in-process) | Worker process | Internal DB queries via service interfaces. No network, no filesystem. | `project.get_info`, `project.get_pcd`, `project.get_team`, `project.approve_tasks` |
| **Sandbox** (containerized) | ACA Dynamic Sessions (Hyper-V) | **Everything else.** Any tool that touches the network, filesystem, or executes code. | `file.*`, `shell.*`, `web.*`, `git.*`, `security.*`, `research.*`, SonarQube, Snyk, Azure AI Search, SharePoint |

**New tools default to Sandbox.** Registering a tool as Platform requires explicit justification and an architecture test that verifies no outbound calls.

### Primary: Azure Container Apps Dynamic Sessions (Custom Container)

A custom-container session pool with a hardened base image containing the exact toolchain agents need:

- **Hyper-V isolation** per session — strongest isolation available on ACA
- **Sub-second startup** from pre-warmed pool (`readySessionInstances`)
- **Network egress disabled** by default (enable only with Azure Firewall allowlist)
- **Per-project session persistence** — same `identifier` routes all calls to the same warm session, preserving file system state across agent task invocations
- **Managed identity** for image pull from ACR — no stored credentials
- **24-hour max session duration** (`maxAlivePeriodInSeconds: 86400`)

### Base Image

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS base
RUN apt-get update && apt-get install -y \
    git curl jq ripgrep python3 python3-pip \
    nodejs npm \
    && rm -rf /var/lib/apt/lists/*
# Security: non-root user for agent execution
RUN groupadd -r agent && useradd -r -g agent -m -s /bin/bash agent
# Pre-installed tools for sandboxed execution (Principle 22)
# Web search, external API calls, file ops, shell all run here
# Sign and push to private ACR with digest pinning
USER agent
WORKDIR /workspace
```

### Per-Project Session ID

```csharp
// Generate server-side, NEVER from LLM or user input
string sessionId = Convert.ToHexString(
    HMACSHA256.HashData(
        _secretKey,
        Encoding.UTF8.GetBytes($"{projectId}:{tenantId}:{agentRunId}")));
```

### MAF Function Tool Pattern

Agents invoke code execution via function tools that delegate to the session:

```csharp
[Description("Execute a shell command in the project's isolated sandbox")]
public async Task<ShellResult> RunShellAsync(
    [Description("The shell command to execute")] string command)
{
    var token = await _credential.GetTokenAsync(
        new TokenRequestContext(["https://dynamicsessions.io/.default"]));

    var response = await _httpClient.PostAsJsonAsync(
        $"{_poolEndpoint}/code/execute?identifier={_sessionId}&api-version=2025-10-02-preview",
        new { properties = new { codeInputType = "InlineShell", executionType = "Synchronous",
                                 code = command } },
        cancellationToken);

    var result = await response.Content.ReadFromJsonAsync<SessionResult>();
    return new ShellResult(result.Stdout, result.Stderr, result.ExitCode);
}
```

Similar tools for `ReadFileAsync`, `WriteFileAsync`, `GitCloneAsync`, `GitDiffAsync`.

### Sandbox Tool Delegation Pattern (Principle 22)

With container-first execution, tools that previously ran in-process (web search, SonarQube, Azure SDK) now delegate to the Dynamic Session. Two delegation patterns:

**Pattern A: HTTP via helper script (external APIs)**

For tools calling third-party APIs (Tavily, Snyk, SonarQube), the base image includes a pre-installed `awp-http` helper script that makes authenticated HTTP requests. The Worker tool constructs the request, sends it as a session command, and parses the response:

```csharp
[Description("Search the web using Tavily")]
public async Task<SearchResult> SearchAsync(
    [Description("Search query")] string query)
{
    // API key injected as session env var at session creation (never from LLM)
    var command = $"awp-http POST https://api.tavily.com/search " +
                  $"--header 'Content-Type: application/json' " +
                  $"--body '{{\"query\":\"{EscapeJson(query)}\",\"api_key\":\"$TAVILY_API_KEY\"}}'";

    var result = await _session.ExecuteAsync(command, ct);
    return JsonSerializer.Deserialize<SearchResult>(result.Stdout)!;
}
```

**Pattern B: Token forwarding (Azure SDK)**

For tools calling Azure services (AI Search, SharePoint/Graph), the Worker acquires a short-lived token via its Managed Identity and passes it to the session as an environment variable:

```csharp
[Description("Search project documents via Azure AI Search")]
public async Task<SearchResult> SearchDocumentsAsync(
    [Description("Search query")] string query)
{
    // Worker acquires token with its UAMI, forwards to session
    var token = await _credential.GetTokenAsync(
        new TokenRequestContext(["https://search.azure.com/.default"]), ct);

    var command = $"awp-http GET '{_searchEndpoint}/indexes/{_indexName}/docs" +
                  $"?search={Uri.EscapeDataString(query)}&api-version=2024-07-01' " +
                  $"--header 'Authorization: Bearer {token.Token}'";

    var result = await _session.ExecuteAsync(command, ct);
    return JsonSerializer.Deserialize<SearchResult>(result.Stdout)!;
}
```

**Key design decisions:**
- **API keys are injected as session environment variables at creation time** — never passed through LLM output or tool arguments (prompt injection defence)
- **Azure tokens are acquired by the Worker** (which has the Managed Identity) and **forwarded as short-lived bearer tokens** to the session — the session never has its own identity for Azure services
- **The `awp-http` helper** is a minimal curl wrapper pre-installed in the base image that handles TLS, retries, and structured JSON output. It's not a framework — it's ~50 lines of shell script
- **The Worker constructs all URLs and auth headers** — the session just executes the HTTP call. Business logic stays in the backend (Principle 15)

### Fallback: ACI for Long-Lived/Durable Projects

For projects exceeding 24 hours or requiring durable Azure Files workspace:

- ACI container group with `OnFailure` restart policy
- Azure Files SMB mount per project (survives restarts)
- Confidential Containers (AMD SEV-SNP) for production cardholder data workloads
- Higher cold start (~30s-2min) but unlimited duration

### What We're NOT Using

| Option | Why Not |
|--------|---------|
| **MAF/Foundry Code Interpreter** | Python-only, no shell/git/build tools, no outbound network, 1-hour max, insufficient for real projects |
| **Docker-in-Docker on ACA** | Not supported — ACA blocks privileged containers platform-wide |
| **ACA Jobs** | Not Hyper-V isolated (same kernel as host pool); not a sandbox for untrusted code |
| **AKS Kata pods** | Strongest isolation but highest operational complexity; defer unless we already operate AKS |

## Network Security

```
┌─── ACA Internal Environment (Private Endpoint) ──────┐
│                                                        │
│  Agent App ──→ Dynamic Sessions Pool                  │
│  (Managed ID)   (Custom Container, Hyper-V)           │
│                  ├── EgressDisabled (default)          │
│                  └── EgressEnabled + Azure Firewall    │
│                      (explicit egress allowlist)       │
└────────────────────────────────────────────────────────┘
```

### Egress Allowlist (Azure Firewall)

With Principle 22 (Container-First), web search and external API tools now run inside Dynamic Sessions. The Azure Firewall allowlist must include their endpoints:

| Tool Category | Allowed Endpoints | Justification |
|---------------|-------------------|---------------|
| Web search | `api.tavily.com`, `api.search.brave.com`, `api.perplexity.ai` | Search failover chain |
| Security scanners | `sonarqube.internal.investec.com`, `api.snyk.io` | Vulnerability scanning |
| Compliance APIs | `compliance-api.internal.investec.com` | Internal compliance checks |
| Azure services | `*.search.windows.net`, `graph.microsoft.com` | AI Search, SharePoint/Graph |
| Package registries | `api.nuget.org`, `registry.npmjs.org`, `pypi.org` | Build and test tools |
| Git | `dev.azure.com`, `github.com` | Source code access |
| Cloud metadata | **BLOCKED** (`169.254.169.254`, `metadata.google.internal`) | Hardcoded deny — prevents credential theft |

**Empty allowlist = deny all egress** (Principle 14: Secure by Default). Each project can have a per-project egress override, but only additive — cannot remove denies or bypass metadata blocking.

## Aspire Integration

```csharp
// AppHost — provision session pool alongside the agent app
var sessionPool = builder.AddAzureContainerAppsSessionPool("sandbox")
    .WithCustomContainer(new SessionPoolCustomContainerConfig
    {
        Image = "myacr.azurecr.io/project-sandbox:latest",
        MaxConcurrentSessions = 50,
        ReadySessionInstances = 5,
        CooldownPeriodInSeconds = 300,
        EgressEnabled = false,
    });

var agentWorker = builder.AddProject<AgenticWorkforce_Worker>("worker")
    .WithReference(sessionPool);
```

## Audit

- `AppEnvSessionConsoleLogs` and `AppEnvSessionLifecycleLogs` → Log Analytics
- OpenTelemetry spans from MAF tool invocations correlated with session logs
- Every shell command, file write, and git operation logged with project/execution context

## Cost

- Code interpreter pool: $0.03/session-hour (billed in 1-hour increments)
- Custom container pool: ACA Dedicated plan pricing on E16 instances
- Cleanup: explicit `stopSession` call at project end stops billing immediately
- ACI fallback: per-second vCPU + GiB billing

## Consequences

- No first-party C# SDK for Dynamic Sessions data plane — must use `HttpClient` + REST API (or MCP client)
- Custom container pool requires workload-profiles-enabled ACA environment
- Regional availability for Dynamic Sessions is smaller than ACA's overall footprint — verify SA North
- Session identifier is security-critical — generate server-side with HMAC, never from LLM output
- 24-hour max session for Dynamic Sessions; longer projects route to ACI
- Base image must be maintained and updated for security patches

### Principle Compliance

- **P15 Backend Owns All Logic:** The agent application server owns all decisions about which commands are permitted and which files can be accessed. The sandbox is a pure execution environment — it does not make authorization decisions. File scope restrictions are enforced in the backend tool implementation before commands are sent to the session.
- **P16 Single Source of Truth:** The sandbox file system is ephemeral, not a source of truth. All artifacts produced must be extracted and persisted to the authoritative store (PostgreSQL + Blob) before the session ends. Session logs in Log Analytics are the authoritative audit record of container activity.
- **P17 Human Authority:** Humans can terminate any sandbox session immediately via the `stopSession` API. The kill switch halts all active sessions. When an agent requests network egress or elevated permissions, the request surfaces as a human approval gate.
- **P18 Idempotency:** `RunShellAsync` tool calls may be retried if the Durable Task activity replays. Tool implementations check execution logs for matching commands before re-executing. File writes use atomic write-and-rename patterns. Git operations check existing state before proceeding.
- **P20 Version Everything:** The sandbox base image is tagged with a semantic version and content hash — not `latest`. The Dynamic Sessions API version is pinned explicitly in client code. Image updates require explicit version bumps.
- **P22 Container-First Execution:** All tools that make network calls (web search, SonarQube, Snyk, Azure SDK integrations) or access the filesystem run in Dynamic Sessions. Only internal platform query tools (`project.*`) are exempt. This is the defining ADR for Principle 22.
