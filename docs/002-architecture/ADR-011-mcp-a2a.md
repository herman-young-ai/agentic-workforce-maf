# ADR-011: MCP and A2A Protocol Integration

**Status:** Accepted
**Date:** 2026-05-10
**Decision Makers:** Architecture team
**Research:** [R11-response-mcp-a2a.md](../098-research/R11-response-mcp-a2a.md)

---

## Context

Agentic Workforce Platform agents need to integrate with external tools (security scanners, code analysis, internal bank APIs, document stores, web search) and potentially expose agents for consumption by other bank platforms. MAF has native support for MCP (Model Context Protocol) and A2A (Agent-to-Agent protocol).

## Decision

**Native `AIFunction` tools for Azure SDK integrations + Containerised MCP servers for sandboxed tools + A2A for cross-team agent interop**

### Integration Pattern Per Tool

| Target | Pattern | Rationale |
|--------|---------|-----------|
| Bandit / Semgrep (security scanners) | **MCP server in container** (stdio or Streamable HTTP) | Process isolation, sandbox, reusable from VS Code/Copilot, blast radius contained |
| Safety / Snyk (dependency checking) | **Native `AIFunction`** wrapping REST API | Synchronous REST is trivial; `HttpClient` + Polly retry |
| SonarQube (code quality) | **Native `AIFunction`** wrapping REST API | Stable REST + OpenAPI; no MCP benefit |
| Internal compliance API (Entra ID) | **Native `AIFunction`** with `ManagedIdentityCredential` | Best Entra ID story; `BearerTokenPolicy` on `HttpClient` |
| SharePoint (playbooks, policies) | **Native `AIFunction`** wrapping Microsoft Graph SDK | Graph .NET SDK handles auth and throttling |
| Azure AI Search (knowledge base) | **Native `AIFunction`** wrapping `Azure.Search.Documents` | Full control over query construction |
| Git operations (sandboxed) | **MCP server in container** (or Dynamic Sessions) | Sandbox is the whole point; hard process boundary |
| Web search (Perplexity/Tavily/Brave) | **Native `AIFunction`** with failover chain | Failover, quota tracking, caching easier in C# |
| External compliance agent (other team) | **A2A proxy** (`A2AAgent`) | Cross-team boundary; agent discovery via Agent Card |

### MCP: Two Modes Available

**Local MCP Tools** (recommended for our use case):
- MCP client runs in our .NET process
- Works with ANY provider (Anthropic, OpenAI, Foundry, Ollama)
- Tools surface as `McpClientTool : AIFunction` — plug straight into `ChatClientAgent`
- C# SDK: `ModelContextProtocol`, `ModelContextProtocol.Core`, `ModelContextProtocol.AspNetCore`

```csharp
// Connect to security scanner MCP server
await using var mcp = await McpClientFactory.CreateAsync(new StdioClientTransport(new()
{
    Name = "SecurityScanners",
    Command = "dotnet",
    Arguments = ["run", "--project", "./SecurityScanMcpServer.csproj"]
}));

var mcpTools = await mcp.ListToolsAsync();

AIAgent agent = anthropicClient.AsAIAgent(
    model: "claude-sonnet-4-6",
    instructions: "You triage security findings. Use tools instead of guessing.",
    tools: [.. mcpTools.Cast<AITool>()]);
```

**Hosted MCP Tools** (Foundry-managed):
- Foundry acts as MCP client, manages the connection
- Only for Foundry-backed agents
- Built-in approval gating (`MCPApproval("always" | "never")`)
- Best when you want Foundry to handle MCP socket and credentials

### Custom MCP Server Skeleton (Security Scanner)

```csharp
// Program.cs — stdio MCP server
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();

// SecurityScanTools.cs
[McpServerToolType]
public static class SecurityScanTools
{
    [McpServerTool, Description("Run Semgrep scan and return JSON findings.")]
    public static async Task<string> RunSemgrep(
        [Description("Path to scan")] string path,
        [Description("Ruleset")] string? ruleset = "p/ci")
    {
        var psi = new ProcessStartInfo("docker",
            $"run --rm -v {path}:/src returntocorp/semgrep semgrep --config {ruleset} --json /src")
        { RedirectStandardOutput = true, UseShellExecute = false };
        using var proc = Process.Start(psi)!;
        return await proc.StandardOutput.ReadToEndAsync();
    }
}
```

For HTTP transport: swap to `WithHttpTransport()` + `MapMcp("/api/mcp").RequireAuthorization()`.

### Tool Approval (HITL)

Three mechanisms:
1. **`ApprovalRequiredAIFunction`** — wraps any `AIFunction`; run returns `FunctionApprovalRequestContent` instead of executing
2. **Hosted MCP `MCPApproval("always")`** — Foundry surfaces approval events
3. **Custom `DelegatingChatClient` middleware** — intercept tool calls before `UseFunctionInvocation()` for fine-grained policy (read auto-approve, write requires human)

### Web Search: Custom Failover Tool

