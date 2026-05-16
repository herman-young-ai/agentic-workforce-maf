# Microsoft Agent Framework (MAF) Integration Capabilities — MCP, A2A, Tools, and Web Search (May 2026)

## TL;DR

- **MAF 1.0 (GA April 3, 2026) ships stable agent, workflow, MCP, and provider APIs in `Microsoft.Agents.AI` (current 1.5.0)**, but **A2A in MAF (`Microsoft.Agents.AI.A2A` and `…Hosting.A2A.AspNetCore`) is still on preview package versions even though the underlying A2A v1 protocol/SDK are stable** — production-usable, but pin versions and expect minor surface churn.
- **Two distinct MCP modes ship for C#/Azure**: *Hosted MCP Tools* (Foundry executes the MCP client server-side via `MCPToolDefinition` + `MCPToolResource`/`MCPApproval`, with built-in approval gating) and *Local MCP Tools* (your process is the MCP client, using the official `ModelContextProtocol` C# SDK with `StdioClientTransport`/Streamable HTTP/SSE/WebSocket; tools surface as `AITool`/`AIFunction` and work with any provider that supports function tools — Azure OpenAI, OpenAI, Anthropic, Bedrock, Gemini, Ollama, Foundry).
- **For your decision matrix, the production-correct defaults on Azure are**: native `AIFunction`/`AIFunctionFactory.Create` tools wrapping Azure SDKs for Azure AI Search, Microsoft Graph/SharePoint, internal REST + Entra ID, SonarQube, Snyk, and web-search failover; *containerised MCP servers* for security scanners (Bandit, Semgrep), Safety, and sandboxed git; and the **Foundry hosted Web Search tool (`WebSearchToolDefinition`, Bing-grounded)** when you want Microsoft to run web search for you, otherwise a custom failover function tool over Tavily/Brave/Perplexity.

---

## Key Findings

### Release/maturity snapshot (May 2026)

