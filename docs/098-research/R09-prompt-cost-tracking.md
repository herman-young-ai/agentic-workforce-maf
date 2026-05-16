# R09: Cost Tracking, Budget Enforcement, and FinOps

## Prompt for claude.ai

---

I am building an AI agent platform in C# using Microsoft Agent Framework (MAF) on Azure. The platform runs multiple AI agents per mission, each making LLM calls. We need real-time cost tracking, per-agent/per-mission budget enforcement, and FinOps dashboards. This is for a bank — cost overruns are unacceptable.

Give me **concrete implementation patterns** in MAF C#. Code sketches, not concepts.

### Our Cost Tracking Requirements

**Per-call recording:**
Every LLM call must record: agent_name, model, provider, input_tokens, output_tokens, cache_read_tokens, cache_creation_tokens, cost_usd, latency_ms. Cost is calculated from a model pricing table (price per MTok, with cache discounts).

**Budget hierarchy:**

| Level | Budget Type | Enforcement |
|---|---|---|
| Per-call | No explicit budget | Record cost |
| Per-agent per-execution | Configurable ceiling (default $1.00) | Hard stop — terminate agent if exceeded |
| Per-execution | Configurable ceiling | Hard stop — fail execution |
| Per-session | Configurable ceiling (default $50.00) | Warning at 80%, hard stop at 100% |
| Per-mission | Configurable ceiling | Warning at 80%, emergency model downgrade at 90%, hard stop at 100% |
| Per-hour platform-wide | Alert threshold ($5.00/hr) | Alert only (no auto-stop) |

**Emergency cost controls:**
- Budget exceeded at 90% → automatically downgrade to cheapest model (e.g. Claude Haiku)
- Budget exceeded at 100% → terminate execution, notify user
- Anomaly detection: if a single agent call costs >$1, flag it
- Emergency stop: admin can pause all autonomous executions platform-wide

**Model pricing table:**

| Model | Input $/MTok | Output $/MTok | Cache Read $/MTok | Cache Create $/MTok |
|---|---|---|---|---|
| claude-sonnet-4 | 3.00 | 15.00 | 0.30 | 3.75 |
| claude-haiku-4.5 | 0.80 | 4.00 | 0.08 | 1.00 |
| gpt-4o | 2.50 | 10.00 | — | — |
| gpt-4o-mini | 0.15 | 0.60 | — | — |
| text-embedding-3-small | 0.02 | — | — | — |

Prices change — must be configurable, versioned with effective_from/effective_to dates.

**Dashboard metrics needed:**
- Total cost by mission, by agent, by model, by time period
- Token economics: input/output ratio per agent (detect verbose agents)
- Cache hit rate and savings
- Cost timeline (hourly aggregation for charts)
- Top N most expensive calls
- Per-agent utilisation (runs, cost, duration)

### Questions — Answer with Code

**Q1: MAF IChatClient middleware for cost recording**
Show the middleware that:
- Intercepts every LLM call
- Extracts token usage from the response
- Looks up the model in the pricing table
- Calculates cost
- Writes an LlmCall record (async, non-blocking)
- Checks cumulative cost against budget and throws if exceeded

Show complete C# middleware function (~20-30 lines).

**Q2: Budget enforcement service**
Show a `BudgetService` that:
- Tracks cumulative cost per execution, per session, per mission (in-memory + DB)
- Returns `BudgetCheckResult` with status (ok, warning, downgrade, exceeded)
- Thread-safe for concurrent agent executions within the same mission

**Q3: Model pricing lookup**
Show the EF Core entity for `ModelPricing` with composite key (model, effective_from) and a service that resolves current price for a given model at a given timestamp.

**Q4: Emergency model downgrade**
Show how MAF middleware can dynamically switch the model in `ChatOptions` when budget hits 90%:
- Original request: claude-sonnet-4
- Budget at 91%: transparently downgrade to claude-haiku-4.5
- Log the downgrade event

**Q5: Token usage extraction from MAF response**
Does MAF's `ChatResponse` / `AgentResponse` expose token usage (InputTokenCount, OutputTokenCount)? If it comes from the underlying `IChatClient`, show how to access it. If it varies by provider (Anthropic vs OpenAI), show both.

**Q6: Cost aggregation queries**
Show 2-3 EF Core LINQ queries for:
- Total cost per agent for a mission in the last 24 hours
- Hourly cost timeline for charting
- Cache hit rate (cache_read_tokens / total_input_tokens) per agent

### Output Format

For each Q1-Q6:
- C# code sketch (10-30 lines)
- One paragraph on the pattern

Then:
- **Architecture summary**: where cost data flows (middleware → service → DB → dashboard API)
- Any MAF limitations for cost tracking (e.g. does the Anthropic provider expose cache tokens?)

Keep total response under 2500 words. Code preferred.

---

## After Research

Save claude.ai's response as: `docs/098-research/R09-response-cost-tracking.md`