```csharp
public sealed class WebSearchTool
{
    private readonly IReadOnlyList<IWebSearchProvider> _chain; // Tavily → Brave → Perplexity

    [Description("Search the public web. Returns up to N results.")]
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        [Description("The search query")] string query,
        [Description("Max results (1-10)")] int maxResults = 5,
        CancellationToken ct = default)
    {
        foreach (var provider in _chain)
        {
            try
            {
                var hits = await provider.SearchAsync(query, maxResults, ct);
                if (hits.Count > 0) return hits;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _log.LogWarning(ex, "Provider {P} failed; falling back.", provider.Name);
            }
        }
        return [];
    }
}

// Register as AIFunction
var searchTool = AIFunctionFactory.Create(webSearchTool.SearchAsync);
```

Foundry's hosted Web Search (`WebSearchToolDefinition`, Bing-grounded) is available but: data leaves Azure compliance boundary, doesn't respect Private Endpoints, and Microsoft DPA doesn't apply.

### A2A: Exposing and Consuming Agents

**Expose our agents:**
```csharp
builder.AddA2AServer("security-reviewer");
app.MapA2AHttpJson("security-reviewer", "/a2a/security-reviewer");
app.MapWellKnownAgentCard(new AgentCard { Name = "SecurityReviewer", ... });
```

**Consume external agents:**
```csharp
A2ACardResolver resolver = new(new Uri("https://compliance.partner-team.internal"));
AIAgent complianceAgent = await resolver.GetAIAgentAsync();
// Use like any local agent — works in workflows, as tools, streaming
var result = await complianceAgent.RunAsync("Review this for SOX compliance.");
```

**A2A in workflows (handoff):**
```csharp
Workflow workflow = AgentWorkflowBuilder
    .CreateHandoffBuilderWith(localAgent)
    .WithHandoffs(localAgent, remoteA2AAgent)
    .Build();
```

### A2A Maturity

- **Protocol (A2A v1)**: stable, Linux Foundation governed
- **MAF packages**: still preview (`1.0.0-preview.251001.1`) — pin versions
- **Auth**: standard HTTP (Bearer/OAuth2/Entra ID) declared in Agent Card `securitySchemes`
- **Streaming**: supported via `message/stream` SSE + push notifications for long tasks
- Production-usable for internal microservices; best at **organisational/team boundaries**

### Performance Overhead

| Approach | Latency |
|----------|---------|
| Native `AIFunction` | In-process method call (~0 overhead) |
| stdio MCP (same machine) | Sub-millisecond per call after warmup |
| HTTP/Streamable HTTP MCP (LAN) | 1-10 ms intra-cluster |
| Foundry Hosted MCP | Extra hop (client → Foundry → MCP → back); acceptable for non-hot-path |
| A2A (HTTP) | Full HTTP round-trip; network latency dependent |

## Key Packages

```xml
<!-- MCP -->
<PackageReference Include="ModelContextProtocol" Version="latest-preview" />
<PackageReference Include="ModelContextProtocol.AspNetCore" Version="latest-preview" />

<!-- A2A -->
<PackageReference Include="Microsoft.Agents.AI.A2A" Version="1.0.0-preview.251001.1" />
<PackageReference Include="Microsoft.Agents.AI.Hosting.A2A.AspNetCore" Version="1.0.0-preview.251007.1" />
<PackageReference Include="A2A.AspNetCore" Version="latest-preview" />
```

## Consequences

- MCP C# SDK is co-maintained by Microsoft and Anthropic — API stabilising but still preview-tagged; pin versions
- A2A MAF packages are preview — isolate behind a thin abstraction at the boundary
- Local MCP tools work with all providers (Anthropic, OpenAI, etc.) because they surface as standard `AIFunction`
- Anthropic provider in MAF does NOT yet support hosted MCP / code interpreter / web search — surface these as `AIFunction` wrappers
- MCP servers in containers need Aspire orchestration for local dev; in production deploy as sidecar or separate Container App
- A2A shines at team boundaries; for tightly-coupled internal services, plain REST + Entra ID JWTs is simpler
- Foundry Web Search (Bing) has data residency implications — data leaves Azure compliance boundary
- MCP tool approval via `ApprovalRequiredAIFunction` or middleware is the HITL surface; built-in approval router not yet shipped

### Principle Compliance

- **P14 Secure by Default:** MCP servers and A2A endpoints default to deny-all tool access. Each tool must be explicitly allowlisted per agent. An agent with an empty tool manifest has zero tools available. No auto-discovery of MCP servers or A2A agents.
- **P15 Backend Owns All Logic:** Tool invocation decisions, approval routing, and failover logic all run server-side. The client only renders approval prompts and displays results.
- **P16 Single Source of Truth:** The tool catalog/registry is the single authoritative source for which tools exist and their configurations. No tool definitions scattered across agent configurations without a central registry reference.
- **P18 Idempotency:** MCP tool calls (especially write operations like Git commits) are idempotent or explicitly marked as non-idempotent. Duplicate invocations of the same tool call do not produce additional side effects.
- **P19 Bounded Resource Usage:** Explicit timeouts per MCP server connection, max concurrent MCP sessions, max payload sizes for tool inputs/outputs, and A2A request timeout limits. Web search failover chain has per-provider rate limits.
- **P20 Version Everything:** Agent Cards (A2A) and MCP tool schemas are versioned so changes don't break consuming agents. Custom MCP servers have a versioning strategy.
- **P21 Explicit Over Implicit:** Tool registration is fully explicit — every integration declared in configuration with its transport, auth, and allowlist. No auto-discovery.