| Component | Package | Status |
|---|---|---|
| Core agent framework | `Microsoft.Agents.AI` (1.0 GA, current 1.5.0) | **Stable / GA** |
| Workflows (graph, group chat, handoff, sequential, concurrent, Magentic) | `Microsoft.Agents.AI` + `Microsoft.Agents.AI.Workflows` | **Stable / GA** |
| Provider connectors (Foundry, Azure OpenAI, OpenAI, Anthropic, Bedrock, Gemini, Ollama) | `Microsoft.Agents.AI.Foundry`, `…OpenAI`, `…Anthropic`, etc. | **Stable / GA** (Bedrock today is via OpenAI-compatible endpoint; native client is a tracked feature request) |
| MCP client (Local MCP Tools) | `ModelContextProtocol`, `ModelContextProtocol.Core`, `ModelContextProtocol.AspNetCore` (C# SDK maintained jointly with Microsoft) | API stabilising; recent versions still preview-tagged in places — pin versions |
| MCP hosted tool path (Foundry) | `Microsoft.Agents.AI.Foundry` + `MCPToolDefinition`/`MCPToolResource` | Public preview / GA-track on Foundry; Foundry Toolboxes still preview |
| A2A protocol | A2A v1.0 (open spec; Linux Foundation; technical steering committee includes Microsoft, Google, AWS, Cisco, IBM, Salesforce, SAP, ServiceNow) | **Protocol stable** |
| A2A in MAF (client) | `Microsoft.Agents.AI.A2A` (e.g. `1.0.0-preview.251001.1`) | **Preview**, but built on stable v1 SDK |
| A2A in MAF (server hosting) | `Microsoft.Agents.AI.Hosting.A2A` + `…Hosting.A2A.AspNetCore` (e.g. `1.0.0-preview.251007.1`) | **Preview** |
| Foundry Hosted Agents (containerised agents on Foundry Agent Service) | Hosted Agents (preview) | Preview, GA in flight |
| DevUI | preview | Preview, .NET parity in flight |

The 1.0 announcement explicitly listed A2A as "**A2A 1.0 support coming soon**" alongside fully GA MCP, and as of the May 2026 doc set the .NET A2A packages remain on preview semver. Microsoft has separately published a v1 update post (*"A2A v1 Is Here"*) confirming the .NET libraries were updated to the v1 SDK with minimal breaking changes.

---

## Section 1 — MCP in MAF

### 1.1 Hosted MCP Tools vs Local MCP Tools

Two architecturally distinct integrations, both documented under *Microsoft Learn → agent-framework → agents → tools*:

**Hosted MCP Tools** (`learn.microsoft.com/agent-framework/agents/tools/hosted-mcp-tools`)
- The **MCP client lives inside the Microsoft Foundry Agent Service**. Your code only declares the tool; Foundry connects to the upstream MCP server, performs the JSON-RPC handshake, and surfaces results to the model.
- Bound to **Foundry-backed agents** (Persistent/Declarative agents created via `AIProjectClient.AgentAdministrationClient`).
- Surface in C#: `MCPToolDefinition(serverLabel, serverUrl)`, optional `AllowedTools`, plus a per-run `MCPToolResource { RequireApproval = new MCPApproval("never" | "always") }` passed via `ChatClientAgentRunOptions.ChatOptions.RawRepresentationFactory`.
- Authentication options on Foundry: API key, OAuth, OAuth identity passthrough (on-behalf-of user), Entra ID via project connection, or unauthenticated.
- Foundry **Toolboxes** (preview) bundle web search, code interpreter, file search, Azure AI Search, MCP servers, OpenAPI tools, and A2A connections behind a single MCP-compatible endpoint that any MCP-enabled runtime (MAF, LangGraph, Copilot SDK, etc.) can consume.
- Best for: "I want minimal infra, server-side credential management, and Foundry to handle the MCP socket."

**Local MCP Tools** (`learn.microsoft.com/agent-framework/agents/tools/local-mcp-tools`)
- **The MCP client runs inside your .NET process.** Your agent talks to MCP servers directly using the official MCP C# SDK (`ModelContextProtocol`).
- Works with **any provider that supports function tools** — Azure OpenAI, OpenAI, Anthropic Claude, Bedrock-via-OpenAI-compat, Gemini, Ollama, Foundry, or even a custom `IChatClient`.
- Tools returned by `ListToolsAsync()` are `McpClientTool` instances that inherit from `AIFunction`, so they are passed straight to `AsAIAgent(..., tools: [..])` and used by the standard ChatClientAgent function-call loop.
- Best for: separation of concerns (tool teams ship MCP servers independently of agent teams), running the same MCP server across MAF, VS Code Copilot, Claude Desktop, etc., and full control over transport and auth.

### 1.2 Provider × MCP support matrix

The MAF tool docs (Tools Overview) explicitly note that **Local MCP tools work with any provider that supports function tools**, because they are surfaced as standard `AIFunction` objects through Microsoft.Extensions.AI. Hosted MCP tools require a server-side runtime that knows how to act as MCP client.

| Provider | Local MCP (via `ModelContextProtocol` SDK) | Hosted MCP (server-side execution) |
|---|---|---|
| Microsoft Foundry | ✅ | ✅ (`MCPToolDefinition` + `MCPToolResource`) |
| Azure OpenAI (Chat Completions, Responses) | ✅ | Hosted MCP only via Foundry; Responses API has its own MCP tool surface |
| OpenAI (Chat Completions, Responses, Assistants) | ✅ | OpenAI Responses MCP tool — supported via the same provider-specific raw representation pattern |
| Anthropic Claude (`Microsoft.Agents.AI.Anthropic`) | ✅ | ❌ (no Anthropic-side hosted MCP exposed by MAF) |
| Amazon Bedrock | ✅ (currently routed via OpenAI-compat endpoint; native client is a tracked GitHub feature request, issue #2524) | ❌ |
| Google Gemini | ✅ | ❌ |
| Ollama / Foundry Local / DMR | ✅ | ❌ |

### 1.3 Building a custom MCP server in C# (security-scanner skeleton)

Microsoft and Anthropic co-maintain the official C# SDK (`modelcontextprotocol/csharp-sdk`). Three NuGets:

- `ModelContextProtocol.Core` — minimal client/low-level server
- `ModelContextProtocol` — main package + DI/hosting helpers
- `ModelContextProtocol.AspNetCore` — HTTP/SSE/Streamable HTTP server

Minimal stdio MCP server wrapping a security scanner (e.g. Semgrep) — production pattern follows the Microsoft .NET blog *Build a Model Context Protocol (MCP) server in C#*:

```csharp
// Program.cs  (dotnet add package ModelContextProtocol --prerelease
//              dotnet add package Microsoft.Extensions.Hosting)
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = Host.CreateApplicationBuilder(args);

// IMPORTANT for stdio transport: never write to stdout — it corrupts JSON-RPC.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();   // discovers [McpServerToolType] / [McpServerTool]

await builder.Build().RunAsync();
```

```csharp
// SecurityScanTools.cs
using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;

[McpServerToolType]
public static class SecurityScanTools
{
    [McpServerTool, Description("Run a Semgrep scan against a directory and return JSON findings.")]
    public static async Task<string> RunSemgrep(
        [Description("Absolute path to the source tree to scan.")] string path,
        [Description("Optional Semgrep ruleset, e.g. 'p/owasp-top-ten'.")] string? ruleset = "p/ci")
    {
        // Always shell out into a sandboxed container in production
        // (e.g. docker run --rm -v {path}:/src returntocorp/semgrep ...).
        var psi = new ProcessStartInfo("docker", $"run --rm -v {path}:/src returntocorp/semgrep semgrep --config {ruleset} --json /src")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var proc = Process.Start(psi)!;
        string json = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return json;
    }
}
```

For a remote/Streamable-HTTP MCP server, swap the host:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)   // stateless if no sampling/elicitation
    .WithToolsFromAssembly();
var app = builder.Build();
app.MapMcp("/api/mcp")
   .RequireAuthorization();   // JwtBearer + Entra ID is supported
app.Run();
```

### 1.4 Registering a local MCP server with a MAF agent (C#)

Per `agent-framework/agents/tools/local-mcp-tools`:

```csharp
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using ModelContextProtocol.Client;

// 1. Connect MCP client to your scanner server (stdio).
await using var mcp = await McpClientFactory.CreateAsync(new StdioClientTransport(new()
{
    Name      = "SecurityScanners",
    Command   = "dotnet",
    Arguments = ["run", "--project", "./SecurityScanMcpServer.csproj"]
}));

// 2. Pull tool list (each McpClientTool : AIFunction).
var mcpTools = await mcp.ListToolsAsync();

// 3. Plug the tools into a standard AIAgent.
AIAgent agent = new AIProjectClient(
        new Uri(Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")!),
        new DefaultAzureCredential())   // use ManagedIdentityCredential in prod
    .AsAIAgent(
        model: "gpt-4o-mini",
        instructions: "You triage security findings. Use tools instead of guessing.",
        tools: [.. mcpTools.Cast<AITool>()]);

Console.WriteLine(await agent.RunAsync("Scan /src/repo for OWASP Top 10 findings."));
```

The same pattern works with `OpenAIClient`, `AzureOpenAIClient`, `AnthropicClient`, etc. — only the chat-client construction changes. For HTTP-based servers, swap `StdioClientTransport` for `SseClientTransport` (legacy) or the Streamable HTTP transport.

### 1.5 Process/container isolation and transports

Yes — the MCP server runs in a **separate process and may run in a separate container**. The MAF docs and the Microsoft sample *Interview Coach* demonstrate this explicitly: a Python MarkItDown MCP server runs in its own Aspire-orchestrated container while the .NET agent talks to it over MCP.

Transports supported by the C# SDK and MAF:

- **stdio** — `StdioClientTransport` / `StdioServerTransport`. Process-per-connection; great for local dev or when you launch one container per agent run (`docker run -i --rm mcp/duckduckgo`).
- **HTTP + SSE** (legacy) — `SseClientTransport` / `WithHttpTransport()` + `MapMcp()`.
- **Streamable HTTP** — current preferred remote transport in `ModelContextProtocol.AspNetCore`; supports `Mcp-Session-Id` header, optional stateless mode, and Streamable HTTP responses suitable for Azure App Service / Container Apps / Functions.
- **WebSocket** — supported in Python (`MCPWebsocketTool`); the .NET tooling primarily emphasises stdio + Streamable HTTP for production.

Foundry custom MCP servers can also be hosted on Azure Functions (HTTP webhook MCP endpoint at `/runtime/webhooks/mcp`) and on Azure Container Apps (private MCP requires Standard Agent Setup with VNet injection).

### 1.6 Approval / human-in-the-loop for MCP tools

Two complementary mechanisms ship in MAF 1.0:

1. **Per-tool approval flag** — wrap any `AIFunction` (including `McpClientTool`) in `ApprovalRequiredAIFunction` (C#) or use `approval_mode="always_require"` (Python). When the agent decides to call the tool, the run completes returning a `FunctionApprovalRequestContent` instead of the final answer; the caller responds with `FunctionApprovalResponseContent` to continue. This is the supported HITL surface (docs: *Using function tools with human in the loop approvals*).
2. **Hosted MCP approval** — for Foundry-hosted MCP, set `MCPApproval("always")` (or `"never"`) on `MCPToolResource`. With `"always"`, every MCP tool invocation surfaces a `ToolApprovalRequestContent` event that the workflow `RequestPort` mechanism can route to a human reviewer.
3. **Custom middleware** — the `DelegatingChatClient` pattern from `Microsoft.Extensions.AI` lets you intercept tool calls **before** `UseFunctionInvocation()` runs, allowing selective approval (e.g. read-only auto-approve, write requires human). This is currently the recommended pattern for fine-grained policy, since MAF does not yet ship a higher-level out-of-the-box approval router.

**Known issue** (worth noting for production): GitHub issue #3297 reports that in the AG-UI Python integration, MCP tools are not executed after HITL approval flow completes (regular function tools work). This is fixed/being fixed in the AG-UI package; the underlying MAF approval surface itself is sound, but if you wire AG-UI + MCP + HITL, validate the flow against the latest preview package before production cutover.

### 1.7 Performance overhead

There is no Microsoft-published micro-benchmark, but the architectural costs are:

- **stdio MCP** — one local process spawn per server (one-time), then JSON-RPC over pipes; sub-millisecond per tool call after warm-up. Effectively zero network overhead. No process isolation between concurrent calls in a single server, so the server itself must handle concurrency.
- **HTTP/Streamable HTTP MCP** — adds full HTTP round-trip + JSON serialisation (~1–10 ms intra-cluster, 30–100 ms cross-region). SSE keeps the connection open, which amortises costs across many tool calls in a session.
- **Foundry Hosted MCP** — adds an extra hop (your client → Foundry → MCP server → response → Foundry → your client). Acceptable for non-latency-critical tools (docs search, security scanning) but avoid for hot-path tools.
- **Native function tools** (`AIFunctionFactory.Create`) — in-process delegate invocation; lowest possible overhead (effectively a method call plus JSON arg serialisation for schema validation).

Rule of thumb: native function tool < stdio MCP (same machine) < HTTP/SSE MCP (LAN) < Foundry hosted MCP (cross-service). Microsoft docs warn against `DefaultAzureCredential` in production specifically because its sequential probing adds latency — use `ManagedIdentityCredential` for any of the above.

### 1.8 MCP server state across calls in one session

Yes — MCP is explicitly a **stateful, session-scoped JSON-RPC protocol**. The MCP spec defines a session ID (`Mcp-Session-Id` header for Streamable HTTP) issued during `initialize`; subsequent requests reuse the session and the server may keep arbitrary in-memory state (sample servers like `@modelcontextprotocol/server-memory` exploit this). A few caveats:

- For `WithHttpTransport(o => o.Stateless = true)` the server intentionally has no per-session memory; pick this only when the tool is truly stateless.
- In horizontally scaled cloud deployments (multiple instances behind a load balancer), session affinity is not guaranteed unless you configure sticky sessions or externalise state to Redis/Cosmos DB. AWS Bedrock AgentCore docs are explicit that microVM-level RAM is *not* a durable state mechanism — the same applies to Azure Container Apps replicas.
- For **stdio MCP**, the server process is alive for the duration of the agent's lifetime, so in-memory state survives across all tool calls within that agent run.

---

## Section 2 — A2A Protocol in MAF

### 2.1 What A2A is, at the protocol level

Agent-to-Agent (A2A) is an **open standard, originally from Google, now governed by the Linux Foundation** with a TSC including Microsoft, Google, AWS, Cisco, IBM, Salesforce, SAP and ServiceNow. As of v1.0 the protocol is stable. Wire-level shape:

- **Transport**: HTTP(S) with two protocol bindings — **HTTP+JSON** (REST-style) and **JSON-RPC 2.0**. Both are first-class in MAF; servers may expose both simultaneously.
- **Discovery**: Agents publish an **Agent Card** at `/.well-known/agent-card.json` (a JSON document with `name`, `description`, `version`, `capabilities`, `skills`, `securitySchemes`, and `SupportedInterfaces[]` listing endpoints + bindings). Three discovery patterns: well-known path, central registry/catalogue, or direct URL.
- **Methods**: `message/send` (sync), `message/stream` (SSE streaming), `tasks/get` (status), `tasks/cancel`, `tasks/sendSubscribe`, `tasks/pushNotificationConfig/set`, etc.
- **Streaming**: Server-Sent Events. Server advertises `capabilities.streaming = true`; client uses `message/stream`. Each SSE event is a JSON-RPC response carrying a `TaskStatusUpdateEvent` or `TaskArtifactUpdateEvent` with a `final: bool` flag.
- **Long-running tasks**: tasks have a lifecycle (`submitted → working → input-required → completed/failed/canceled`); the spec adds **push notifications** to a client-provided HTTPS webhook for tasks too long for SSE.
- **Identity**: A2A puts no identity in payloads — auth is at HTTP layer (declared in `securitySchemes`), supports Bearer/OAuth2/OIDC/API-key/mTLS.

### 2.2 Exposing a MAF agent as an A2A endpoint (ASP.NET Core)

Two ways. The official MAF integration page (`agent-framework/integrations/a2a`) shows the simplified `MapA2A` style; the deeper hosting page (`agent-framework/hosting/agent-to-agent`) shows the per-binding API.

```csharp
// dotnet add package Microsoft.Agents.AI.Hosting.A2A.AspNetCore --prerelease
// dotnet add package A2A.AspNetCore --prerelease
// dotnet add package Microsoft.Agents.AI.Foundry --prerelease
// dotnet add package Azure.AI.Projects --prerelease
// dotnet add package Azure.Identity

using A2A;
using A2A.AspNetCore;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;

var builder = WebApplication.CreateBuilder(args);
string endpoint = builder.Configuration["AZURE_AI_PROJECT_ENDPOINT"]!;
string model    = builder.Configuration["AZURE_AI_MODEL"] ?? "gpt-4o-mini";

// 1. Register the agent in DI (keyed by agent name).
builder.Services.AddKeyedSingleton<AIAgent>("weather-agent", (sp, _) =>
    new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential())
        .AsAIAgent(
            model: model,
            instructions: "You are a helpful weather assistant.",
            name: "weather-agent"));

// 2. Bind the A2A server to that agent.
builder.AddA2AServer("weather-agent");

var app = builder.Build();

// 3. Map both protocol bindings (clients pick which to use).
app.MapA2AHttpJson("weather-agent", "/a2a/weather-agent");
app.MapA2AJsonRpc ("weather-agent", "/a2a/weather-agent");

// 4. Serve the agent card for discovery.
app.MapWellKnownAgentCard(new AgentCard
{
    Name = "WeatherAgent",
    Description = "A helpful weather assistant.",
    Version = "1.0",
    DefaultInputModes  = ["text"],
    DefaultOutputModes = ["text"],
    SupportedInterfaces =
    [
        new AgentInterface { Url = "https://my-host/a2a/weather-agent",
                             ProtocolBinding = ProtocolBindingNames.HttpJson,
                             ProtocolVersion = "1.0" }
    ]
});

app.Run();
```

`MapWellKnownAgentCard` is provided by the `A2A.AspNetCore` SDK (not the MAF hosting package), and only one well-known card may be served per host — additional agents are still reachable by direct URL but not via `/.well-known/agent-card.json`.

### 2.3 Consuming a remote A2A agent (A2AAgent proxy)

```csharp
// dotnet add package Microsoft.Agents.AI.A2A --prerelease
using A2A;
using Microsoft.Agents.AI;

A2ACardResolver resolver = new(new Uri("https://compliance.partner-team.internal"));
AIAgent complianceAgent = await resolver.GetAIAgentAsync();   // wraps remote endpoint as AIAgent

// Forced binding selection (default prefers HTTP+JSON):
AgentCard card = await resolver.GetAgentCardAsync();
AIAgent agent = card.AsAIAgent(new A2AClientOptions
{
    PreferredBindings = [ProtocolBindingNames.JsonRpc]
});

// Use it like any local agent.
Console.WriteLine(await agent.RunAsync("Review this purchase request for SOX compliance."));

// Streaming:
await foreach (var update in agent.RunStreamingAsync("Write a long compliance memo."))
    if (!string.IsNullOrEmpty(update.Text)) Console.Write(update.Text);
```

For tightly coupled scenarios you can also instantiate `A2AClient` directly: `new A2AClient(new Uri("…")).AsAIAgent(name: "x")`.

### 2.4 A2A agents in MAF workflows

Yes — A2A agents are first-class workflow participants. Because `A2AAgent` is just an `AIAgent`, the standard `WorkflowBuilder`/`AgentWorkflowBuilder` patterns work unchanged: sequential pipelines, fan-out/fan-in, **handoff**, **group chat**, and **Magentic-One** orchestration. The Microsoft "A2A v1 Is Here" post shows mixing a local Foundry/Azure-OpenAI procurement agent with a remote partner-team compliance agent in a handoff workflow:

```csharp
A2ACardResolver resolver = new(new Uri("https://compliance.partner-team.internal"));
AIAgent complianceAgent = await resolver.GetAIAgentAsync();

AIAgent procurementAgent = projectClient
    .ProjectOpenAIClient.GetChatClient("gpt-4o-mini").AsIChatClient()
    .AsAIAgent(
        instructions: "Hand off to compliance when review is needed.",
        name: "procurement-agent");

Workflow workflow = AgentWorkflowBuilder
    .CreateHandoffBuilderWith(procurementAgent)
    .WithHandoffs(procurementAgent, complianceAgent)
    .Build();
```

Note that the legacy `AgentGroupChat` term came from Semantic Kernel — in MAF the equivalent is `AgentWorkflowBuilder.CreateGroupChatBuilderWith(...)`, which also accepts A2AAgent participants. Internally each agent in the workflow is wrapped in an `AIAgentHostExecutor`/`AgentExecutor` that translates messages, manages session state, and forwards turn tokens.

### 2.5 A2A authentication and Entra ID

A2A delegates auth to standard HTTP mechanisms — auth schemes are declared in the Agent Card's `securitySchemes` field, mirroring OpenAPI. **Microsoft Foundry's A2A connection guide explicitly supports four auth methods**:

1. **Key-based** — API key/PAT/Bearer token stored in a Foundry project connection.
2. **Microsoft Entra ID** — the agent's project managed identity acquires a token from Entra ID and sends it on every A2A call. Roles required are assigned on the **target** service.
3. **OAuth identity passthrough (on-behalf-of)** — Foundry generates a consent link, the user signs into the upstream service, and Foundry then acts on the user's behalf. Works with any OAuth 2.0 endpoint, including Entra-ID-protected ones. Users need at least *Azure AI User* on the project.
4. **Custom OAuth app registration** — provide client_id/secret/auth_url/token_url; Foundry returns a redirect URI to whitelist.

Best practice on Azure: protect your A2A server with a JWT Bearer middleware tied to your Entra tenant, leave only the AgentCard at `/.well-known/agent-card.json` unauthenticated, and require app or delegated tokens with explicit scopes for every other path. Many third-party guides (Auth0, Red Hat, Diagrid/Dapr) describe this same pattern.

### 2.6 Maturity of A2A in MAF

**Mixed.** The protocol itself (A2A v1) is stable and Linux-Foundation-governed. The MAF .NET wrappers are still **preview-versioned** (`Microsoft.Agents.AI.A2A 1.0.0-preview.251001.1`, `Microsoft.Agents.AI.Hosting.A2A.AspNetCore 1.0.0-preview.251007.1`), and the Microsoft 1.0 announcement explicitly worded A2A as "**A2A 1.0 support coming soon**." The "A2A v1 Is Here" post that followed confirms migration from v0.3 SDK with minor breaking changes. In practice this means:

- Production-usable today for internal microservices and partner-team integrations.
- **Pin the preview package version**, watch the `microsoft/agent-framework` repo for breaking changes, and isolate the A2A boundary behind your own thin abstraction.
- A reported real-world gotcha (Will Velida blog, *Building a Multi-Agent System in .NET*) — A2A polling-based long-running tasks block the calling agent's SSE stream to a UI; for tightly coupled internal services already under your control, plain REST + agent-identity JWTs is often a better fit than A2A, which shines at **organisational/team boundaries**.
- A2A v3.0 spec improves task stream resumption — the current MAF/A2A SDK still surfaces an `InvalidOperationException` if you try to use a continuation token with streaming.

### 2.7 Streaming over A2A

Yes — `RunStreamingAsync` on the .NET `A2AAgent` proxy maps directly to the A2A `message/stream` JSON-RPC method, which receives Server-Sent Events. Required: the remote agent advertises `capabilities.streaming: true` in its Agent Card. Three event types arrive: `Message` (assistant text deltas), `TaskStatusUpdateEvent`, `TaskArtifactUpdateEvent`. Each is converted to `AgentResponseUpdate` for your code. **Push notifications** (server → client webhook) are also part of the v1 spec for tasks longer than a practical SSE timeout.

---

## Section 3 — Function Tools vs MCP decision matrix (Azure / C#)

The MAF docs and Microsoft samples consistently nudge you toward native `AIFunction` tools when:
- You already have an SDK in .NET that knows the protocol/auth.
- Latency matters (tool is on a hot path of the agent loop).
- Auth is tied to your application's identity (managed identity, Entra ID).

…and toward **MCP** (local or hosted) when:
- The integration target is a discrete tool ecosystem (filesystem, scanners) where *the same server* is reused across multiple agents/IDEs/Copilot.
- You need process or container isolation (security scanners, sandboxed shells).
- The tool is maintained by a different team or vendor.

Recommended pattern per integration target:

| Target | Recommended pattern | Why |
|---|---|---|
| **Bandit / Semgrep** (security scanners, container exec) | **MCP server in a container** (stdio or Streamable HTTP), wrapped from your C# scanner-runner project as in §1.3 | Real process isolation, per-run sandbox, reuse from VS Code / Copilot / other agents, LLM-prompt-injection blast radius is contained |
| **Safety / Snyk** (dependency checkers) | **Native `AIFunction`** wrapping the official REST API; or a thin community/vendor MCP server if one exists | Synchronous REST is trivial; `HttpClient` + `IHttpClientFactory` + Polly retry is enough |
| **SonarQube** (REST API) | **Native `AIFunction`** | Stable REST + OpenAPI; no benefit to MCP unless you need cross-agent reuse |
| **Internal compliance API (REST + Entra ID)** | **Native `AIFunction`** authenticated via `DefaultAzureCredential` / `ManagedIdentityCredential` token provider | Best Entra ID story; `IHttpClientFactory` with a `BearerTokenPolicy` is the canonical pattern |
| **SharePoint document store** | **Native `AIFunction`** wrapping **Microsoft Graph SDK** (`Sites.Selected` permission, app or delegated) | Graph .NET SDK handles auth and throttling. Foundry's hosted SharePoint tool runs as user-context — be aware that Logic Apps / managed-identity invocation paths require extra scope assignment |
| **Azure AI Search** | **Native `AIFunction`** wrapping `Azure.Search.Documents` (`SearchClient`); or the **Foundry Hosted Azure AI Search tool** when you want Foundry to provide grounding + citations | Both are GA; choose hosted when you want server-side citation rendering, native when you want full control over query construction |
| **Git operations (sandboxed shell)** | **MCP server in a container** that exposes `git_clone`, `git_diff`, etc., over stdio | Sandbox is the whole point; MCP gives you a hard process boundary the model can't escape |
| **Web search (Perplexity / Tavily / Brave)** with failover | **Native `AIFunction`** with a small in-process failover chain (see §4.3); or expose the same chain as a single MCP `web_search` tool if other agents need it | Failover, quota tracking, and caching are easier in C# than in MCP server config |

---

## Section 4 — Web Search Integration

### 4.1 Built-in web search

MAF agents do not ship a *framework-built-in* web search tool, but **Foundry-backed MAF agents expose a stable hosted Web Search tool via `WebSearchToolDefinition`**, which is the new GA replacement for the legacy "Grounding with Bing Search" tool. Behind the scenes it uses **Grounding with Bing Search / Bing Custom Search** as a First-Party Consumption Service. Important production caveats:

- Web Search and Grounding with Bing **do not respect VPN or Private Endpoints** — they act as public endpoints regardless of network-secured Foundry. Treat results as untrusted input.
- Data leaves the Azure compliance boundary; Microsoft's Data Protection Addendum does **not** apply. Government Community Cloud customers waive elevated commitments by enabling it.
- Some models are excluded (notably gpt-4o-mini 2024-07-18 and gpt-5 in the classic tool — verify support per model deployment).

### 4.2 Foundry hosted Web Search with MAF (.NET)

```csharp
// dotnet add package Microsoft.Agents.AI.Foundry --prerelease
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

var endpoint   = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")!;
var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME")!;

AIAgent agent = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential())
    .AsAIAgent(
        model: deployment,
        instructions: "You are a helpful assistant that can search the web for current information.",
        tools: [new WebSearchToolDefinition()]);

