# Research Index

Research prompts for the Mission Control MAF architecture. Each prompt is designed to be pasted into claude.ai. Save responses alongside the prompts.

## Status Tracker

| # | Topic | Prompt | Response | Tier | Blocks |
|---|---|---|---|---|---|
| R01 | Workflow Durability (MAF vs Temporal vs Durable Functions) | [Prompt](R01-prompt-workflow-durability.md) | [Done](R01-response-workflow-durability.md) → [ADR-001](../002-architecture/ADR-001-workflow-engine.md) | 1 | R04, R10, R12 |
| R02 | Azure AI Foundry (Models, Anthropic, Governance) | [Prompt](R02-prompt-foundry-models.md) | [Done](R02-response-foundry-models.md) → [ADR-002](../002-architecture/ADR-002-llm-provider-strategy.md) | 1 | R03, R09 |
| R03 | MAF Agent Extensibility (Custom Agents, Context, Templates) | [Prompt](R03-prompt-maf-agent-extensibility.md) | [Done](R03-response-maf-agent-extensibility.md) → [ADR-003](../002-architecture/ADR-003-agent-model-design.md) | 1 | R10, R11 |
| R04 | Data Layer (EF Core + PostgreSQL + pgvector vs alternatives) | [Prompt](R04-prompt-data-layer.md) | [Done](R04-response-data-layer.md) → [ADR-004](../002-architecture/ADR-004-data-layer.md) | 2 | R08 |
| R05 | Real-time Events (SignalR vs SSE vs Web PubSub) | [Prompt](R05-prompt-realtime-events.md) | [Done](R05-response-realtime-events.md) → [ADR-005](../002-architecture/ADR-005-realtime-events.md) | 2 | R12 |
| R06 | Container Isolation (Code Execution Sandboxing) | [Prompt](R06-prompt-container-isolation.md) | [Done](R06-response-container-isolation.md) → [ADR-006](../002-architecture/ADR-006-container-isolation.md) | 2 | R11 |
| R07 | Identity and Auth (Entra ID + Workload Identity) | [Prompt](R07-prompt-identity-auth.md) | [Done](R07-response-identity-auth.md) → [ADR-007](../002-architecture/ADR-007-identity-auth.md) | 3 | R08 |
| R08 | Audit, Compliance, Immutable Evidence | [Prompt](R08-prompt-audit-compliance.md) | [Done](R08-response-audit-compliance.md) → [ADR-008](../002-architecture/ADR-008-audit-compliance.md) | 3 | — |
| R09 | Cost Tracking and Budget Enforcement | [Prompt](R09-prompt-cost-tracking.md) | [Done](R09-response-cost-tracking.md) → [ADR-009](../002-architecture/ADR-009-cost-tracking.md) | 3 | — |
| R10 | Context Assembly Pipeline | [Prompt](R10-prompt-context-assembly.md) | [Done](R10-response-context-assembly.md) → [ADR-010](../002-architecture/ADR-010-context-assembly.md) | 4 | — |
| R11 | MCP and A2A Protocol Integration | [Prompt](R11-prompt-mcp-a2a.md) | [Done](R11-response-mcp-a2a.md) → [ADR-011](../002-architecture/ADR-011-mcp-a2a.md) | 4 | — |
| R12 | Aspire + MAF + Avalanche Integration | [Prompt](R12-prompt-aspire-integration.md) | [Done](R12-response-aspire-integration.md) → [ADR-012](../002-architecture/ADR-012-aspire-integration.md) | 4 | — |
| R18 | GraphRAG — Knowledge Graph Layer | [Prompt](R18-prompt-graphrag-knowledge-graph.md) | [Done](R18-response-graphrag-knowledge-graph.md) → [ADR-015](../002-architecture/ADR-015-knowledge-graph.md) | 5 | R04, R10, R14 |

## Dependency Graph

```
R01 (Workflows)  ──┐
R02 (Foundry)     ──┼──→ R03 (Agent Model) ──→ R10 (Context Assembly)
R04 (Data Layer)  ──┘                          R11 (MCP/A2A)
                                               R12 (Aspire Integration)
R05 (Real-time)  ─────────────────────────────→ R12
R06 (Containers) ─────────────────────────────→ R11
R07 (Auth)       ──→ R08 (Audit)
R09 (Cost)       ── standalone
```

## Recommended Order

Start with Tier 1 (foundation decisions), then Tier 2 (infrastructure), then Tier 3 (cross-cutting), then Tier 4 (advanced).

Within each tier, prompts are independent and can be run in parallel.

## Existing Research

| File | Topic |
|---|---|
| [002-avalanche-bff-skeleton-analysis.md](002-avalanche-bff-skeleton-analysis.md) | Avalanche + MAF BFF skeleton design |
