# R12: Aspire + MAF + Avalanche — Putting It All Together

## Prompt for claude.ai

---

I am building an AI agent orchestration platform in C# using Microsoft Agent Framework (MAF), scaffolded via Investec's Avalanche tool (which generates .NET Aspire + Azure Container Apps + Bicep + Azure DevOps pipelines). I need to understand how all these pieces compose into a single deployable solution.

Give me a **concrete solution architecture** with project structure, Aspire wiring, and deployment topology. Code and structure — not concepts.

### What We Have

**From Avalanche scaffold:**
- `.azuredevops/` — 3 pipelines (deploy, PR validation, cleanup)
- `infra/` — Bicep templates (Container Apps, Key Vault, App Insights)
- `src/AppHost/` — .NET Aspire orchestrator
- `src/ServiceDefaults/` — shared config (health checks, OpenTelemetry, resilience)
- `src/BFF/` — ASP.NET Core BFF API

**From MAF (we're adding):**
- Agent definitions (15+ agents with tools, prompts, model configs)
- Workflow definitions (mission lifecycle, chat turns, scheduled jobs)
- Agent session management
- Middleware (cost tracking, security, gates)

**From our domain:**
- 34-entity domain model (EF Core + PostgreSQL)
- Knowledge platform (embeddings, semantic search)
- Real-time events (SignalR)
- Audit pipeline

### Questions — Answer with Structure and Code

**Q1: Solution structure**
Show the recommended .NET solution structure (`.sln` + `.csproj` projects) for this system. Consider:
- Clean Architecture / Vertical Slice — which fits better for an agent platform?
- Where do MAF agent definitions live (separate project or inside BFF)?
- Where do MAF workflows live?
- How to separate domain model from infrastructure from API?
- Test project structure

Show the directory tree with project names and key files (~30 lines).

**Q2: Aspire AppHost wiring**
Show the `Program.cs` for the Aspire AppHost that orchestrates:
- BFF API (ASP.NET Core)
- Agent Worker (background service that runs MAF workflows and agent executions)
- PostgreSQL (Aspire component)
- Redis (Aspire component)
- React frontend (Aspire npm/node integration)

Show ~20 lines of Aspire wiring code.

**Q3: ServiceDefaults for MAF**
The Aspire ServiceDefaults project provides shared configuration. Show what to add for MAF:
- OpenTelemetry traces/metrics for MAF agent operations
- Health checks for LLM provider connectivity (Foundry Anthropic, Azure OpenAI)
- Resilience policies (retry, circuit breaker) for LLM calls
- Configuration binding for agent catalog settings

**Q4: BFF ↔ Agent Worker separation**
Should the BFF API and the agent execution engine be in the same process or separate?
- Same process: simpler, but a long-running agent execution blocks API responsiveness
- Separate: BFF enqueues work, Worker processes it, communicates via Redis/queue
- Hybrid: BFF handles API + short operations, Worker handles long-running workflows

Show the recommended pattern with the communication mechanism between them.

**Q5: Dependency injection for MAF**
Show how to register MAF services in ASP.NET Core DI:
- Agent factory (resolves agent definitions from catalog DB)
- Workflow engine
- IChatClient instances for each provider (Foundry Anthropic, Azure OpenAI)
- Middleware chain registration
- Session store

Show the `IServiceCollection` extension method (~20 lines).

**Q6: Production deployment topology**
Show the Azure resource topology for production:

```
[Azure Container Apps Environment]
├── container-app: mission-control-bff (API + SignalR)
├── container-app: mission-control-worker (agent execution + workflows)
├── container-app: mission-control-frontend (React SPA static serve)
│
[Data]
├── PostgreSQL Flexible Server (domain + audit + embeddings)
├── Azure Cache for Redis (events, sessions, idempotency)
│
[AI]
├── Azure AI Foundry Project (Claude + GPT models)
│
[Security]
├── Key Vault (secrets)
├── Entra ID App Registration (auth)
├── Managed Identity (workload auth)
│
[Observability]
├── Application Insights (telemetry)
├── Log Analytics Workspace (logs)
```

For this topology:
- How many Bicep modules do we need?
- Does Aspire generate the Bicep, or do we maintain it separately from the Avalanche scaffold?
- How does Aspire's dev-time orchestration map to production Container Apps?

**Q7: Health checks and readiness**
Show health check implementations for:
- PostgreSQL connectivity
- Redis connectivity
- Azure AI Foundry model availability (can we ping the model endpoint?)
- MAF agent runtime health (is the agent factory initialized?)

These should integrate with Aspire's health check infrastructure and Container Apps probes.

**Q8: OpenTelemetry for MAF**
Does MAF emit OpenTelemetry traces/metrics? If not, show how to add tracing via middleware:
- Trace per agent.RunAsync() call (agent name, model, duration, token count)
- Trace per workflow execution (workflow name, steps completed, total duration)
- Metric: LLM calls per minute, cost per minute, tokens per minute

### Output Format

- Q1: Directory tree
- Q2-Q5: C# code sketches (10-25 lines each)
- Q6: Resource topology confirmation + Bicep guidance
- Q7-Q8: Code sketches + integration notes

Then:
- **Top 3 architectural risks** of this composition
- **What Aspire gives us for free** vs what we must build ourselves

Keep total response under 3000 words. Structure and code preferred.

---

## After Research

Save claude.ai's response as: `docs/098-research/R12-response-aspire-integration.md`