Console.WriteLine(await agent.RunAsync("What's today's top AI news?"));
```

Yes — the Foundry-hosted Web Search tool can be used from any MAF agent backed by a Foundry project, exactly as above. Citation annotations come back as URL citations on the agent message.

### 4.3 Custom failover web search function tool (C#)

When you can't (or don't want to) use Bing, the production pattern is a single `AIFunction` that delegates to a small failover chain across providers (Tavily → Brave → Perplexity → DuckDuckGo as a free fallback). You wrap it once with `AIFunctionFactory.Create` and the agent treats it as a single tool:

```csharp
using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Polly;
using Polly.Extensions.Http;

public sealed record SearchHit(string Title, string Url, string Snippet);

public interface IWebSearchProvider
{
    string Name { get; }
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct);
}

public sealed class WebSearchTool
{
    private readonly IReadOnlyList<IWebSearchProvider> _chain;       // ordered: primary → fallbacks
    private readonly ILogger<WebSearchTool> _log;

    public WebSearchTool(IEnumerable<IWebSearchProvider> chain, ILogger<WebSearchTool> log)
    { _chain = chain.ToList(); _log = log; }

    [Description("Search the public web. Returns up to N results with title, URL, and snippet.")]
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        [Description("The search query.")] string query,
        [Description("Maximum number of results (1–10). Default 5.")] int maxResults = 5,
        CancellationToken ct = default)
    {
        foreach (var p in _chain)
        {
            try
            {
                var hits = await p.SearchAsync(query, maxResults, ct);
                if (hits.Count > 0)
                {
                    _log.LogInformation("WebSearch via {Provider} ({Hits} hits)", p.Name, hits.Count);
                    return hits;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _log.LogWarning(ex, "Provider {Provider} failed; falling back.", p.Name);
            }
        }
        return Array.Empty<SearchHit>();
    }
}

