# Avalanche + MAF: BFF Skeleton Analysis

**Purpose:** Design a C# BFF skeleton for Mission Control using Microsoft Agent Framework (MAF) scaffolded via Avalanche.
**Sources:**
- [Avalanche Getting Started](https://oneinvestec.atlassian.net/wiki/spaces/EP1/pages/1593573999)
- [Microsoft Agent Framework Overview](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp)
**Date:** 2026-05-10

---

## 1. Strategy: Avalanche Scaffolds the Shell, MAF Powers the Agents

Avalanche already provides the **"Internal Web App + BFF (.NET)"** template. This gives us a production-ready .NET BFF with Aspire, pipelines, and infra. We layer MAF on top for the agent orchestration layer.

```
Avalanche template:  Shell (BFF API + Infra + Pipelines + Auth)
         +
MAF:                 Brain (Agents + Workflows + Tools + Sessions)
         =
Mission Control:     AI-first security mission platform on ICE paved path
```

---

## 2. What Avalanche Gives Us (Adopt Directly)

| Layer | What's Generated | How We Use It |
|-------|-----------------|---------------|
| **BFF API (.NET)** | Controllers, `Program.cs`, ServiceDefaults | Our API surface — missions, sessions, executions, agents |
| **.NET Aspire** | `AppHost` + `ServiceDefaults` | Orchestrates BFF, worker, frontend, Redis, PostgreSQL in dev |
| **Infra-as-Code** | Bicep (Container Apps, Key Vault, App Insights, VNet, Managed Identity) | Deploy to Azure with ICE conventions |
| **CI/CD Pipelines** | 3 Azure DevOps YAML pipelines (deploy, PR validation, cleanup) | Adopt verbatim |
| **Auth** | Entra ID app registration + OAuth | Replace custom JWT — use ICE standard auth |
| **Container Registry** | Icebox push/pull service connections | Container image lifecycle |
| **Monitoring** | Application Insights integration | Telemetry out of the box |
| **Config** | Per-environment variable templates (`global.yml`, `dev.yml`, `prod.yml`) | Environment-specific config |

---

## 3. What MAF Gives Us (Layer on Top)

### 3.1 MAF Component Mapping to Mission Control

| MAF Concept | C# Type | Mission Control Use |
|-------------|---------|-------------------|
| **AIAgent** | `AIAgent` (base class) / `ChatClientAgent` | Each specialised agent (planner, coder, security reviewer, researcher, architect) |
| **Agent Session** | `AgentSession` | Mission context / conversation state that persists across turns |
| **Workflows** | `WorkflowBuilder` + Executors + Edges | Mission execution lifecycle (plan → dispatch → execute → verify → retry) |
| **Function Tools** | `AIFunctionFactory.Create()` | Agent capabilities: file read/write, web search, code execution, DB queries |
| **MCP Tools** | Local/Hosted MCP integration | External tool servers (code analysis, vulnerability scanning) |
| **Middleware** | Agent Run / Function Calling / IChatClient middleware | Cost tracking, security guardrails, logging, gate enforcement |
| **Agent-as-Tool** | `.AsAIFunction()` | Compose agents — planner calls coder as a tool |
| **Providers** | Foundry, Anthropic, OpenAI, Ollama | Multi-provider LLM support (Claude via Foundry Anthropic, GPT via Azure OpenAI) |
| **Checkpointing** | Workflow superstep checkpoints | Durable execution — resume after failures |
| **Human-in-the-Loop** | `RequestInfoExecutor` in workflows | Approval gates (plan review, task approval, post-run sign-off) |
| **A2A Protocol** | `A2AAgent` proxy | Remote agent interop (future: federated agent teams) |

### 3.2 MAF vs Current Python Implementation

| Capability | Current (Python) | MAF (C#) |
|-----------|-----------------|----------|
| Agent definition | PydanticAI decorators + custom classes | `AIAgent` / `ChatClientAgent` with `IChatClient` |
| Multi-turn conversation | Custom session service + Redis | `AgentSession` with `CreateSessionAsync()` / `SerializeSession()` |
| Tool calling | Custom tool registry + PydanticAI tools | `AIFunctionFactory.Create()` + `@tool` attribute |
| Workflow orchestration | Temporal (external dependency) | MAF `WorkflowBuilder` (in-process, checkpointable) — OR keep Temporal via activities |
| Middleware / cross-cutting | Custom FastAPI middleware | MAF Agent Run + Function Calling + IChatClient middleware chain |
| Streaming | SSE via Redis pub/sub | MAF `RunStreamingAsync()` + ASP.NET SSE/SignalR |
| Human-in-the-loop | Temporal signals + custom approval flow | MAF `RequestInfoExecutor` in workflows |
| Cost tracking | Custom per-call recording | MAF IChatClient middleware (intercept every LLM call) |
| Multi-provider | PydanticAI provider abstraction | MAF providers (Foundry, Anthropic, Azure OpenAI, OpenAI, Ollama) |

---

## 4. Proposed Solution Architecture

### 4.1 Project Structure (Avalanche + MAF)

```
MissionControl/
|-- .azuredevops/                           # FROM AVALANCHE
|   |-- pipelines/
|   |   |-- azure-deploy.yml
|   |   |-- azure-cleanup.yml
|   |   +-- azure-pr-validation.yml
|   +-- templates/
|       |-- stages/
|       +-- variables/
|           |-- global.yml
|           |-- dev.yml
|           +-- prod.yml
|
|-- infra/                                  # FROM AVALANCHE (Bicep)
|   |-- main.bicep
|   |-- main.bicepparam
|   +-- modules/
|       |-- container-app.bicep
|       |-- keyvault.bicep
|       |-- postgres-flexible.bicep
|       |-- redis.bicep
|       +-- appinsights.bicep
|
|-- src/
|   |-- MissionControl.sln
|   |
|   |-- MissionControl.AppHost/             # FROM AVALANCHE (Aspire orchestrator)
|   |   +-- Program.cs                      # Wires BFF + Worker + PostgreSQL + Redis + Frontend
|   |
|   |-- MissionControl.ServiceDefaults/     # FROM AVALANCHE (shared config)
|   |   +-- Extensions.cs                   # Health checks, OpenTelemetry, resilience
|   |
|   |-- MissionControl.BFF/                 # BFF API (ASP.NET Minimal API or Controllers)
|   |   |-- Controllers/
|   |   |   |-- MissionsController.cs
|   |   |   |-- SessionsController.cs
|   |   |   |-- ExecutionsController.cs
|   |   |   |-- AgentsController.cs
|   |   |   |-- WorkflowsController.cs
|   |   |   +-- StreamsController.cs        # SSE / SignalR hub
|   |   |-- Middleware/
|   |   |   |-- CostTrackingMiddleware.cs   # MAF IChatClient middleware
|   |   |   |-- SecurityGuardrailMiddleware.cs
|   |   |   +-- GateEnforcementMiddleware.cs
|   |   |-- Hubs/
|   |   |   +-- MissionEventHub.cs          # SignalR for real-time events
|   |   +-- Program.cs
|   |
|   |-- MissionControl.Agents/             # MAF AGENT DEFINITIONS
|   |   |-- Catalog/
|   |   |   |-- PlannerAgent.cs
|   |   |   |-- CoderAgent.cs
|   |   |   |-- SecurityReviewerAgent.cs
|   |   |   |-- ResearcherAgent.cs
|   |   |   |-- ArchitectAgent.cs
|   |   |   +-- DirectorAgent.cs
|   |   |-- Tools/
|   |   |   |-- FileSystemTools.cs          # Read/write/search files
|   |   |   |-- WebSearchTools.cs           # Perplexity/Tavily/Brave
|   |   |   |-- CodeExecutionTools.cs       # Sandboxed code execution
|   |   |   |-- DatabaseTools.cs            # Query knowledge store
|   |   |   +-- GitTools.cs                 # Git operations
|   |   |-- Prompts/
|   |   |   +-- *.md                        # System prompts per agent
|   |   +-- AgentRegistry.cs               # Agent catalog / factory
|   |
|   |-- MissionControl.Workflows/          # MAF WORKFLOW DEFINITIONS
|   |   |-- MissionRunWorkflow.cs           # Plan → Dispatch → Execute → Verify → Retry
|   |   |-- ChatTurnWorkflow.cs             # Single chat interaction flow
|   |   |-- ScheduledJobWorkflow.cs         # Cron-triggered mission runs
|   |   |-- Executors/
|   |   |   |-- PlanningExecutor.cs
|   |   |   |-- DispatchExecutor.cs
|   |   |   |-- AgentExecutionExecutor.cs
|   |   |   |-- VerificationExecutor.cs
|   |   |   |-- GateApprovalExecutor.cs     # Human-in-the-loop
|   |   |   +-- RetryDecisionExecutor.cs
|   |   +-- Events/
|   |       +-- MissionWorkflowEvents.cs
|   |
|   |-- MissionControl.Domain/             # DOMAIN MODEL
|   |   |-- Entities/
|   |   |   |-- Mission.cs
|   |   |   |-- Session.cs
|   |   |   |-- ExecutionRecord.cs
|   |   |   |-- MissionContextDocument.cs
|   |   |   |-- Plan.cs
|   |   |   |-- Artifact.cs
|   |   |   |-- Learning.cs
|   |   |   +-- Decision.cs
|   |   |-- Enums/
|   |   |-- ValueObjects/
|   |   +-- Interfaces/
|   |       |-- IMissionRepository.cs
|   |       |-- ISessionRepository.cs
|   |       +-- IKnowledgeService.cs
|   |
|   |-- MissionControl.Infrastructure/     # DATA ACCESS + EXTERNAL INTEGRATIONS
|   |   |-- Data/
|   |   |   |-- MissionControlDbContext.cs  # EF Core (PostgreSQL + pgvector)
|   |   |   |-- Repositories/
|   |   |   +-- Migrations/
|   |   |-- Redis/
|   |   |   |-- EventBus.cs                # Pub/sub for SSE
|   |   |   +-- CacheService.cs
|   |   +-- External/
|   |       |-- WebSearchProvider.cs
|   |       +-- ContentExtractor.cs
|   |
|   +-- MissionControl.Tests/
|       |-- Unit/
|       |-- Integration/
|       +-- Architecture/                   # ArchUnit-style tests
|
|-- frontend/                               # React SPA (unchanged from current)
|   |-- src/
|   +-- package.json
|
|-- .avalanche.json
|-- azure.yaml
|-- Dockerfile
+-- README.md
```

### 4.2 Key Code Patterns

#### Agent Definition (MAF)

```csharp
// MissionControl.Agents/Catalog/PlannerAgent.cs
public static class PlannerAgentFactory
{
    public static AIAgent Create(AIProjectClient projectClient)
    {
        return projectClient.AsAIAgent(
            model: "claude-sonnet-4",  // via Foundry Anthropic
            name: "planner",
            instructions: PromptLoader.Load("planner.md"),
            tools: [
                AIFunctionFactory.Create(FileSystemTools.ReadFile),
                AIFunctionFactory.Create(FileSystemTools.SearchFiles),
                AIFunctionFactory.Create(WebSearchTools.Search),
                AIFunctionFactory.Create(DatabaseTools.QueryKnowledge),
            ]);
    }
}
```

#### Mission Run Workflow (MAF WorkflowBuilder)

```csharp
// MissionControl.Workflows/MissionRunWorkflow.cs
public static Workflow BuildMissionRunWorkflow(IServiceProvider services)
{
    var planner    = new PlanningExecutor(services);
    var dispatcher = new DispatchExecutor(services);
    var executor   = new AgentExecutionExecutor(services);
    var verifier   = new VerificationExecutor(services);
    var gate       = new GateApprovalExecutor(services);   // HITL
    var retry      = new RetryDecisionExecutor(services);

    var builder = new WorkflowBuilder(planner);

    // Plan → Gate (if gate_mode requires) → Dispatch → Execute → Verify
    builder.AddEdge(planner, gate);
    builder.AddEdge(gate, dispatcher, condition: msg => msg.Approved);
    builder.AddEdge(dispatcher, executor);
    builder.AddEdge(executor, verifier);

    // Verify → Retry (if failed) or complete
    builder.AddEdge(verifier, retry, condition: msg => !msg.Passed);
    builder.AddEdge(retry, dispatcher, condition: msg => msg.Decision == "retry");

    return builder.Build();
}
```

#### Cost Tracking Middleware (MAF)

```csharp
// MissionControl.BFF/Middleware/CostTrackingMiddleware.cs
async Task<ChatResponse> CostTrackingMiddleware(
    IEnumerable<ChatMessage> messages,
    ChatOptions? options,
    IChatClient innerClient,
    CancellationToken ct)
{
    var sw = Stopwatch.StartNew();
    var response = await innerClient.GetResponseAsync(messages, options, ct);
    sw.Stop();

    await _costService.RecordAsync(new LlmCallRecord
    {
        Model = options?.ModelId,
        InputTokens = response.Usage?.InputTokenCount ?? 0,
        OutputTokens = response.Usage?.OutputTokenCount ?? 0,
        LatencyMs = sw.ElapsedMilliseconds,
        AgentName = options?.AdditionalProperties?["agent_name"]?.ToString(),
        MissionId = options?.AdditionalProperties?["mission_id"]?.ToString(),
    });

    return response;
}
```

#### Session Management (MAF)

```csharp
// Multi-turn conversation with persistent session
AgentSession session = await agent.CreateSessionAsync();

// Turn 1
var response1 = await agent.RunAsync("Analyse the authentication module for vulnerabilities", session);

// Turn 2 (session carries context)
var response2 = await agent.RunAsync("Now create a remediation plan for the top 3 findings", session);

// Persist session for later resumption
var serialized = agent.SerializeSession(session);
await _sessionStore.SaveAsync(missionId, serialized);

// Resume later
var restored = await agent.DeserializeSessionAsync(await _sessionStore.LoadAsync(missionId));
```

---

## 5. Avalanche Scaffold Steps

Use the **"Internal Web App + BFF (.NET)"** template:

```bash
# 1. Clone the Azure DevOps repo
git clone https://dev.azure.com/investec/<project>/_git/mission-control
cd mission-control

# 2. Run Avalanche
npx -y --registry https://pkgs.dev.azure.com/investec/_packaging/investec-npm-packages/npm/registry/ \
  @investec/avalanche scaffold

# 3. Select: Internal Web App + BFF (.NET)
# 4. Configure:
#    Subscription:     ice-<tbd>
#    Resource Group:   rg-missionctrl-dev-001
#    Location:         southafricanorth
#    AD Group:         azure-<team>-engteam
#    Service Conn:     azurerm-missionctrl-*
#    Entra ID:         aadapp-mission-control-*
#    App Name:         msnctrl  (8 chars or less)
#    Container Name:   mission-control-bff
#    Namespace:        MissionControl
```

After scaffolding, layer in the MAF packages:

```bash
# Add MAF NuGet packages
dotnet add src/MissionControl.BFF package Microsoft.Agents.AI --prerelease
dotnet add src/MissionControl.BFF package Microsoft.Agents.AI.Foundry --prerelease
dotnet add src/MissionControl.Agents package Microsoft.Agents.AI --prerelease
dotnet add src/MissionControl.Agents package Microsoft.Agents.AI.Foundry --prerelease
dotnet add src/MissionControl.Workflows package Microsoft.Agents.AI.Workflows --prerelease

# Add Anthropic provider (for Claude via Foundry)
dotnet add src/MissionControl.Agents package Anthropic.Foundry --prerelease

# Add infrastructure packages
dotnet add src/MissionControl.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/MissionControl.Infrastructure package pgvector.EntityFrameworkCore
dotnet add src/MissionControl.Infrastructure package StackExchange.Redis
```

---

## 6. Decision Points

| Decision | Options | Recommendation |
|----------|---------|----------------|
| **Workflow engine** | MAF Workflows (in-process) vs Temporal (external) | Start with MAF Workflows. Evaluate Temporal for cross-service durability later. MAF checkpointing may suffice. |
| **Primary LLM provider** | Foundry Anthropic (Claude) vs Azure OpenAI (GPT) | Foundry Anthropic for agent execution. Azure OpenAI for embeddings. MAF supports both simultaneously. |
| **Real-time events** | SignalR vs SSE | SignalR (better .NET integration, auto-reconnect, binary support). |
| **Database** | PostgreSQL (EF Core) vs Cosmos DB | PostgreSQL + pgvector (matches current domain model, vector search for knowledge). |
| **Frontend** | Keep React SPA vs Razor/Blazor | Keep React SPA. BFF serves API; frontend is separate container/static. |
| **Auth** | Custom JWT vs Entra ID (Avalanche default) | Entra ID. Aligns with ICE standard. Avalanche provides the service connections. |

---

## 7. What We Should NOT Adopt

| Pattern | Why Skip |
|---------|----------|
| Avalanche's interactive CLI prompts | We're a single product, not a template factory |
| Template replacement variables | Hardcode for our specific deployment |
| Razor views / server-rendered UI | Keep React SPA |
| Avalanche's default simple API template | Use as starting point but extend heavily with MAF |

---

## 8. Action Items

### Phase 1: Foundation (Avalanche Scaffold)
1. Request Azure prerequisites (subscription, resource group, Entra ID app reg, service connections)
2. Create Azure DevOps repo
3. Run `avalanche scaffold` with Internal Web App + BFF (.NET) template
4. Verify pipelines created and building

### Phase 2: Domain Layer
5. Create `MissionControl.Domain` project with entities from requirements spec
6. Create `MissionControl.Infrastructure` with EF Core + PostgreSQL + pgvector
7. Migrate domain model (Mission, Session, Execution, Plan, Artifact, Learning, Decision, etc.)

### Phase 3: MAF Agent Layer
8. Add MAF NuGet packages
9. Create `MissionControl.Agents` project with agent catalog
10. Port agent definitions (planner, coder, security reviewer, researcher, architect, director)
11. Implement function tools (file system, web search, code execution, git, DB)
12. Configure Foundry Anthropic + Azure OpenAI providers

### Phase 4: MAF Workflow Layer
13. Create `MissionControl.Workflows` project
14. Implement MissionRunWorkflow (plan → gate → dispatch → execute → verify → retry)
15. Implement ChatTurnWorkflow
16. Implement approval gates via HITL executors

### Phase 5: BFF API Surface
17. Implement controllers matching current API surface (missions, sessions, executions, agents, catalog, streams)
18. Add MAF middleware (cost tracking, security guardrails, gate enforcement)
19. Add SignalR hub for real-time mission events
20. Integrate Entra ID auth

### Phase 6: Frontend + Integration
21. Connect React frontend to new BFF API
22. Verify end-to-end flow: create mission → run workflow → stream events → approve gate → complete

---

## 9. Prerequisites Checklist

- [ ] Azure Subscription allocated
- [ ] Resource Groups created (`rg-missionctrl-dev-001`, `rg-missionctrl-prod-001`)
- [ ] Entra ID App Registration created (`aadapp-mission-control-*`)
- [ ] Azure DevOps repo created
- [ ] Service connections configured (`azurerm-missionctrl-*`)
- [ ] Engineering team AD group assigned
- [ ] Icebox service connections available
- [ ] Foundry project created with Claude model deployment
- [ ] Azure OpenAI resource with embeddings model deployed

> These prerequisites can take several days to provision. Start early.

---

## 10. Key References

- [MAF Overview](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp)
- [MAF Agents](https://learn.microsoft.com/en-us/agent-framework/agents/?pivots=programming-language-csharp)
- [MAF Workflows](https://learn.microsoft.com/en-us/agent-framework/workflows/?pivots=programming-language-csharp)
- [MAF Tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/?pivots=programming-language-csharp)
- [MAF Sessions](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/session?pivots=programming-language-csharp)
- [MAF Middleware](https://learn.microsoft.com/en-us/agent-framework/agents/middleware/?pivots=programming-language-csharp)
- [MAF GitHub Samples (C#)](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples)
- [Avalanche Getting Started](https://oneinvestec.atlassian.net/wiki/spaces/EP1/pages/1593573999)
- [Mission Control Requirements Spec](../001-overview/001-mission-control-requirements.md)
