# ADR-002: LLM Provider Strategy

**Status:** Accepted
**Date:** 2026-05-10
**Decision Makers:** Architecture team
**Research:** [R02-response-foundry-models.md](../098-research/R02-response-foundry-models.md)

---

## Context

The Agentic Workforce Platform uses multiple LLM providers for different agent roles. We need to decide how to access Claude (primary), GPT (secondary/embeddings), and cost-efficient models from a C#/MAF application on Azure.

## Decision

**Hybrid: Foundry for Azure OpenAI + Foundry Anthropic for Claude (with direct API fallback capability)**

### Primary inference: Foundry Anthropic (Claude)

| Agent Role | Model | Via |
|---|---|---|
| Planner / Director | Claude Sonnet 4.6 (1M context) | Foundry Anthropic |
| Coder | Claude Sonnet 4.6 | Foundry Anthropic |
| Security Reviewer | Claude Sonnet 4.6 (extended thinking) | Foundry Anthropic |
| Researcher | Claude Sonnet 4.6 | Foundry Anthropic |
| Quality / Verification | Claude Haiku 4.5 | Foundry Anthropic |
| Summarisation | Claude Haiku 4.5 | Foundry Anthropic |
| Emergency fallback | Claude Haiku 4.5 | Foundry Anthropic |

### Embeddings: Azure OpenAI via Foundry

| Purpose | Model | Via |
|---|---|---|
| Knowledge platform vectors | text-embedding-3-small (1536d) | Foundry (Azure-direct, Microsoft SLA) |

### Single Foundry project

One Foundry resource per environment (dev/prod) hosts both Anthropic and OpenAI deployments. Auth via Entra ID Managed Identity.

### Key findings that shaped this decision

1. **All required Claude models are available** — Sonnet 4.6, Haiku 4.5, Opus 4.6/4.7 are all in Foundry
2. **Extended thinking works** — adaptive/enabled/disabled with effort levels
3. **Prompt caching works** — 5-min and 1-hour TTLs, 0.1x cache read pricing
4. **Streaming tool calls work** — full Messages API streaming
5. **Structured JSON output** — beta on Foundry (GA on direct API), sufficient for our needs
6. **Pricing is identical** to direct Anthropic API, and MACC-eligible
7. **Foundry Anthropic is still "preview"** — acknowledged as a risk, but models are production-billable
8. **No Microsoft-backed SLA** for Claude — it's a partner model; Anthropic's own commitments apply

### Data residency caveat

Foundry Anthropic inference runs on **Anthropic-managed infrastructure**, not Azure regions. EU-resident inference is "Coming 2026" with no firm date. For our South Africa deployment:
- This is acceptable for non-PII agent workloads (code analysis, architecture review, planning)
- If PII processing is required, we'll route those agents through direct API or evaluate AWS Bedrock EU when needed
- Architecture designed so provider swap is a config change, not a re-architecture

## Content Safety

Foundry does **not** auto-apply Azure AI Content Safety filters to Claude. We must implement content filtering as MAF middleware (IChatClient middleware that calls Azure AI Content Safety API on input/output).

## Model Governance

| Capability | How |
|---|---|
| Pin agent to model version | Separate Foundry deployments per version (e.g. `claude-sonnet-prod-v1`) |
| Budget enforcement | Fail fast — execution stops with clear error when budget exceeded; no model downgrade |
| Cost tracking | `ChatResponse.Usage` in IChatClient middleware → LlmCall table |
| Rate limiting | Per-deployment TPM/RPM in Foundry; consider APIM for per-agent throttling |
| Audit logging | Enable Foundry diagnostic logs (Audit, RequestResponse, Trace) → Log Analytics |

## Alternatives Considered

| Option | Verdict | Why Not |
|--------|---------|---------|
| Foundry-only | Selected (with caveats) | Best Azure integration; MACC-eligible; single billing |
| Direct Anthropic API only | Fallback option | Loses MACC, Entra ID auth, Azure diagnostics; same data residency constraint |
| AWS Bedrock (EU) for Claude | Deferred | Best for EU data residency; MAF .NET connector not yet available (Python only) |
| Azure OpenAI only (no Claude) | Rejected | Claude is superior for our agent workloads (coding, security, planning) |

## Consequences

- `Microsoft.Agents.AI.Anthropic` is preview — pin versions, budget for upgrade work
- Anthropic C# SDK (`Anthropic` v10+) is beta — same caution
- Content Safety must be wired as middleware (not automatic)
- Rate-limit headers are stripped by Foundry — use Azure Monitor instead
- Model identity: no "Claude Sonnet 4" — correct names are Sonnet 4.5, 4.6; Haiku 4.5; Opus 4.1-4.7

### Principle Compliance

- **P14 Secure by Default:** Content Safety middleware is mandatory — application refuses to start without it. New Foundry deployments have diagnostic logging enabled at provisioning time. If Key Vault is unreachable for direct API fallback keys, the fallback path is denied, not silently degraded.
- **P16 Single Source of Truth:** The `LlmCall` table in PostgreSQL is the authoritative record of all LLM interactions. Foundry diagnostic logs are a derived analytical copy. Cost dashboards derive from `LlmCall`, not Foundry billing APIs.
- **P17 Human Authority:** Humans override model assignments at the agent catalog level. Budget ceiling increases require human approval. Content safety violations require human review — the system does not auto-adjust.
- **P18 Idempotency:** LLM calls within Durable Task activities may replay. Cost-tracking middleware checks for existing `LlmCall` records (keyed by execution ID + step sequence) before recording duplicates.
- **P21 Explicit Over Implicit:** Each agent's model assignment is explicitly declared in the catalog — no automatic model selection or fallback chain. If a Foundry deployment is unavailable, the call fails fast rather than silently routing to an alternate model.

## Key Packages

```
Microsoft.Agents.AI.Anthropic              --prerelease  (Claude provider)
Anthropic.Foundry                          --prerelease  (Foundry credentials)
Microsoft.Agents.AI.Foundry                --prerelease  (Azure OpenAI via Foundry)
Azure.AI.Projects                          GA            (AIProjectClient)
Azure.Identity                             GA            (Managed Identity)
```

## Wiring Pattern

```csharp
// Claude via Foundry Anthropic
var anthropic = new AnthropicFoundryClient(
    new AnthropicFoundryIdentityTokenCredentials(
        new ManagedIdentityCredential(), resourceName, ["https://ai.azure.com/.default"]));
AIAgent planner = anthropic.AsAIAgent(model: "claude-sonnet-4-6", instructions: "...", name: "planner");

// GPT/Embeddings via Foundry Azure OpenAI
var foundry = new AIProjectClient(new Uri(projectEndpoint), new ManagedIdentityCredential());
AIAgent triage = foundry.AsAIAgent(model: "gpt-4o-mini", instructions: "...", name: "triage");
```