// Provider example (Tavily). Same pattern for Brave, Perplexity, DuckDuckGo.
public sealed class TavilySearchProvider : IWebSearchProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    public string Name => "Tavily";
    public TavilySearchProvider(HttpClient http, string apiKey) { _http = http; _apiKey = apiKey; }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string q, int n, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync("https://api.tavily.com/search",
            new { api_key = _apiKey, query = q, max_results = n, search_depth = "basic" }, ct);
        resp.EnsureSuccessStatusCode();
        var doc  = await resp.Content.ReadFromJsonAsync<TavilyEnvelope>(cancellationToken: ct);
        return doc!.Results.Select(r => new SearchHit(r.Title, r.Url, r.Content)).ToArray();
    }

    private sealed record TavilyEnvelope(TavilyResult[] Results);
    private sealed record TavilyResult(string Title, string Url, string Content);
}
```

Wire-up + agent registration:

```csharp
// Program.cs — typed clients with Polly retry + circuit breaker
services.AddHttpClient<TavilySearchProvider>().AddPolicyHandler(retryPolicy);
services.AddHttpClient<BraveSearchProvider>().AddPolicyHandler(retryPolicy);
services.AddHttpClient<PerplexitySearchProvider>().AddPolicyHandler(retryPolicy);

services.AddSingleton<IEnumerable<IWebSearchProvider>>(sp =>
[
    sp.GetRequiredService<TavilySearchProvider>(),
    sp.GetRequiredService<BraveSearchProvider>(),
    sp.GetRequiredService<PerplexitySearchProvider>()
]);

