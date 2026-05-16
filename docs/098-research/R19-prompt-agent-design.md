# R19: Agent Design — Catalog, Prompts, Tools, Constraints, and Runtime Model

## Prompt for claude.ai

---

You are a senior AI platform architect designing the complete agent model for the **Agentic Workforce Platform** — an AI agent orchestration system for a regulated bank (Investec), built in C# using Microsoft Agent Framework (MAF) 1.5.0, deployed on Azure Container Apps.

The platform deploys teams of specialised AI agents within projects. Each agent has a catalog definition (stored in DB), a layered system prompt (assembled from files + DB), constrained tools, scoped file access, and a configurable model. Agents are the "workers" — Tasks (the platform's primitive) are what they execute.

### What We've Already Decided

**Agent runtime (ADR-003):**
- `ChatClientAgent` is sealed — we compose, don't subclass
- `AgentFactory` reads catalog from DB and constructs `ChatClientAgent` instances at runtime
- `AIContextProvider` injects per-turn context (PCD, learnings, task definition, etc.)
- IChatClient middleware pipeline: Budget → Audit → FunctionInvocation → ContentSafety → OTel → Provider
- Tools registered via `AIFunctionFactory.Create()` from C# methods

**Prompt layering (5 layers, from ADR-010):**
1. Organization prompts (disk, global — principles, coding standards, security posture)
2. Category prompt (disk, per agent type — e.g., "software", "research", "security")
3. Agent system prompt (DB, versioned — the agent's core instructions)
4. Project brief (DB, per project — the project's objective and context)
5. User prompt (DB, per agent per project — human-authored fine-tuning additive to system prompt)

**Constraints:**
- Agents are pinned to a specific model — no runtime model swapping
- Tools are explicitly allowlisted per agent — empty manifest = zero tools
- File scope (read/write paths) is enforced at the tool implementation level
- Budget ceiling per agent per execution
- Learnings from agents start as `pending` — require human promotion to `active`

### What We Need From the Prototype

The Mission Control prototype has a mature agent design. Here's what it uses:

**Agent YAML config (per agent):**
```yaml
agent_name: security.webapp.scanner.agent
agent_type: vertical                    # vertical (domain-specific) or horizontal (cross-domain)
description: "Deterministic SAST orchestrator..."
enabled: true
chat_enabled: false                      # can humans chat with this agent directly?
version: "1.0.0"

model:
  name: "copilot:claude-haiku-4.5"
  temperature: 0.0
  max_tokens: 8192

tools:
  - security.code.scan
  - security.code.lint
  - security.deps.scan

max_input_length: 16000
max_budget_usd: 1.50

execution:
  mode: container                        # local | container | worktree
  template: "security-assessment-worker:latest"

invocation:
  tier: user                             # user | system | platform

scope:
  read:
    - "@workspace/target/"
  write:
    - "@workspace/evidence/sarif/"

interface:
  input:
    findings: array
    sub_questions: array
  output:
    covered_questions: array
    uncovered_question_ids: array

produces_artifact: false

keywords:                                # for routing/discovery
  - security
  - scanner

thinking_budget:
  max_thinking_tokens: 10000
```

**Prompt structure on disk:**
```
config/prompts/
├── organization/           # Layer 1: global
│   ├── principles.md
│   ├── coding_standards.md
│   └── security_posture.md
├── categories/             # Layer 2: per category
│   ├── software.md
│   ├── research.md
│   ├── security.md
│   └── system.md
└── agents/                 # Layer 3: per agent
    ├── mission/planning/
    │   ├── system.md       # default system prompt
    │   └── chat.md         # variant for chat mode
    ├── security/webapp/scanner/
    │   └── system.md
    └── research/strategist/
        └── system.md
```

**Per-project user prompt (Layer 5, from DB):**
- Stored on `ProjectAgent` junction table (project ↔ agent catalog)
- Versioned in `PromptVersion` table
- Additive — appended after the system prompt, never replaces it
- Example: "For this project, focus on OWASP Top 10 specifically. The codebase uses Dapper for data access — check for SQL injection in Dapper queries."

### What I Need You to Design

Design the complete agent model for the C# / MAF platform. Take what works from the prototype and improve it for MAF.

**SECTION 1: Agent Catalog Entity — Complete Schema**

Design the `AgentCatalog` entity with every field. Consider:
- Agent identity (name, type, version, description, keywords)
- Model configuration (provider, model, temperature, max_tokens, thinking budget)
- Tool manifest (explicit list of allowed tool names)
- File scope (read paths, write paths — with `@workspace/` prefix support for sandbox paths)
- Interface contract (typed input/output schemas — what the agent accepts and produces)
- Constraints (max budget, max input length, timeout, max tool calls, max retries)
- Execution mode (which sandbox — local dev, Dynamic Sessions, ACI)
- Invocation tier (who can invoke — user, system, platform)
- Chat enablement (can humans chat with this agent directly?)
- Artifact production (does this agent produce artifacts? what type?)
- Visibility (public, private, internal — who can see this agent in the catalog?)
- Status (enabled, disabled, deprecated)

Show the C# entity class with all properties.

**SECTION 2: Agent Categories and Types**

Define the agent taxonomy:
- What are the valid agent types? (horizontal vs vertical? or a richer taxonomy?)
- What are the valid categories? (software, research, security, system, mission — or more?)
- How do categories map to prompt layer 2?
- Can users define custom categories?

**SECTION 3: Prompt Assembly in C# / MAF**

Design how the 5-layer prompt is assembled and delivered to MAF's `ChatClientAgent`:
- Layer 1: Organization prompts — loaded from embedded resources or config files at startup
- Layer 2: Category prompt — loaded from disk based on agent category
- Layer 3: Agent system prompt — loaded from DB (`AgentCatalog.SystemPrompt` or `PromptVersion`)
- Layer 4: Project brief — loaded from `Project.Brief`
- Layer 5: User prompt — loaded from `ProjectAgent.UserPrompt`

Show the C# `PromptAssembler` class. Consider:
- How does this compose into MAF's single `Instructions` string vs `AIContextProvider`?
- Should layers 1-3 be the static `Instructions` and layers 4-5 be dynamic via `AIContextProvider`?
- How to handle prompt variants (e.g., `system.md` vs `chat.md` for the same agent)?
- How to version prompt changes (PromptVersion table)?

**SECTION 4: Tool Manifest and Scoping**

Design how tools are constrained per agent:
- The agent catalog lists tool names (strings). How do these map to actual `AIFunction` implementations?
- Is there a central `ToolRegistry` that maps tool names to implementations?
- How is file scope (read/write paths) enforced? Is it in the tool implementation or a middleware?
- Can per-project tool overrides exist? (e.g., this agent gets an additional tool for this project)
- How do MCP tools integrate with the tool manifest?
- What happens when an agent tries to call a tool not in its manifest? (fail fast per Principle 8)

Show the `ToolRegistry` and `FileScope` enforcement code.

**SECTION 5: AgentFactory — Runtime Construction**

Design the factory that builds MAF agents from catalog entries:
- How does it resolve the IChatClient for the agent's configured provider/model?
- How does it assemble the prompt (calling PromptAssembler)?
- How does it resolve and register tools from the manifest?
- How does it wire the IChatClient middleware pipeline (Budget, Audit, ContentSafety, OTel)?
- How does it create the AIContextProvider for per-turn context injection?
- How does it handle agent variants (system vs chat mode)?
- Caching: are agent instances cached or created fresh per execution?

Show the `AgentFactory` class with the full construction flow.

**SECTION 6: Per-Project Agent Customisation**

Design how agents are customised per project:
- `ProjectAgent` junction table — what fields does it have?
- User prompt (Layer 5) — how is it stored, versioned, and applied?
- Custom constraints per project (override catalog defaults?)
- Custom tool overrides per project (add tools? restrict tools?)
- Role within the project (Supervisor, Researcher, QA, Reporter — from the template)
- Enabled/disabled per project (can an owner disable an agent for their project?)

Show the `ProjectAgent` entity and how it merges with the catalog definition at runtime.

**SECTION 7: Agent Visibility to Other Agents and Humans**

Design how agents see each other and how humans see agents:
- The roster: how does the Planner agent see the available team? (format, fields, constraints)
- The catalog browser: how does the UI show available agents? (what fields are exposed?)
- Agent detail view: what does a human see when inspecting an agent? (prompt visible? tools? constraints?)
- Agent-as-tool: when one agent is used as a tool by another, what's visible?

**SECTION 8: Agent Lifecycle**

Design the lifecycle of an agent definition:
- Creation: how is a new agent added to the catalog? (API? YAML seed? both?)
- Versioning: what changes constitute a version bump? (prompt change? tool change? model change?)
- Deprecation: how is an agent marked as deprecated? What happens to projects using it?
- Retirement: when is an agent removed? Can it be removed while projects reference it?
- Testing: how do you test an agent before publishing? (sandbox/workshop mode)

### Output Format

For each section:
- C# code sketches (10-30 lines, compilable intent)
- Entity/DTO classes where relevant
- One paragraph explaining the design rationale

Then at the end:
- **Agent taxonomy table**: all agent types with their category, model tier, typical tools, typical scope
- **Prompt assembly data flow diagram** (ASCII)
- **Key differences from Mission Control prototype**: what we kept, what we changed, why

Keep total response under 5000 words. Code preferred over prose.

---

## After Research

Save claude.ai's response as: `docs/098-research/R19-response-agent-design.md`
