# Azure AI Foundry as a Model Gateway for a C# Microsoft Agent Framework (MAF) Application in a Regulated Bank — Technical Assessment, May 2026

## TL;DR

- **Foundry Anthropic is a viable model gateway for production Claude usage in a Microsoft-native bank stack — Sonnet 4.6, Opus 4.6/4.7, Haiku 4.5 are all available, billed through Azure Marketplace at Anthropic's standard list prices, and integrated with Microsoft Agent Framework 1.0 (GA, April 2026) via the `Microsoft.Agents.AI.Anthropic` NuGet plus `AnthropicFoundryClient`.**
- **However, Foundry Anthropic is still labelled "preview" and has three material gaps for an EU-regulated bank: (1) inference physically runs on Anthropic-managed infrastructure, not in an Azure region — EU-native inference is officially "Coming 2026" with no firm date; (2) Foundry does not apply Azure AI Content Safety filters to Claude deployments by default — content safety must be wired in by the application; (3) Foundry omits Anthropic's standard rate-limit headers, which complicates client-side back-pressure.**
- **The recommended pattern for the bank is a hybrid: use Foundry as the gateway for Azure OpenAI (GPT-4o, text-embedding-3-small) and for Anthropic where data residency permits, while keeping the option to call the direct Anthropic API (or AWS Bedrock / GCP Vertex EU regions) for workloads that require EU-resident inference today. MAF supports mixing providers in one app trivially because every agent is an `AIAgent` over `IChatClient`.**

---

## Key Findings

| Dimension | Foundry Anthropic | Direct Anthropic API |
|---|---|---|
| Models (May 2026) | Claude Mythos Preview, Opus 4.7, Opus 4.6, Opus 4.5, Opus 4.1, Sonnet 4.6, Sonnet 4.5, Haiku 4.5 | Same plus Claude Code SDK / Agent SDK access |
| "Sonnet 4 / Opus 4 / Haiku 4.5" | Sonnet 4 and Opus 4 do **not exist** as discrete models in this generation (jumps 3.x → 4.1). Sonnet 4.5/4.6 and Opus 4.1/4.5/4.6/4.7 are available. Haiku **4.5** is GA-preview in Foundry. | Same |
| Max context window | 1 M tokens for Opus 4.7, Opus 4.6, Sonnet 4.6 (GA); 200 K for Sonnet 4.5, Haiku 4.5, Opus 4.1/4.5 | Identical |
| Extended ("adaptive") thinking | Supported (Opus 4.6/Sonnet 4.6 = enabled / disabled / adaptive; Opus 4.7 = adaptive / disabled; Mythos = adaptive / enabled). Effort levels low/medium/high/max | Identical |
| Prompt caching | 5-minute and 1-hour TTL caching supported; automatic and explicit `cache_control` | Identical (workspace isolation took effect Feb 5 2026 on both surfaces) |
| Streaming | Supported (SSE Messages API streaming) | Identical |
| Streaming tool calls | Supported (tool use with streaming + fine-grained tool streaming) | Identical |
| Structured JSON output (`output_config` / response_format) | **Public beta** on Foundry (Sonnet 4.5+, Opus 4.1+, Sonnet 4.6, Opus 4.6, Opus 4.7, Haiku 4.5, Mythos) | **GA** on direct API for the same model set |
| Hosted server tools (web search, web fetch, code execution, MCP connector, Files API, Skills, Memory, computer use, citations, vision) | Supported | Identical |
| Pricing per MTok (in/out) | Marketplace = list price: Haiku 4.5 $1 / $5; Sonnet 4.6 $3 / $15; Opus 4.6 / 4.7 $5 / $25; Opus 4.6/4.7 1M context premium $10 / $37.50 above 200K input | Identical, plus optional 1.1× US-only inference geo on direct API |
| Discounts | Batch API 50%, prompt cache reads 0.1×, cache writes 1.25×–2× | Identical |
| Latency overhead | Foundry adds Azure APIM hop (`apim-request-id` header) on top of Anthropic's infrastructure. Anthropic states inference still runs on Anthropic-managed infra; the gateway adds tens of ms typically, not order-of-magnitude. | Lowest |
| Data residency | **Global Standard only at launch**; US DataZone "coming soon"; EU "Coming 2026". Inference physically on Anthropic infrastructure regardless of the Azure region selected. | Equivalent constraint — direct API also runs on Anthropic infra (US, with `inferenceGeo='us'` option) |
| SLA | Microsoft Product Terms classify Foundry Anthropic as a **Non-Microsoft Product**; **Microsoft does not publish a Microsoft-backed SLA**. Anthropic continues to provide its safety and data commitments (incl. zero-data-retention availability). For Microsoft-backed SLAs you need Foundry Models **sold directly by Azure** (Azure OpenAI, etc.). | Anthropic's standard service availability commitments |
| Content Safety filters | **Not applied by default** to Claude deployments. Microsoft Learn explicitly states: "Foundry doesn't provide built-in content filtering for Claude models at deployment time." Customers must call Azure AI Content Safety API or wire content filtering as middleware. (Filters are applied automatically only to Azure-direct models / Azure OpenAI.) | None server-side; bank must implement |
| Audit logging | First-class via `azurerm_monitor_diagnostic_setting` → categories `Audit`, `RequestResponse`, `Trace` to Log Analytics + Storage; correlation IDs `request-id` and `apim-request-id` returned | Anthropic logs only; bank must capture client-side |
| Cost tracking | Azure Cost Management at deployment-tag granularity; per-call cost requires App Insights + custom calculation from `usage` object | Anthropic console only |
| Quota / rate limits | Configurable TPM/RPM per deployment; quota-increase form supported (Anthropic is the only partner family that supports increases). Anthropic's `anthropic-ratelimit-*` headers are **stripped** by Foundry — must use Azure Monitor instead | Standard Anthropic headers exposed |
| Subscription / regions | Enterprise or MCA-E only; East US 2, Sweden Central; Azure credits **cannot** be applied (credit card billed) | Any |

