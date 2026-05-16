# R20: Agent Implementation Design — Base Agent, Naming, Folder Layout, and Future-Proof Agent Library

## Prompt for claude.ai

---

You are a senior .NET architect designing the complete agent implementation architecture for the **Agentic Workforce Platform** — an AI agent orchestration system for a regulated bank, built in C# using Microsoft Agent Framework (MAF) 1.5.0, deployed on Azure Container Apps.

The platform deploys teams of specialised AI agents within projects. We need a **production-ready agent implementation architecture** that is modular, future-proof, and scales to 50+ agents across multiple domains.

### Architectural Decisions Already Made

These are non-negotiable. Design within these constraints.

**Agent runtime (ADR-003, ADR-016):**
- `ChatClientAgent` is sealed — we compose, don't subclass
- `AgentFactory` reads catalog from DB, constructs agents at runtime
- `UseProvidedChatClientAsIs = true` — we compose our own IChatClient pipeline
- `RequirePerServiceCallChatHistoryPersistence = true` — crash recovery + audit
- `AIContextProvider` for per-turn context injection (single provider per agent in MAF 1.5.0)
- IChatClient pipeline shared per `(provider, model)`: Budget → Audit → FunctionInvocation → ContentSafety → OTel → Provider

**Prompt layering (5 layers):**
1. Organization prompts (disk files — global)
2. Category prompt (disk file — per agent category)
3. Agent system prompt (DB — versioned per agent)
4. Project brief (DB — per project)
5. User prompt (DB — per agent per project, additive)

**Tool scoping:**
- ToolRegistry maps tool names (strings) to AIFunction implementations
- Explicit manifest per agent — empty = zero tools (Secure by Default)
- File scope enforced at tool implementation level, not middleware
- MCP tools integrate via manifest (ToolBinding.McpServer)

**Orchestration (ADR-017):**
- Three roles: Project Director (agent), Dispatch Engine (code), Project Supervisor (agent)
- Director = human's delegate, auto-assigned to every project, primary chat agent
- Dispatch Engine = deterministic Durable Task orchestrator, NOT an agent
- Supervisor = post-run classifier (Haiku), structured output: wait/advance/refine/complete/escalate

**Agent categories (fixed):**
- `mission` — orchestration agents (director, planner, supervisor)
- `software` — code analysis, architecture review, quality verification
- `research` — strategist, searcher, analyst, synthesizer
- `security` — scanner, triage, reporter
- `system` — summarization, verification, knowledge officer

**Agent types:**
- `horizontal` — cross-domain, reusable (planner, verifier, summarizer)
- `vertical` — domain-specific specialist (security scanner, code analyst)

**Principles that constrain design:**
- P14: Secure by Default — empty tool manifest = zero tools, new agents disabled by default
- P15: Backend Owns All Logic — all agent construction and execution server-side
- P19: Bounded Resource Usage — every agent has budget, timeout, tool call limits
- P20: Version Everything — agent definitions, prompts, tools all versioned
- P21: Explicit Over Implicit — no auto-discovery of agents, tools, or prompts

### Reference: Mission Control Prototype Structure

The Python prototype organises agents like this:

```
config/
├── agents/                              # Agent YAML definitions
│   ├── mission/
│   │   ├── director/agent.yaml
│   │   ├── planning/agent.yaml
│   │   ├── supervisor/agent.yaml
│   │   └── workflow/agent.yaml
│   ├── research/
│   │   ├── analyst/agent.yaml
│   │   ├── searcher/agent.yaml
│   │   ├── strategist/agent.yaml
│   │   ├── synthesizer/agent.yaml
│   │   └── verifier/agent.yaml
│   ├── security/webapp/
│   │   ├── scanner/agent.yaml
│   │   ├── triage/agent.yaml
│   │   └── reporter/agent.yaml
│   ├── software/
│   │   ├── architecture/agent.yaml
│   │   └── quality/agent.yaml
│   ├── system/
│   │   ├── knowledge_officer/agent.yaml
│   │   ├── summarization/agent.yaml
│   │   ├── synthesis/agent.yaml
│   │   └── verification/agent.yaml
│   ├── platform/
│   │   ├── director/agent.yaml
│   │   └── health/agent.yaml
│   └── mission_control.yaml             # Global config (routing, pricing, limits, guardrails)
│
├── prompts/
│   ├── organization/                    # Layer 1: global prompts
│   │   ├── principles.md
│   │   ├── coding_standards.md
│   │   ├── security_posture.md
│   │   └── communication_style.md
│   ├── categories/                      # Layer 2: per-category prompts
│   │   ├── mission.md
│   │   ├── software.md
│   │   ├── research.md
│   │   ├── security.md
│   │   └── system.md
│   └── agents/                          # Layer 3: per-agent prompts
│       ├── mission/director/system.md
│       ├── mission/planning/system.md
│       ├── mission/planning/chat.md     # variant for chat mode
│       ├── security/webapp/scanner/system.md
│       └── ...
```

Agent names follow a dotted convention: `{category}.{subcategory?}.{name}.agent`
- `mission.director.agent`
- `mission.planning.agent`
- `security.webapp.scanner.agent`
- `research.strategist.agent`
- `system.verification.agent`

### What I Need You to Design

Design the complete C# implementation architecture for the agent library. This is NOT the runtime/execution layer — it's the agent DEFINITIONS, TOOLS, PROMPTS, and INFRASTRUCTURE that the `AgentFactory` consumes.

---

**SECTION 1: Solution Structure — The `AgenticWorkforce.Agents` Project**

Design the folder layout for the shared agent library project that is referenced by both Api and Worker:

```
src/AgenticWorkforce.Agents/
├── ???
```

Consider:
- Where do agent-specific tools live? (per-agent or centralised?)
- Where do agent prompt files live? (embedded resources? separate content folder?)
- Where do organization/category prompts live?
- Where do interfaces for wrapping MAF live? (IAgentRuntime, IAgentFactory)
- Where does the ToolRegistry live?
- Where does the PromptAssembler live?
- Where does the ChatClientFactory (shared IChatClient pipeline builder) live?
- Where do the context providers live?
- How do you organise tools by domain? (security tools, research tools, software tools)
- How do you handle cross-cutting tools? (file read/write, web search, shell execute)
- What about the Director, Supervisor, Planner — do they get special treatment?
- How do you add a brand new agent category in future (e.g., "finance", "compliance") without restructuring?

Show the complete folder tree (30-50 entries) with explanations.

**SECTION 2: Naming Conventions**

Define naming conventions for:
- Agent names in the catalog (`{category}.{subcategory?}.{name}`)
- Agent C# class names (if any — or is everything config-driven?)
- Tool names in the registry (`{category}.{domain}.{action}`)
- Tool C# method names
- Prompt file names and paths
- Category names
- Interface/abstraction names

Provide a naming convention table with examples for each, and rules for when to use subcategories.

**SECTION 3: Base Agent Infrastructure**

Design the reusable infrastructure that ALL agents share:

a) **IAgentRuntime** — the wrapper interface (Principle 4: Wrap the Core)
```csharp
public interface IAgentRuntime
{
    Task<AgentResult> RunAsync(string agentName, string objective, ProjectContext context, CancellationToken ct);
    IAsyncEnumerable<AgentEvent> RunStreamingAsync(string agentName, string objective, ProjectContext context, CancellationToken ct);
}
```

b) **AgentFactory** — the full construction flow with:
- IChatClient pipeline resolution (shared per provider+model)
- Prompt assembly (5-layer)
- Tool resolution from manifest via ToolRegistry
- File scope enforcement
- Context provider creation
- Variant support (system vs chat mode)

c) **ToolRegistry** — central registry mapping tool names to AIFunction factories

d) **PromptAssembler** — loads and assembles the 5-layer prompt

e) **ChatClientFactory** — builds and caches IChatClient pipelines per (provider, model)

f) **ProjectContextProvider** — the single AIContextProvider that handles PCD, learnings, task definition, code map, history

Show the C# code for each (10-25 lines).

**SECTION 4: Tool Organisation**

Design how tools are organised and registered:

a) **Cross-cutting tools** (used by many agents):
- `file.read`, `file.write`, `file.search` — sandboxed file operations
- `shell.execute` — sandboxed shell command execution
- `web.search` — web search with failover chain
- `web.fetch` — URL content extraction

b) **Domain-specific tools** (used by specific agent categories):
- `security.code.scan`, `security.deps.scan` — security scanning tools
- `research.web.search`, `research.extract` — research tools
- `project.get_info`, `project.get_plan` — project management tools (for Director)
- `project.refine_plan`, `project.run_objective` — project action tools (for Director)

c) **MCP tools** (external MCP servers):
- How are they registered in the ToolRegistry?
- How are they resolved at agent construction time?

Show the ToolRegistry implementation and the tool registration pattern in DI.

**SECTION 5: The Three Orchestration Agents — Director, Planner, Supervisor**

Design the implementation for each orchestration agent. These are special because they're built into the platform, auto-assigned to projects, and have platform-level tools.

For each (Director, Planner, Supervisor):
- Agent catalog seed YAML (the definition that's seeded into the DB at startup)
- System prompt file (key sections, not the full prompt)
- Tools (explicit manifest with descriptions)
- How it's auto-assigned to new projects
- How it interacts with the Dispatch Engine

**SECTION 6: Adding a New Agent — The Developer Experience**

Walk through the steps a developer takes to add a brand new agent to the platform:

1. What files do they create?
2. What configuration do they add?
3. What tools do they implement (if any)?
4. What prompt do they write?
5. How do they test it locally?
6. How do they seed it into the catalog?
7. How does it become available to projects?

This should be a step-by-step tutorial that a new developer can follow.

**SECTION 7: Agent Seed Strategy**

Design how agent definitions are seeded into the database:
- YAML seed files → DB catalog entries (like the prototype's `seed-catalog` command)
- Initial migration vs ongoing seeding
- How to handle version upgrades (new prompt, new tools)
- How to handle deprecation/retirement
- Platform vs project-scoped agents

**SECTION 8: Future-Proofing**

Address these scenarios:
- Adding a new category (e.g., "finance") — what changes?
- Adding a subcategory (e.g., "security.cloud") — what changes?
- Adding an agent with custom AIAgent subclass (not ChatClientAgent) — possible?
- Adding an agent backed by A2A (remote) — how does it fit?
- Adding an agent that needs a custom IChatClient pipeline (different from the shared one) — how?
- Supporting 50+ agents across 10 categories — does the structure hold?
- Multi-tenant: different organisations wanting different agents — how?

### Output Format

For each section:
- Complete folder tree or code (compilable intent, not pseudocode)
- Naming convention table with examples
- Decision rationale (1-2 paragraphs max)

Then at the end:
- **Complete folder tree** for `src/AgenticWorkforce.Agents/` (the definitive layout)
- **Agent registration checklist** (what to do when adding a new agent)
- **Key differences from Mission Control prototype** (what we kept, changed, why)

Keep total response under 6000 words. Code and structure preferred over prose.

---

## After Research

Save claude.ai's response as: `docs/098-research/R20-response-agent-implementation-design.md`
