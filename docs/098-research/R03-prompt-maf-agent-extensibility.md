# R03: MAF Agent Extensibility — Custom Agents, Context Injection, and Template Model

## Prompt for claude.ai

---

I am rebuilding an AI agent orchestration platform in C# using Microsoft Agent Framework (MAF). The existing Python system has a rich agent catalog with 15+ specialised agents, each with versioned prompts, configurable tools, model assignments, constraints, and execution modes. On top of that, a separate enterprise TRD defines a template inheritance model where agent templates inherit from base templates with monotonic guardrails.

I need to understand **exactly how far MAF's agent abstractions can be extended** in C#. Give me concrete patterns with code sketches — not just concepts.

### What Our Agent Catalog Looks Like

Each agent in our catalog has these properties (from the existing system):

```
agent_name: "security.reviewer"
agent_type: "specialist"
version: "2.1.0"
description: "Reviews code for security vulnerabilities"
system_prompt: "You are a senior security engineer..." (versioned, stored in DB)
model_config: { provider: "anthropic", model: "claude-sonnet-4", temperature: 0.0, max_tokens: 8192 }
tools: ["file_read", "file_search", "web_search", "code_execute"]
scope: { file_patterns: ["**/*.py", "**/*.js"], excluded_paths: [".git", "node_modules"] }
constraints: { max_budget_usd: 1.0, max_input_length: 32000, timeout_seconds: 600 }
thinking_budget: { enabled: true, max_tokens: 16384 }
execution_mode: "standard" | "streaming" | "batch"
produces_artifact: true
artifact_type: "vulnerability_report"
```

### What the Enterprise Template Model Adds

The TRD defines template inheritance:

```
base.agent → maker.base → onboarding.screening.uk
```

With these rules:
- Scalar fields: child wins
- Tools/skills lists: merged by key, child overrides parent for same key
- data_scopes: child may narrow but never widen
- guardrails: additive and monotonic — child may add, never remove
- supervision: child may require more approvals, never fewer
- The resolved template is hashed and pinned at deployment time

### Questions — Answer Each with a C# Code Pattern

**Q1: Custom AIAgent subclass**
How do I create a custom `AIAgent` subclass in MAF C# that wraps all of the catalog properties above? Show the class skeleton with the key override points. Can I override `RunAsync` to inject custom pre/post logic (budget check, artifact production, cost recording)?

**Q2: Agent factory from database catalog**
Show a pattern for a factory that reads agent definitions from a database (EF Core) and constructs MAF `AIAgent` instances at runtime. The factory must:
- Load system prompt from DB (versioned)
- Configure the correct IChatClient provider (Anthropic or OpenAI) based on model_config
- Register the correct tools based on the agent's tool list
- Apply constraints (budget, timeout) via middleware

**Q3: Context injection before agent execution**
Our system assembles a multi-layer context packet before each agent run:

```
Layer 0: Mission Context Document (MCD) — always included, never trimmed
Layer 1a: Task definition — always included
Layer 1b: Resolved upstream task inputs — summarized if over budget
Layer 2: Execution history — trimmed first
Layer 2.5: Learnings — skipped if budget < threshold
Layer 3: Code Map — only for coding tasks, token-capped
```

How do I inject this assembled context into a MAF agent run? Options:
- Prepend as system message?
- Use MAF context providers?
- Agent run middleware that modifies the messages collection?
- Custom `AgentSession` with pre-loaded state?

Show the recommended pattern with code.

**Q4: Prompt layering (5-layer assembly)**
Our system prompts are assembled from 5 ordered layers:

1. Organization prompt (global, from disk)
2. Category prompt (per agent type, from disk)
3. Agent system prompt (per agent, from DB, versioned)
4. Mission brief (per mission, from DB)
5. User prompt (per agent per mission, from DB)

How should this compose in MAF? Is it one concatenated `instructions` string? Or multiple system messages? Show the assembly pattern.

**Q5: Template inheritance with monotonic guardrails**
Show a C# pattern for resolving a template inheritance chain where:
- A `SecurityReviewerTemplate` inherits from `SpecialistBase` which inherits from `AgentBase`
- Guardrails are additive (child can't remove parent guardrails)
- Data scopes are intersective (child can't widen parent scopes)
- The resolved template is hashed for immutability

This doesn't need to be MAF-specific — it's a domain model pattern. But show how the resolved template produces a MAF `AIAgent`.

**Q6: AgentSession extensions**
MAF's `AgentSession` has a `StateBag`. Can I use it to carry:
- Rolling summary (string, updated after compression)
- Token counts (input/output cumulative)
- Cost tracking (USD cumulative)
- Channel bindings (which surfaces are connected to this session)
- Mission context reference

Or do I need a custom session class? Show the pattern.

**Q7: Agent-as-Tool composition**
MAF supports `.AsAIFunction()` to use one agent as a tool for another. Our Director agent orchestrates Planner, Coder, and Reviewer. Show the pattern for:
- Director agent has Planner, Coder, Reviewer as function tools
- Each sub-agent maintains its own session/state
- The Director can route dynamically based on task type

### Output Format

For each Q1-Q7:
- 10-30 line C# code sketch (compilable intent, not pseudocode)
- One paragraph explaining the pattern and any MAF limitations encountered
- Flag anything that MAF doesn't support today (where we'd need to roll our own)

Keep total response under 3000 words. Code is preferred over prose.

---

## After Research

Save claude.ai's response as: `docs/098-research/R03-response-maf-agent-extensibility.md`
