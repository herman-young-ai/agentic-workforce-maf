# R10: Context Assembly Pipeline in C#/MAF

## Prompt for claude.ai

---

I am rebuilding an AI agent platform in C# using Microsoft Agent Framework (MAF). The platform's most critical subsystem is the **context assembly pipeline** — the mechanism that determines what each agent knows before it executes a task. The quality of agent output is directly proportional to the quality of context assembly.

I need **concrete C# implementation patterns** for building this pipeline on top of MAF. Code sketches and architecture — not concepts.

### What Context Assembly Does

Before every agent task execution, the system assembles a multi-layer context packet that is injected into the agent's prompt. The layers are prioritised — when the total exceeds the token budget, lower-priority layers are trimmed or removed.

### Context Layers (priority order — highest = last trimmed)

| Priority | Layer | Source | Size | Trim Behaviour |
|---|---|---|---|---|
| 0 (highest) | Mission Context Document (MCD) | DB (versioned JSON) | 2-15 KB | Never trimmed |
| 1a | Task definition | Execution plan | 200-500 tokens | Never trimmed |
| 1b | Upstream task inputs | Previous task outputs in the DAG | Variable | Summarised if over budget |
| 2.5 | Learnings | DB (mission + platform learnings) | 500-1500 tokens | Skipped if budget < threshold |
| 3 | Code Map | Generated from repo | 2000-8000 tokens | Token-capped; only for coding tasks |
| 2 (lowest) | Execution history | DB (recent failures, runs) | Variable | Reduced or removed first |

### Prompt Layering (separate from context)

The agent's system prompt is assembled from 5 ordered layers:

| Order | Layer | Source | Scope |
|---|---|---|---|
| 1 | Organization prompt | Disk file | Global — all agents |
| 2 | Category prompt | Disk file | Per agent category (specialist, planner, etc.) |
| 3 | Agent system prompt | DB (versioned) | Per agent |
| 4 | Mission brief | DB (missions.brief) | Per mission |
| 5 | User prompt | DB (mission_team_members.user_prompt) | Per agent per mission |

### Session Memory Compression

Long conversations are compressed via rolling summary:
- When messages since last anchor exceed threshold (default 50), fire compression
- A summarisation agent produces a rolling summary of the compressed span
- The summary replaces the old messages in context; anchor moves forward
- Fields tracked: rolling_summary (text), rolling_summary_anchor (message index), rolling_summary_version (int)

### Token Budget Management

| Parameter | Default | Purpose |
|---|---|---|
| context_token_budget | ~100k | Total budget for assembled context |
| history_reserve_tokens | ~2000 | Reserved for execution history |
| learnings_reserve_tokens | 1500 | Reserved for learnings |
| learnings_max_entries | 5 | Max learning entries |
| code_map_max_tokens | ~8000 | Max tokens for code map |

### Questions — Answer with C# Code

**Q1: Token counting in .NET**
How to count tokens for a given text in .NET? Options:
- Microsoft.Extensions.AI tokenizer
- SharpToken (tiktoken port)
- Anthropic tokenizer equivalent
Show the recommended approach with a 5-line code example. How accurate are .NET tokenizers for Claude models?

**Q2: Context assembler service**
Show a `ContextAssembler` class that:
- Takes a `TaskExecutionContext` (mission_id, task definition, agent config, domain_tags)
- Loads each layer from its source (DB/disk/generated)
- Applies token budget with priority trimming
- Returns an assembled `ContextPacket` (system prompt string + context messages)
Show the class skeleton (~30-40 lines) with the assembly algorithm.

**Q3: Injecting context into MAF agent run**
Once the `ContextPacket` is assembled, how to inject it into a MAF `agent.RunAsync()` call?
Options:
a) Concatenate everything into the `instructions` parameter
b) Prepend as system messages in the messages collection
c) Use MAF agent middleware to modify messages before the LLM call
d) Custom `AgentSession` with pre-loaded context

Which is correct for MAF? Show the integration code.

**Q4: MCD as a versioned JSON document**
Show the C# pattern for:
- MCD stored as a JSON column in PostgreSQL (EF Core)
- Path-based mutations (add/replace/remove using dot notation, e.g. "architecture.components.auth")
- Optimistic concurrency on version field
- Restricted paths that agents cannot modify

**Q5: Rolling summary compression**
Show how to implement session memory compression using a MAF agent:
- Detect when compression threshold is reached
- Call a summarisation agent (Haiku) with the message span
- Replace messages in the session with the summary
- Update anchor and version

**Q6: Domain-tag-driven layer inclusion**
Show how domain tags on a task (e.g. `["coding", "security"]`) determine which context layers are included:
- `coding` tag → include Code Map layer
- `security` tag → include security assessment config
- No `coding` tag → skip Code Map entirely

### Output Format

For each Q1-Q6:
- C# code sketch (10-40 lines)
- One paragraph explaining the pattern and any MAF-specific considerations

Then:
- **Data flow diagram** (ASCII): Task → ContextAssembler → [load layers] → [budget trim] → ContextPacket → agent.RunAsync()
- **Key risk**: what's the hardest part of this to get right?

Keep total response under 3000 words. Code preferred.

---

## After Research

Save claude.ai's response as: `docs/098-research/R10-response-context-assembly.md`
