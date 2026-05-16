# ADR-001: Workflow Engine

**Status:** Accepted
**Date:** 2026-05-10
**Decision Makers:** Architecture team
**Research:** [R01-response-workflow-durability.md](../098-research/R01-response-workflow-durability.md)

---

## Context

The Agentic Workforce Platform requires durable workflow execution for multi-agent projects that:
- Run for hours to days
- Pause for human approval (4h default, 24h escalation)
- Survive pod restarts and deployments on Azure Container Apps
- Support child workflows, signals, queries, scheduled execution
- Enforce budget before each step

## Decision

**Hybrid: Durable Task SDK + DTS (outer) + MAF Workflows (inner)**

### Outer layer: Durable Task SDK with Azure Durable Task Scheduler (DTS)

The project lifecycle orchestrator runs on the Durable Task SDK (`Microsoft.DurableTask.Worker.AzureManaged` 1.19.0, GA) with the Azure-managed Durable Task Scheduler as the durable backend.

This provides:
- **GA, Microsoft-supported** durability with Azure support tickets
- Activity-level recovery (not superstep-level)
- `WaitForExternalEvent<T>(timeout)` for human approval gates
- Per-activity retry policies
- DTS dashboard with Entra ID + RBAC
- KEDA scaler for ACA autoscaling
- Embeds in ASP.NET Core / Aspire process
- DTS Consumption SKU (pay-per-action) or Dedicated for higher throughput

### Inner layer: MAF WorkflowBuilder (agent graph)

Each agent execution step is a MAF `WorkflowBuilder` graph invoked **inside a Durable Task activity**. This preserves MAF's type-safe executor/edge model for the agent coordination logic (plan → dispatch → execute → verify).

### Migration path

When `Microsoft.Agents.AI.DurableTask` (currently 1.4.0-preview) reaches GA, swap the inner MAF graph to run natively on DTS without changing the workflow definition.

## Alternatives Considered

| Option | Verdict | Why Not |
|--------|---------|---------|
| MAF Workflows alone | Rejected | In-memory checkpoints only; superstep-level recovery; no fencing; not durable enough for regulated production |
| Azure Durable Functions | Rejected | Tied to Functions host; awkward fit for Aspire/ACA; forces out-of-process serverless model |
| DTFx self-hosted | Rejected | Community-maintained, no Microsoft support, explicitly deprecated |
| Temporal (.NET) | Deferred | Best feature set (signals, queries, updates, cron) but requires separate AKS cluster; operational tax too high for ACA-only deployment |
| Temporal Cloud | Deferred | Data residency review required; $200/month minimum; third-party SaaS compliance |

## Consequences

- DTS is an additional Azure resource to provision (Bicep module needed)
- Local dev uses DTS Docker emulator via `CommunityToolkit.Aspire.Hosting.DurableTask`
- Workflow versioning must be planned before first deploy (`UseDefaultVersion("1.0.0")`)
- DTS Consumption SKU caps at 500 actions/sec and 30-day retention for closed runs
- Cron scheduling: use DTS scheduling (preview in .NET SDK) or ACA cron jobs until GA

### Principle Compliance

- **P14 Secure by Default:** New workflow step types are disabled by default — must be explicitly registered. DTS dashboard requires Entra ID + RBAC; missing auth config = refuse to start.
- **P15 Backend Owns All Logic:** All workflow state transitions, step routing, retry decisions, and budget enforcement execute server-side in the Durable Task orchestrator. Clients display progress but never compute next-step logic.
- **P16 Single Source of Truth:** DTS is the authoritative source for orchestration state. The application never maintains a shadow copy in PostgreSQL. Workflow status queries derive from DTS client API.
- **P18 Idempotency:** All Durable Task activities must be idempotent — check for existing results before executing side effects. External API calls within activities use `Idempotency-Key` headers. MAF steps inherit the replay contract.
- **P21 Explicit Over Implicit:** Workflow definitions declare all steps, edges, and retry policies explicitly — no implicit step discovery or auto-wiring. The MAF → DTS migration path is an explicit config toggle, not auto-detected.

## Key Packages

```
Microsoft.DurableTask.Worker.AzureManaged  1.19.0  (GA)
Microsoft.DurableTask.Client.AzureManaged  1.19.0  (GA)
Microsoft.Agents.AI                        1.5.0   (GA)
Microsoft.Agents.AI.Workflows             (GA)
Microsoft.Agents.AI.DurableTask           1.4.0-preview (monitor for GA)
CommunityToolkit.Aspire.Hosting.DurableTask        (local dev)
```