---

## Section 1 — Foundry Anthropic (Claude via Azure AI Foundry)

### Available models (May 2026)
`learn.microsoft.com/azure/foundry/foundry-models/how-to/use-foundry-models-claude` lists, as of the latest revision, the following Foundry-deployable Claude models (all "Global Standard" deployment, in East US 2 or Sweden Central):

- **Claude Opus 4.7** — frontier coding/agent model, 1 M context, adaptive thinking only, tokenizer change vs. 4.6 (~+18–35% more tokens for the same input)
- **Claude Opus 4.6** — 1 M context, adaptive/enabled/disabled thinking, four effort levels including `max`
- **Claude Opus 4.5**
- **Claude Opus 4.1**
- **Claude Sonnet 4.6** — 1 M context (GA in Foundry), adaptive thinking, $3 / $15 per MTok
- **Claude Sonnet 4.5** — 200 K context
- **Claude Haiku 4.5** — 200 K context, $1 / $5 per MTok
- **Claude Mythos Preview** — gated research preview for defensive cybersecurity (Entra ID auth only)

There is **no model called "Claude Sonnet 4" or "Claude Opus 4"** — the post-3.x line jumps directly to 4.1. The bank should plan around 4.5 / 4.6 / 4.7 and Haiku 4.5.

### Feature support matrix
- **Extended (adaptive) thinking**: Yes. The `thinking` parameter accepts `enabled` / `disabled` / `adaptive` (Opus 4.6, Sonnet 4.6); Opus 4.7 supports `adaptive` / `disabled`; Mythos supports `adaptive` / `enabled`. The `effort` parameter accepts `low` / `medium` / `high`, and on Opus 4.7/4.6 and Sonnet 4.6 also `max`. Both work standalone or combined.
- **Prompt caching**: Yes — both 5-minute and 1-hour TTLs are GA and available on Foundry (workspace-level cache isolation enforced as of Feb 5 2026, matching the direct API). Pricing multipliers: cache writes 1.25× (5-min) / 2× (1-hour); cache reads 0.1×.
- **Streaming tool calls**: Yes — full streaming Messages API including parallel tool calls and fine-grained tool streaming.
- **Structured JSON output (`output_config` / `response_format`, `tool_choice`)**: **Public beta on Foundry**, GA on the direct Anthropic API. Use the `output_config.format = json_schema` parameter for guaranteed schema conformance on Sonnet 4.5+, Opus 4.1+, Sonnet 4.6, Opus 4.6, Opus 4.7, Haiku 4.5, and Mythos. `tool_choice` (forced tool use) is fully supported.
- **Hosted tools**: code execution, web search, web fetch, citations, vision, file uploads (Files API), Skills, Memory, MCP connector, computer use, bash, text editor — all available via Foundry-hosted Claude.
- **PDF, Files, Token Count APIs**: Available.