services.AddSingleton<WebSearchTool>();

// In the agent factory:
var tool = AIFunctionFactory.Create(
    sp.GetRequiredService<WebSearchTool>().SearchAsync,
    name: "web_search",
    description: "Search the public web with automatic failover across providers.");

AIAgent agent = chatClient.AsAIAgent(
    instructions: "Use web_search for any question that requires current public information.",
    tools: [tool]);

static IAsyncPolicy<HttpResponseMessage> retryPolicy =>
    HttpPolicyExtensions.HandleTransientHttpError()
        .OrResult(r => (int)r.StatusCode == 429)
        .WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(200 * Math.Pow(2, i)));
```

This is exactly the shape the MAF docs recommend (`AIFunctionFactory.Create` over a delegate, `[Description]` on the parameters, DI-managed dependencies). For caching, drop in `IMemoryCache` or `IDistributedCache` keyed on `(query, maxResults)`.

---

## Caveats

- **A2A in MAF is preview-versioned** even after MAF 1.0 GA. The **A2A v1 protocol** is stable; the **.NET wrappers** still ship under `1.0.0-preview.*`. Pin versions, watch the `microsoft/agent-framework` and `A2A` SDK changelogs, and treat A2A as production-suitable but actively evolving.
- **MCP C# SDK** — version cadence is fast. Public posts cite versions ranging from `0.4.0-preview.3` to `0.9.0-preview.1` to recent stable-track releases. Pin your `ModelContextProtocol*` package versions and monitor the `modelcontextprotocol/csharp-sdk` repo.
- **Hosted Web Search and Grounding with Bing** flow data **outside Azure compliance boundaries** and **don't respect Private Endpoints**. For regulated workloads, use a custom failover function tool (§4.3) routed through your egress controls, or enforce domain restrictions with Grounding-with-Bing Custom Search.
- **`DefaultAzureCredential` is a development convenience.** All MAF doc samples carry an explicit warning to use a specific credential (`ManagedIdentityCredential`, `WorkloadIdentityCredential`, or `ClientSecretCredential`) in production to avoid latency, unintended credential probing, and security risks from fallback mechanisms.
- **Foundry Hosted Agents** (containerised MAF agents on Foundry Agent Service) are still in **public preview** and there is a Microsoft commitment to bring them to GA "soon." If you're committing to Foundry as the runtime, validate quota, region, and SLA expectations.
- **Bedrock support in C#** is currently via the OpenAI-compatibility endpoint (`bedrock-runtime.{region}.amazonaws.com/openai/v1`); a native Bedrock client is requested but not yet shipped (issue #2524). For Anthropic-on-Bedrock, this works but you give up some Bedrock-native features.
- **Hosted MCP requires Foundry-backed agents.** If your Azure deployment uses Azure OpenAI or OpenAI directly without Foundry, you must use Local MCP Tools — which is fully supported and equally production-grade, just architecturally different.
- **A2A long-running task resumption** is not fully defined in v2.x and `InvalidOperationException` is currently thrown if you try to resume a streaming task with a continuation token. v3.0 of the A2A spec improves this; track the SDK update.
- **Tool security** — the framework does not yet ship built-in input validation/sandboxing middleware for `AIFunction` tools; an open feature request (#2254) tracks this. Implement your own `DelegatingChatClient` middleware for SSRF/SQL-injection/path-traversal guards on any tool that touches sensitive systems, especially for the security-scanner and git-shell categories.
- **Sources for some performance claims** are inferred from documented architecture rather than published benchmarks; treat the latency ordering in §1.7 as directional, and benchmark against your own workloads before sizing.