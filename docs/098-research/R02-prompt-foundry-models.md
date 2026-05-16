# R02: Azure AI Foundry — Model Availability, Foundry Anthropic, and Model Governance

## Prompt for claude.ai

---

I am building an AI agent orchestration platform in C# using Microsoft Agent Framework (MAF) deployed on Azure for a regulated bank (Investec). The platform runs multiple specialised AI agents (planner, coder, security reviewer, researcher, architect) that need different models for different tasks.

I need a **concise technical assessment** of Azure AI Foundry as our model gateway. Structured tables and clear answers — not essays.

### Our Model Requirements

| Agent Role | Current Model (Python impl) | Key Capabilities Needed |
|---|---|---|
| Planner / Director | Claude Sonnet 4 | Extended thinking, structured JSON output, tool calling, 200k context |
| Coder | Claude Sonnet 4 | Tool calling (file read/write, git, terminal), streaming, 200k context |
| Security Reviewer | Claude Sonnet 4 | Deep reasoning, structured vulnerability reports, tool calling |
| Researcher | Claude Sonnet 4 | Web search tool integration, large context for synthesis |
| Quality/Architecture | Claude Haiku 4.5 | Fast verification, structured pass/fail output, cost-efficient |
| Summarisation | Claude Haiku 4.5 | Session memory compression, cost-efficient |
| Embeddings | OpenAI text-embedding-3-small | 1536-dimension vectors for knowledge search |
| Emergency fallback | Claude Haiku 4.5 | Budget exceeded — downgrade to cheapest model |

### Questions to Answer

**Section 1: Foundry Anthropic (Claude via Azure)**

| Question | Answer |
|---|---|
| Which Claude models are available via Foundry Anthropic today? | |
| Is Claude Sonnet 4 / Opus 4 available? | |
| Does Foundry Anthropic support extended thinking? | |
| Does Foundry Anthropic support prompt caching? | |
| Does Foundry Anthropic support streaming tool calls? | |
| Does Foundry Anthropic support structured JSON output (tool_choice, response_format)? | |
| What's the max context window via Foundry vs direct Anthropic API? | |
| Pricing: Foundry Anthropic vs direct Anthropic API (per MTok input/output)? | |
| Latency overhead of Foundry proxy vs direct API? | |
| Does data stay within Azure region (important for bank data residency)? | |
| SLA/uptime guarantees from Microsoft for Foundry Anthropic? | |

**Section 2: Azure OpenAI via Foundry**

| Question | Answer |
|---|---|
| Can we deploy GPT-4o and text-embedding-3-small via Foundry? | |
| Foundry Models vs classic Azure OpenAI resource — which to use? | |
| Can we mix Anthropic and OpenAI models in the same Foundry project? | |

**Section 3: Model Governance in Foundry**

| Question | Answer |
|---|---|
| Model versioning — can we pin agent X to model version Y? | |
| A/B testing — can we route % of traffic to different models? | |
| Usage quotas — can we set per-agent or per-team token limits? | |
| Content filtering — does Foundry apply content safety filters? Can we configure them? | |
| Audit logging — does Foundry log all model calls with input/output for compliance? | |
| Cost tracking — does Foundry expose per-call cost data via API? | |
| Rate limiting — how does Foundry handle rate limits across multiple agents hitting the same model? | |

**Section 4: MAF Integration**

| Question | Answer |
|---|---|
| MAF NuGet packages needed for Foundry Anthropic | |
| MAF NuGet packages needed for Azure OpenAI via Foundry | |
| Can MAF `ChatClientAgent` switch models at runtime (e.g. budget-triggered downgrade)? | |
| Does MAF's IChatClient middleware get access to token usage/cost data from the response? | |
| Can a single MAF application use multiple Foundry providers simultaneously (Claude + GPT)? | |

**Section 5: Gaps and Alternatives**

If Foundry Anthropic is missing features we need (e.g. extended thinking, prompt caching), what's the fallback?
- Direct Anthropic API via MAF's `Anthropic` provider (not Foundry)?
- Implications for data residency if we bypass Foundry?
- Can we use Foundry for some agents and direct API for others in the same application?

### Output Format

Answer each table above with concise entries. Then provide:

1. **Recommendation**: Foundry-only, direct-only, or hybrid approach — and why
2. **Risk register**: Top 3 risks of the recommended approach for a regulated bank
3. **Migration path**: If Foundry Anthropic is missing features today, what's the interim architecture while we wait?

Keep total response under 2000 words. Tables preferred.

---

## After Research

Save claude.ai's response as: `docs/098-research/R02-response-foundry-models.md`