### Context windows
1 M tokens (GA) for Opus 4.7, Opus 4.6, and Sonnet 4.6. 200 K for everything else (Sonnet 4.5, Haiku 4.5, Opus 4.5, Opus 4.1). This matches the direct Anthropic API exactly. Above 200 K input tokens on Opus 4.6/4.7 and Sonnet 4.6, premium pricing applies (e.g., Opus is $10 / $37.50 per MTok above 200 K).

### Pricing — Foundry vs direct API
Microsoft and Anthropic both confirm that "Pricing for Claude in the Microsoft Marketplace uses Anthropic's standard API pricing." There is no Azure markup or Azure discount on Claude tokens; the only commercial differences are:

| Model | Input $/MTok | Output $/MTok | Cache write 5-min | Cache read | Batch (-50%) |
|---|---|---|---|---|---|
| Claude Sonnet 4.6 | $3.00 | $15.00 | $3.75 | $0.30 | $1.50 / $7.50 |
| Claude Haiku 4.5 | $1.00 | $5.00 | $1.25 | $0.10 | $0.50 / $2.50 |
| Claude Opus 4.6 / 4.7 | $5.00 | $25.00 | $6.25 | $0.50 | $2.50 / $12.50 |

Foundry billing is consolidated on the Azure invoice and is eligible for **Microsoft Azure Consumption Commitment (MACC)** burn-down (a real procurement benefit for a bank with an existing MACC). However: Azure credits (e.g., MSDN, Founders Hub, Sponsored) **cannot** be used for Claude — the credit card on file is charged. Direct Anthropic API offers an additional `inferenceGeo='us'` mode at 1.1× pricing — Foundry is currently global-only and does not expose this premium toggle.

