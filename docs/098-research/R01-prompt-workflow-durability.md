# R01: MAF Workflow Durability vs Temporal vs Azure Durable Functions

## Prompt for claude.ai

---

I am architecting a C# reimplementation of an AI agent orchestration platform (currently Python/Temporal) on the Microsoft Agent Framework (MAF) + Azure AI Foundry + Azure Container Apps. This is for a regulated financial services environment (Investec bank).

I need a **concise architectural comparison** of workflow/orchestration engines for long-running AI agent workflows. The output should be structured tables and clear recommendations — not essays.

### Our Workflow Requirements

Our platform orchestrates multi-agent missions that run for hours to days. The core execution flow is:

```
Plan → [Human Approval Gate] → Dispatch → Execute Agent Tasks → Verify → [Retry Decision] → Complete
```

**Hard requirements:**

1. **Durable execution** — workflows must survive process restarts, deployments, and pod recycling on Azure Container Apps
2. **Human-in-the-loop pauses** — workflows pause for human approval (minutes to 24 hours), then resume exactly where they left off
3. **Scheduled execution** — cron-triggered workflow runs (e.g. nightly security scans)
4. **Child workflows** — a mission run spawns child workflows per agent task, each independently retriable
5. **Signals/queries** — external systems can send signals to running workflows (e.g. approval response, cancellation) and query current state
6. **Timeout hierarchy** — workflow timeout (30 days), activity/step timeout (10 min default, configurable), human approval timeout (4 hours default, 24 hour escalation)
7. **Retry policies** — configurable per activity (max attempts, backoff intervals)
8. **Budget enforcement** — workflow must check budget before each step and terminate if exceeded
9. **Observability** — every workflow state transition must be auditable
10. **Concurrency control** — at most N concurrent workflows per mission

### Options to Compare

Compare these four options across our requirements:

1. **MAF Workflows (WorkflowBuilder + supersteps)** — Microsoft Agent Framework's built-in graph-based workflow engine
2. **Azure Durable Functions (.NET isolated)** — serverless durable orchestration on Azure Functions
3. **Azure Durable Task Framework (DTFx)** — the library underneath Durable Functions, self-hosted in our Container App
4. **Temporal (.NET SDK)** — self-hosted Temporal server on Azure (what we use today in Python)

### Output Format Required

**Table 1: Requirement Coverage Matrix**

| Requirement | MAF Workflows | Durable Functions | DTFx (self-hosted) | Temporal |
|---|---|---|---|---|
| (each of the 10 requirements above) | YES/NO/PARTIAL + brief note | ... | ... | ... |

**Table 2: Operational Characteristics**

| Characteristic | MAF Workflows | Durable Functions | DTFx (self-hosted) | Temporal |
|---|---|---|---|---|
| State persistence backend | ? | ? | ? | ? |
| Survives pod restart | ? | ? | ? | ? |
| Max workflow duration | ? | ? | ? | ? |
| Pause/resume semantics | ? | ? | ? | ? |
| Deployment complexity on Azure | ? | ? | ? | ? |
| .NET SDK maturity | ? | ? | ? | ? |
| Licensing / cost model | ? | ? | ? | ? |

**Table 3: Architecture Fit**

For each option, answer:
- Can it be embedded in an ASP.NET Core / Azure Container App process?
- Does it require a separate cluster/server?
- How does it integrate with MAF agents (can a workflow step call `agent.RunAsync()`)?
- How does it handle the "pause for human approval" pattern?
- What happens if the host process crashes mid-workflow?

**Recommendation section:**

Give a ranked recommendation with rationale. Consider:
- We are already using .NET Aspire + Azure Container Apps (scaffolded via Investec's Avalanche tool)
- We want to minimise external infrastructure (prefer Azure-managed services)
- We need production-grade durability (this is a bank)
- MAF is our agent framework — tight integration matters
- We need this to work within 6 months, not 18 months

**Hybrid patterns:**

If no single option meets all requirements, propose a hybrid. For example: "Use MAF Workflows for short agent execution loops, backed by Durable Functions for the outer mission lifecycle that pauses for approvals."

Keep the total response under 2000 words. Tables are preferred over prose.

---

## After Research

Save claude.ai's response as: `docs/098-research/R01-response-workflow-durability.md`