### Latency overhead
Foundry exposes Claude through `https://<resource>.services.ai.azure.com/anthropic/v1/messages` and inserts an Azure APIM gateway hop (visible via the `apim-request-id` correlation header alongside Anthropic's own `request-id`). According to Anthropic's documentation, the actual inference still happens on Anthropic-managed infrastructure (not Azure GPUs), so the wire-level overhead is just the APIM proxy layer — typically tens of milliseconds, not multiples. There is no published SLO on this overhead; the bank should benchmark it for its own traffic mix.

### Data residency
This is the **single most material constraint** for a regulated EU bank. Anthropic's regional-compliance page and Microsoft Q&A both state explicitly:

- Claude on Foundry currently runs on **Anthropic-hosted infrastructure**, not Azure regional GPUs. AWS Bedrock and GCP Vertex AI run Claude on the cloud provider's own EU regions; Foundry does not yet.
- Even if you provision your Foundry resource in **Sweden Central** or **East US 2**, "the actual inference request is routed to Anthropic's own servers — regardless of which Azure region you select."
- Anthropic's regional-compliance page lists Microsoft Foundry EU support as **"Coming 2026"**, with no firmer date publicly committed.
- Microsoft's data-privacy doc confirms: "When you transact for Claude in Foundry, you will agree to Anthropic's terms of use and **Anthropic (not Microsoft) is the processor of the data**…prompts and outputs may be processed outside of your region for operational purposes."

For a bank with hard EU data-residency obligations (e.g., ECB / DORA / national supervisory mandates, or BaFin / FINMA / FCA expectations on cross-border AI processing), **Foundry Anthropic does not provide an in-region data processing guarantee today.** The interim Microsoft Q&A recommendation is to use AWS Bedrock (e.g., EU Frankfurt) or GCP Vertex AI (EU regions) for Claude until Foundry's EU footprint lands.

### SLA / uptime
Microsoft's Foundry product terms classify Anthropic models as **Non-Microsoft Products**. They are **not covered by Microsoft's Azure Direct SLAs** (which apply to Azure OpenAI, Azure Direct Models, etc.). Microsoft Learn's Foundry Models overview is explicit: enterprise SLAs apply to "Models sold directly by Azure"; partner/community models (Anthropic, Hugging Face, Mistral, etc.) are "supported by their providers, with varying levels of SLA." Anthropic provides its own commitments (including zero-data-retention availability), but the bank should not assume Microsoft's 99.9% Cognitive Services SLA covers Claude inference failures.

### Feature gaps vs direct Anthropic API

| Capability | Direct API | Foundry |
|---|---|---|
| Structured outputs | GA | **Beta** |
| `anthropic-ratelimit-*` headers | Returned | **Stripped** — use Azure Monitor instead |
| `inferenceGeo='us'` option | Yes (1.1× pricing) | Not exposed |
| EU-resident inference | Not currently (US-only); Bedrock/Vertex EU regions are alternative | "Coming 2026" |
| Claude Code SDK / Claude Desktop | Native | Configurable via `CLAUDE_CODE_USE_FOUNDRY=1` and `Help → Developer → Configure third-party inference` |
| Foundry Agent Service `create_agent` (managed agents in Foundry portal) | n/a | **Not supported for Claude** — only Microsoft-direct models. Claude must be used via Microsoft Agent Framework or direct SDK |
| Microsoft Entra ID / managed identity auth | n/a | Yes (RBAC, "Cognitive Services User" role) |
| MACC burn-down | No | Yes |
| Microsoft-backed SLA | n/a | No (partner model) |

---

## Section 2 — Azure OpenAI via Foundry

### GPT-4o and text-embedding-3-small
Yes — both are first-class Azure-direct Foundry Models. They are deployable through `Microsoft.Foundry` projects via the Foundry portal or the Models endpoint, with Standard / Data Zone / Global Standard / Provisioned deployment types, and they are covered by Microsoft SLAs and Azure Direct data-processing terms. `text-embedding-3-small` is exposed through the `FoundryEmbeddingClient` / Embeddings API. GPT-4o is accessible via Chat Completions, the Responses API, and the Agent Service.

### Foundry Models vs classic Azure OpenAI resource
- **Microsoft Foundry (the unified successor brand to "Azure AI Studio" / "Azure AI Foundry classic")** consolidates Azure OpenAI, partner/community models, agents, evaluations, and the model catalog under one resource type (`services.ai.azure.com`).
- **Classic Azure OpenAI resource** (`openai.azure.com`) still exists. Microsoft's guidance is that new projects should use Foundry projects; classic resources remain supported but are the "old surface."
- **Recommendation for the bank**: deploy a single Microsoft Foundry resource per environment (dev / pre-prod / prod) and prefer the unified `*.services.ai.azure.com` endpoint. The unified Foundry REST surface (the `/openai/v1/` routes) is GA as of February 2026; SDKs across all languages are converging on it.
- The C# `OpenAI` SDK, the `Azure.AI.OpenAI` SDK, and the `Microsoft.Extensions.AI.OpenAI` adapter all work against both endpoint forms — only the URL changes.

### Mixing Anthropic + OpenAI in one Foundry project
Yes. A single Foundry resource can host both an Azure OpenAI deployment (e.g., `gpt-4o-mini`, `text-embedding-3-small`) and one or more Anthropic deployments (e.g., `claude-sonnet-4-6`). Each deployment is invoked via its own URL path:

- `https://<r>.services.ai.azure.com/openai/v1/chat/completions` (Azure OpenAI / Foundry direct)
- `https://<r>.services.ai.azure.com/anthropic/v1/messages` (Anthropic Messages API)

Authentication via Microsoft Entra ID is shared across both; quota / governance is configured per deployment. Foundry's **Model Router** (GA, version 2025-11-18) can route prompts dynamically between GPT-4o, GPT-5.x, Claude Haiku 4.5, Sonnet 4.5, Opus 4.1, Opus 4.6, DeepSeek, Llama, and Grok models — with the caveat that **Claude models must be deployed separately** before the router can target them.

---

## Section 3 — Model Governance in Azure AI Foundry

### Versioning and pinning
Yes. When you create a Claude or OpenAI deployment, you choose either a specific version or "auto-update to latest." Best practice (and Anthropic's own guidance for Claude Code on Foundry) is to **pin specific versions** for production deployments. The deployment name is what the agent's `model:` parameter references — you can have `claude-sonnet-prod-v1` and `claude-sonnet-shadow-v2` side-by-side, each pinned to a specific underlying version.

### A/B testing / traffic splitting
Foundry does **not** ship a managed traffic-splitting primitive (no built-in "send 5% to v2"). Two practical patterns:
1. **Model Router (GA)** — routes per-prompt based on prompt complexity, with Balanced / Cost / Quality modes and a custom "Model subset" filter. This is *adaptive routing*, not percentage A/B; you can observe distribution in Azure Monitor by filtering on `response.model`.
2. **Application-side splitting in MAF** — implement A/B in middleware or DI: build two `IChatClient` instances and a router function. This is the right pattern for true canary/percentage rollouts.

### Quotas and per-team limits
- Quota is assigned per-subscription, per-region, per-model, per-deployment-type in **TPM** (tokens-per-minute) units, which proportionally drive RPM (requests-per-minute).
- Per-deployment TPM is editable in 1,000-unit increments; you can carve out separate deployments per team and assign each its own TPM ceiling. This is the closest native equivalent to "per-team token caps."
- Anthropic models support quota increase requests (the only partner family that does).
- For true per-agent token budgeting, the bank typically fronts Foundry with **Azure API Management** and uses APIM subscription keys + token-counting policies. The "Tracking Every Token" Microsoft Community Hub pattern (Nov 2025) shows APIM + App Insights doing exactly this.

### Content filtering
This is the second material gap for the bank.
- **Azure OpenAI / Azure Direct models** in Foundry: full Azure AI Content Safety integration (Hate, Self-Harm, Sexual, Violence categories at safe/low/medium/high severities; Prompt Shields, Groundedness Detection, Protected Material). Filters are applied by default, configurable, and modifiable only with Microsoft approval (Limited Access form).
- **Anthropic Claude on Foundry**: Microsoft Learn states explicitly that "**Foundry doesn't provide built-in content filtering for Claude models at deployment time. Configure AI content safety during model inference.**" The serverless content filter that auto-applies to other partner models (Llama, Mistral, Cohere) is not currently auto-applied to Claude. The bank must:
  - Wire Azure AI Content Safety as middleware around every Claude call (input + output), or
  - Use Foundry's "Guardrails" feature in Foundry Control Plane to apply filters across Claude agents/projects, or
  - Trust Anthropic's Constitutional AI built-in safety (insufficient for a regulated bank — needs explicit, auditable, configurable guardrails).
- Microsoft's Foundry guardrails feature (in the new portal) does support applying content filters across multiple agents/models in a project, but coverage of Anthropic deployments has been the subject of multiple Q&A clarifications and should be validated for the bank's specific tenant before going live.

### Audit logging
Foundry's `Microsoft.CognitiveServices/accounts` resource type emits three diagnostic log categories — **`Audit`**, **`RequestResponse`**, **`Trace`** — plus all metrics. These can be routed to Log Analytics (real-time KQL) and Storage (long-term retention) via `azurerm_monitor_diagnostic_setting`. Important caveat: by default, **none** of these logs are enabled. Enable them explicitly. Log Analytics and Application Insights become the audit substrate; Microsoft Purview's DSPM-for-AI provides higher-level data-classification and policy capabilities atop the same telemetry. Both correlation IDs (`apim-request-id`, Anthropic `request-id`) are captured.

### Cost tracking
- Azure Cost Management exposes per-deployment cost via deployment tags (filter by Tag → Deployment).
- Per-call cost is **not** in Foundry's bill-of-materials directly; the standard pattern is to capture the `usage` object from Anthropic/OpenAI responses in App Insights and compute cost in KQL using a per-model price table. The Microsoft Community Hub "Tracking Every Token" reference architecture (2025-11) shows this end to end with APIM + App Insights `customMetrics`.
- The MAF telemetry middleware (OpenTelemetry GenAI semantic conventions) emits `gen_ai.usage.input_tokens` and `gen_ai.usage.output_tokens` automatically once you call `.UseOpenTelemetry()` or wire `AddOpenTelemetry()` on the source `*Microsoft.Agents.AI`.

### Rate limiting
Foundry rate limits are enforced at the deployment level (TPM/RPM). Across multiple agents that share a deployment, the rate limit is shared — so if four MAF agents hit the same `claude-sonnet-prod` deployment, they collectively consume the same TPM bucket. Strategies:
- One deployment per agent or per agent class for hard isolation.
- APIM in front for fine-grained per-caller / per-route throttling and 429 handling.
- The `FunctionInvokingChatClient` in MAF makes one provider call per tool round-trip — so a 5-iteration `MaximumIterationsPerRequest` is up to 5 inbound provider calls per user message; Anthropic and OpenAI rate limiters count those individually. Plan TPM accordingly.

---

## Section 4 — Microsoft Agent Framework Integration (C# / .NET)

MAF is **GA at version 1.0** as of April 2026 (announcement: "Microsoft Agent Framework Version 1.0", microsoft.com/agent-framework blog, April 3 2026). This includes long-term support, stable APIs, and first-party connectors for Microsoft Foundry, Azure OpenAI, OpenAI, **Anthropic Claude**, Amazon Bedrock, Google Gemini, and Ollama.

### NuGet packages

| Package | Purpose | State (May 2026) |
|---|---|---|
| `Microsoft.Agents.AI` | Core MAF abstractions — `AIAgent`, `ChatClientAgent`, sessions, middleware | GA 1.0 |
| `Microsoft.Agents.AI.Foundry` | Foundry project client `AIProjectClient.AsAIAgent(...)`; works for Azure OpenAI models hosted in a Foundry project (GPT-4o, GPT-5.x, embeddings) | Prerelease at 1.0 line; tracking GA |
| `Microsoft.Agents.AI.Anthropic` | Adds Anthropic provider for MAF; exposes `AnthropicClient.AsAIAgent(...)` and Foundry-Anthropic `AnthropicFoundryClient` | Prerelease — versions 1.0.0-rc5 → 1.3.0-preview.260423.1 (April 24 2026) |
| `Microsoft.Agents.AI.OpenAI` | Direct OpenAI / Azure OpenAI provider using Chat Completions / Responses / Assistants APIs | Prerelease |
| `Microsoft.Agents.AI.AzureAI.Persistent` | Foundry Agent Service (server-side persisted agents). **Note:** does NOT support Claude as the underlying model — Foundry Agent Service `create_agent` is OpenAI-only today | Prerelease |
| `Anthropic` (official Anthropic C# SDK v10+) | Used under the hood by `Microsoft.Agents.AI.Anthropic` | Beta |
| `Anthropic.Foundry` | Foundry-specific Anthropic client wrapper exposing `AnthropicFoundryClient`, `AnthropicFoundryApiKeyCredentials`, `AnthropicFoundryIdentityTokenCredentials` | Prerelease |
| `Microsoft.Extensions.AI` / `Microsoft.Extensions.AI.OpenAI` | The `IChatClient` abstraction — current GA-line release 10.5.1 (May 2 2026), targeting .NET 8/9/10, .NET Standard 2.0, .NET Framework 4.6.2+ | GA |
| `Azure.Identity` | Entra ID / Managed Identity | GA |
| `Azure.AI.Projects` | `AIProjectClient` for unified Foundry endpoints | GA |

#### Minimum NuGet package set for the bank's MAF app:
```
dotnet add package Microsoft.Agents.AI                          # core
dotnet add package Microsoft.Agents.AI.Foundry --prerelease     # Azure OpenAI via Foundry
dotnet add package Microsoft.Agents.AI.Anthropic --prerelease   # Claude via Foundry or direct
dotnet add package Anthropic.Foundry --prerelease               # Foundry-specific Anthropic credentials
dotnet add package Azure.Identity                               # Managed Identity / Entra ID
dotnet add package Azure.AI.Projects                            # AIProjectClient
```

### Wiring Anthropic via Foundry (recommended pattern in C#)
```csharp
using Anthropic.Foundry;
using Azure.Identity;
using Microsoft.Agents.AI;

var resource = "<resource-name>";        // subdomain before .services.ai.azure.com
var deployment = "claude-sonnet-4-6";    // your Foundry deployment name (not the model ID)

var client = new AnthropicFoundryClient(
    new AnthropicFoundryIdentityTokenCredentials(
        new ManagedIdentityCredential(),  // production: NOT DefaultAzureCredential
        resource,
        ["https://ai.azure.com/.default"]));

AIAgent agent = client.AsAIAgent(
    model: deployment,
    instructions: "You are an underwriting copilot.",
    name: "Underwriter");
```

Microsoft's docs and the "Trim Journey" walk-through both call out **using `ManagedIdentityCredential` rather than `DefaultAzureCredential` in production** — `DefaultAzureCredential` probes multiple credential sources and adds latency.

### Runtime model switching (budget-triggered downgrade)
`ChatClientAgent` accepts a model name in its constructor/options, but the underlying `IChatClient` is captured at construction time. The clean pattern in MAF:
1. Build N `IChatClient` instances (one per model — Sonnet 4.6, Haiku 4.5, GPT-4o-mini, etc.).
2. Wrap each in a `ChatClientAgent` and register the agents in DI.
3. Use a router middleware (or a simple `IAgentRouter` service) that picks the right agent per request based on token-budget signals, prompt category, or feature flags.

`ChatClientAgent` itself does not have a "swap model on the fly" API — re-using the same agent against multiple models is achieved by either constructing a new agent (cheap) or using Foundry's Model Router as the upstream.

### Token usage / cost in middleware
- The `IChatClient` `ChatResponse.Usage` object exposes `InputTokenCount`, `OutputTokenCount`, `TotalTokenCount` for both OpenAI and Anthropic backends. MAF's `AgentRunResponse` surfaces these via `response.Usage`.
- An open issue (`microsoft/agent-framework #2688`, December 2025) requested a richer `Metrics` property on `AgentRunResponse` (cost estimate, latency breakdown, success/failure). The accepted answer is "use OpenTelemetry — emit cost as a derived metric in your collector," and that is the production pattern: enable `setup_observability()` (Python) or `AddOpenTelemetry().AddSource("*Microsoft.Agents.AI")` (.NET) and compute cost in App Insights.
- MAF middleware (`UseFunctionInvocation`, custom `IChatClient` decorators) gets full access to the request/response, including `Usage`. Cross-cutting telemetry, audit, and Content Safety should be implemented as middleware decorators in the `ChatClientBuilder` pipeline rather than in the agent prompt.

### Multiple Foundry providers in one MAF app
Trivial. Each agent is an `AIAgent` over an `IChatClient`. A bank app can register, e.g.:
- `AzureOpenAIClient(...).GetChatClient("gpt-4o").AsAIAgent(...)` for routing/triage
- `AnthropicFoundryClient(...).AsAIAgent("claude-sonnet-4-6", ...)` for complex reasoning
- `AnthropicFoundryClient(...).AsAIAgent("claude-haiku-4-5", ...)` for high-volume sub-agents
- A `FoundryEmbeddingClient` for `text-embedding-3-small` retrieval

…all in the same DI container, all participating in the same multi-agent workflow (sequential, concurrent, group-chat, handoff orchestrations) and the same A2A or MCP protocol surface. The MAF orchestration layer is provider-agnostic.

### `Microsoft.Extensions.AI.IChatClient` and Anthropic
- `Microsoft.Extensions.AI` is GA (10.5.1 as of May 2 2026).
- Microsoft does **not** ship an official `Microsoft.Extensions.AI.Anthropic` NuGet package. The Anthropic provider in MAF rides on the official `Anthropic` SDK (v10+), which itself implements `IChatClient` via `client.AsIChatClient("claude-opus-4-7")`. There is also a **community** package by `tghamm/Anthropic.SDK` and a third-party `jeremy-schaab/Microsoft.Extensions.AI.Anthropic` package — neither is Microsoft-supported.
- A live GitHub issue (`dotnet/extensions #7058`, late 2025) noted that Anthropic-on-Foundry models initially didn't work with `IChatClient.GetResponseAsync` due to URL/header mismatches. The fix is to use `AnthropicFoundryClient` (from `Anthropic.Foundry`) and then `.AsAIAgent()` rather than the AzureOpenAI adapter.

---

## Section 5 — Gaps and Alternatives

### Direct Anthropic API via MAF (bypassing Foundry)
Fully supported — same `Microsoft.Agents.AI.Anthropic` package, just use `AnthropicClient` instead of `AnthropicFoundryClient`:
```csharp
var client = new AnthropicClient { ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") };
AIAgent agent = client.AsAIAgent(model: "claude-sonnet-4-6", instructions: "...");
```
Required environment variables: `ANTHROPIC_API_KEY` (or `ANTHROPIC_AUTH_TOKEN` + `ANTHROPIC_BASE_URL` for proxy scenarios). The MAF `AIAgent` interface is identical, so workflows and orchestrations don't change.

### Data residency implications of bypassing Foundry
Going direct to Anthropic does **not** give the bank EU data residency either — Anthropic's first-party API runs primarily in US infrastructure. For EU residency *today*, the only options that Microsoft's own Q&A and Anthropic's regional-compliance page point to are **AWS Bedrock (EU regions, e.g., Frankfurt)** and **GCP Vertex AI (EU regions)**. MAF has connectors for both (`AnthropicBedrockClient`, `AnthropicVertexClient` — currently Python; .NET via the Anthropic C# SDK directly).

A second consideration: bypassing Foundry means losing:
- MACC eligibility (direct Anthropic invoicing is not Azure-billable).
- Microsoft Entra ID / Managed Identity auth (you must manage Anthropic API keys, ideally in Azure Key Vault).
- Azure diagnostic logging integration (you must roll your own audit trail to App Insights).
- Single-pane Cost Management view.

For most regulated EU banks, the cleanest interim pattern is therefore **Bedrock-EU for Anthropic + Foundry for Azure OpenAI**, both fronted by MAF, until Foundry's EU-resident Anthropic infrastructure ships in 2026.

### Mixing Foundry + direct API in one MAF app
Yes, and it's a well-supported pattern. MAF doesn't tie an application to a single provider; each `IChatClient` (or `AnthropicClient` / `AnthropicFoundryClient`) is a per-agent or per-route concern. A typical bank topology:
- Agents handling **non-PII, low-sensitivity** prompts → Foundry Anthropic (for unified billing, audit, MACC).
- Agents handling **PII / regulated data** → AWS Bedrock Claude in EU region (until Foundry EU lands), or local Foundry Local + Phi-4 for zero-egress.
- Embeddings + retrieval → Azure OpenAI via Foundry (`text-embedding-3-small`, fully Microsoft-direct, Microsoft SLA).
- Triage / cost control → Foundry Model Router across GPT-4o-mini and Claude Haiku 4.5.

### Features missing in Foundry Anthropic vs direct API (summary)
1. **Structured outputs** — beta on Foundry, GA on direct.
2. **EU-resident inference** — not on Foundry yet (target 2026); not on direct API either, but available on AWS Bedrock / GCP Vertex EU.
3. **Anthropic rate-limit headers** — stripped by Foundry; visible on direct.
4. **`inferenceGeo` parameter** — direct API supports US-only routing at 1.1× pricing; Foundry does not.
5. **Microsoft-backed SLA** — partner model, only Anthropic's commitments apply on Foundry.
6. **Azure AI Content Safety auto-application** — not applied by default to Claude on Foundry (must wire manually); applied by default to Azure-direct models.
7. **Foundry Agent Service `create_agent` (server-managed agents in the Foundry portal)** — Claude is not yet supported. Only Microsoft Agent Framework (client-side `ChatClientAgent`) works for Claude.

---

## Caveats

- **Preview status.** Foundry Anthropic is still labelled "preview" in Microsoft's data-privacy and overview docs as of May 2026, even though models are production-billable and used by named customers (Adobe, Dentons, Manus, Macroscope). For a regulated bank, "preview" is a real procurement and risk-acceptance flag — review with the bank's vendor-risk function.
- **Model identity confusion.** There is no "Claude Sonnet 4," "Claude Opus 4," or "Claude Haiku 4." The series jumps from 3.x to 4.1 and proceeds with .x increments. The user-facing question about "Claude Sonnet 4 / Claude Opus 4 / Claude Haiku 4.5" maps in practice to **Sonnet 4.5 or 4.6 / Opus 4.1 or higher / Haiku 4.5**. All are present in Foundry.
- **GA vs preview labels move quickly.** Microsoft Foundry release notes shipped material changes monthly through Q1 2026 (e.g., February 2026 added Opus 4.6 and Sonnet 4.6 to preview; Opus 4.7 followed in April). Re-validate model availability against `learn.microsoft.com/en-us/azure/foundry/foundry-models/how-to/use-foundry-models-claude` immediately before procurement.
- **The Anthropic C# SDK (`Anthropic` v10+) is itself in beta.** "APIs may change between versions." Pin specific versions and budget for upgrade work.
- **`Microsoft.Agents.AI.Anthropic` is in preview/RC.** Most recent observed versions: `1.0.0-rc5` (December 2025), then `1.3.0-preview.260423.1` (April 24 2026). MAF's core `Microsoft.Agents.AI` is GA at 1.0; the Anthropic adapter package itself is not yet GA.
- **Latency, throughput, and cost numbers in this report are list-price and qualitative.** Production benchmarks should be run against the bank's own prompt mix, especially since Opus 4.7's new tokenizer can produce up to 35% more tokens for the same input, materially affecting effective cost vs. Opus 4.6.
- **Content Safety coverage for Anthropic on Foundry remains an active area.** Microsoft Q&A threads from late 2025 / early 2026 give inconsistent answers about whether the Foundry "Guardrails" Control Plane fully covers Claude deployments. Validate the exact filter set in the bank's tenant before accepting Foundry Anthropic for in-scope workloads.
- **Foundry Agent Service vs MAF.** Foundry Agent Service's portal-managed agents (`create_agent` / threads / runs) do **not** support Claude as the underlying model as of May 2026. Plan the bank's agent estate around MAF's `ChatClientAgent` pattern if Claude is in scope; reserve Foundry Agent Service hosted agents for OpenAI-only paths or the GitHub Copilot / Claude Code SDKs.
- **EU residency timeline is unsigned.** Anthropic's "Coming 2026" commitment for Microsoft Foundry EU is not a contractual SLA. Build the bank's architecture so that switching the inference provider for Claude (Foundry US ↔ Bedrock EU ↔ Vertex EU) is a configuration change, not a re-architecture.