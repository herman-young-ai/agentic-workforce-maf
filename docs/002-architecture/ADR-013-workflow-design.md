# ADR-013: Workflow Design — Editable Graphs with Human and AI Decision Points

**Status:** Accepted
**Date:** 2026-05-10
**Decision Makers:** Architecture team

---

## Context

The Agentic Workforce Platform needs deterministic, repeatable processes for regulated operations (onboarding, security assessments, reconciliation). These processes must be:

- **Created and edited** by both humans (visual graph editor) and AI (Workflow Agent via chat)
- **Deterministic** — the steps, order, and decision points are defined, not emergent
- **Decision-aware** — explicit points where a human or an AI makes a specific choice that determines the path
- **Visual** — rendered and editable as an interactive directed graph in the React frontend
- **Versioned** — every edit creates a new version; executions pin to a specific version
- **Auditable** — every decision (human or AI) is a recorded, traceable event

## Decision

**Stored workflow definitions as editable directed graphs, interpreted at runtime by a generic Durable Task orchestrator, with typed decision nodes for human-in-the-loop and AI-in-the-loop branching.**

### Three Layers

| Layer | What | How |
|-------|------|-----|
| **Definition** | The blueprint — a stored, versioned, editable directed graph | JSON in `WorkflowDefinition` entity (nodes + edges + canvas state) |
| **Authoring** | How workflows are created and modified | Visual graph editor (React Flow) + Workflow Agent (chat-based) |
| **Execution** | How workflows run | `WorkflowInterpreter` reads the graph and orchestrates via Durable Task + MAF agents |

---

## Node Types

| Node Type | What It Does | Output | Decision? |
|-----------|-------------|--------|-----------|
| **Agent Task** | Runs a specific agent with an objective | Structured result (data, pass/fail) | No |
| **Human Decision** | Pauses, presents choices to a human via UI/SignalR/Telegram | The human's choice (string/enum) | **Yes** |
| **AI Decision** | Runs a classifier/evaluator agent that answers a specific question | Structured decision (enum from constrained schema) | **Yes** |
| **Parallel Split** | Fans out to multiple paths simultaneously | — | No |
| **Parallel Join** | Waits for all parallel paths to complete | Combined results | No |
| **Sub-workflow** | Invokes another workflow definition | Sub-workflow outcome | No |
| **Action** | Simple operation: notify, update PCD, create artifact, set variable | — | No |
| **Start** | Entry point | — | No |
| **End** | Terminal — marks success, failure, or specific outcome | — | No |

### Decision Nodes — The Key Concept

Decision points are **designed into the graph** — you can see them, audit them, and know exactly where a human or AI made a choice. This is different from agents freelancing; the decision point is explicit and its outputs are constrained.

**Human Decision:**
- Publishes a `HumanInputRequest` (→ SignalR notification to UI, Telegram, etc.)
- Pauses via Durable Task `WaitForExternalEvent<string>("decision", timeout)`
- The human's response determines which outgoing edge to follow
- Timeout triggers escalation or failure (configurable per node)
- **Segregation of duties enforced** — if the workflow was triggered by an operator, only a reviewer can approve at the decision point

**AI Decision:**
- Runs an agent with a **constrained output schema** (`RunAsync<TDecision>()`)
- The agent answers a specific, designed question (e.g., "Are there critical findings?")
- The structured output determines which edge to follow
- The agent cannot invent new paths — it must choose from the defined options
- **Auditable** — the agent's reasoning is recorded alongside the decision

```
Example: Security Assessment Workflow

┌───────┐    ┌──────────────┐    ┌───────────────┐    ┌──────────────┐
│ Start │───▶│ Agent Task   │───▶│ AI Decision   │───▶│ Human        │
│       │    │ "Run OWASP   │    │ "Classify     │    │ Decision     │
│       │    │  scan"       │    │  severity"    │    │ "Approve     │
└───────┘    └──────────────┘    └───┬───────┬───┘    │  remediation │
                                     │       │        │  plan?"      │
                              critical│  acceptable  └──┬─────┬─────┘
                                     │       │          │     │
                                     ▼       ▼       approve reject
                              ┌──────────┐ ┌─────┐    │     │
                              │ Agent    │ │ End │    ▼     ▼
                              │ Task     │ │ OK  │ ┌─────┐┌──────┐
                              │"Remediate│ └─────┘ │Agent││ End  │
                              │ now"     │         │Task ││Reject│
                              └──────────┘         │"Fix"│└──────┘
                                                   └─────┘
```

---

## Workflow Definition Schema

### Entity

```csharp
public class WorkflowDefinition
{
    public Guid Id { get; set; }
    public Guid? ProjectId { get; set; }           // null = platform template
    public string Name { get; set; }
    public string Description { get; set; }
    public int Version { get; set; }
    public bool Enabled { get; set; }

    // The graph
    public JsonDocument Nodes { get; set; }         // typed node definitions
    public JsonDocument Edges { get; set; }          // connections with conditions
    public JsonDocument CanvasState { get; set; }    // visual editor state

    // Governance
    public string? DesignedBy { get; set; }          // human user or "workflow-architect-agent"
    public DateTimeOffset? LockedAt { get; set; }    // immutable once locked for execution
    public string? DesignedByAgent { get; set; }     // if AI-authored, which agent
}
```

### Node JSON

Agent Task node:
```json
{
  "id": "scan-auth",
  "type": "agent_task",
  "label": "Scan authentication module",
  "agent": "security.reviewer",
  "objective": "Scan /src/auth for OWASP Top 10 vulnerabilities",
  "position": { "x": 400, "y": 200 },
  "config": {
    "timeout_seconds": 600,
    "budget_usd": 2.00
  }
}
```

Human Decision node:
```json
{
  "id": "review-findings",
  "type": "human_decision",
  "label": "Review scan findings",
  "prompt": "The security scan found {{findings_count}} issues. Review the report and decide.",
  "choices": ["approve", "reject", "escalate"],
  "timeout_seconds": 14400,
  "escalation_timeout_seconds": 86400,
  "required_role": "reviewer",
  "position": { "x": 600, "y": 200 }
}
```

AI Decision node:
```json
{
  "id": "classify-severity",
  "type": "ai_decision",
  "label": "Classify finding severity",
  "agent": "quality.verifier",
  "question": "Based on the scan results, are there any critical or high-severity findings that require immediate remediation?",
  "output_schema": {
    "decision": { "enum": ["critical_found", "acceptable", "needs_review"] },
    "reasoning": { "type": "string" }
  },
  "position": { "x": 600, "y": 300 }
}
```

Action node:
```json
{
  "id": "notify-team-lead",
  "type": "action",
  "label": "Notify team lead",
  "action": "notify",
  "config": {
    "channel": "signalr",
    "message": "Security scan complete for {{project_name}}. {{findings_count}} findings."
  },
  "position": { "x": 500, "y": 400 }
}
```

Sub-workflow node:
```json
{
  "id": "run-remediation",
  "type": "sub_workflow",
  "label": "Execute remediation workflow",
  "workflow_name": "code-remediation-v2",
  "input_mapping": {
    "findings": "{{previous_node.output.findings}}"
  },
  "position": { "x": 800, "y": 200 }
}
```

### Edge JSON

```json
[
  { "id": "e1", "from": "start",           "to": "scan-auth",        "condition": null },
  { "id": "e2", "from": "scan-auth",        "to": "classify-severity", "condition": null },
  { "id": "e3", "from": "classify-severity", "to": "review-findings",  "condition": { "decision": "critical_found" } },
  { "id": "e4", "from": "classify-severity", "to": "end-ok",           "condition": { "decision": "acceptable" } },
  { "id": "e5", "from": "classify-severity", "to": "review-findings",  "condition": { "decision": "needs_review" } },
  { "id": "e6", "from": "review-findings",   "to": "run-remediation",  "condition": { "output": "approve" } },
  { "id": "e7", "from": "review-findings",   "to": "escalate-to-ciso", "condition": { "output": "escalate" } },
  { "id": "e8", "from": "review-findings",   "to": "end-rejected",     "condition": { "output": "reject" } }
]
```

---

## Execution: The WorkflowInterpreter

A generic Durable Task orchestrator that reads a WorkflowDefinition and walks the graph:

```csharp
public class WorkflowInterpreter
{
    public async Task ExecuteAsync(
        TaskOrchestrationContext ctx, WorkflowDefinition definition, ProjectContext project)
    {
        var graph = GraphParser.Parse(definition.Nodes, definition.Edges);
        var state = new WorkflowState();
        var currentNode = graph.StartNode;

        while (currentNode is not EndNode)
        {
            switch (currentNode)
            {
                case AgentTaskNode task:
                    state[task.Id] = await ctx.CallActivityAsync<NodeResult>(
                        nameof(ExecuteAgentTask), new AgentTaskInput(task, project, state));
                    break;

                case HumanDecisionNode decision:
                    await ctx.CallActivityAsync(nameof(PublishHumanInputRequest),
                        new HumanInputInput(decision, project));
                    var humanChoice = await ctx.WaitForExternalEvent<string>(
                        $"decision:{decision.Id}", TimeSpan.FromSeconds(decision.TimeoutSeconds));
                    state[decision.Id] = new NodeResult { Output = humanChoice };
                    break;

                case AiDecisionNode decision:
                    state[decision.Id] = await ctx.CallActivityAsync<NodeResult>(
                        nameof(ExecuteAiDecision), new AiDecisionInput(decision, project, state));
                    break;

                case ParallelSplitNode split:
                    var parallelTasks = graph.GetOutgoingNodes(split)
                        .Select(n => ctx.CallSubOrchestratorAsync(
                            nameof(ExecuteSubGraph), new SubGraphInput(n, graph, project, state)));
                    await Task.WhenAll(parallelTasks);
                    break;

                case SubWorkflowNode sub:
                    var subDef = await ctx.CallActivityAsync<WorkflowDefinition>(
                        nameof(LoadWorkflowDefinition), sub.WorkflowName);
                    state[sub.Id] = await ctx.CallSubOrchestratorAsync<NodeResult>(
                        nameof(ExecuteAsync), new { Definition = subDef, Project = project });
                    break;

                case ActionNode action:
                    await ctx.CallActivityAsync(nameof(ExecuteAction),
                        new ActionInput(action, project, state));
                    break;
            }

            // Route to next node based on edge conditions
            currentNode = graph.ResolveNextNode(currentNode, state[currentNode.Id]);
        }
    }
}
```

**Key properties:**
- **Generic** — the interpreter doesn't know about specific workflows at compile time
- **New workflows execute without code deployment** — create in the editor, run immediately
- **Durable** — every node completion is a Durable Task checkpoint; survives pod restarts
- **Each agent task is a Durable Task activity** — independently retriable
- **Decision outputs are recorded in workflow state** — fully auditable

---

## Authoring

### Visual Graph Editor (React Frontend)

Built with React Flow (or similar graph library):

| Area | What |
|------|------|
| **Palette** (left) | Drag node types onto the canvas |
| **Canvas** (centre) | Interactive directed graph — nodes and edges |
| **Properties** (right) | Configure selected node: agent, objective, choices, timeout, budget |
| **Edge conditions** | Click edge to set condition (which output routes here) |
| **Chat panel** | Talk to Workflow Agent to modify the graph via natural language |
| **Version history** | Previous versions, diff, rollback |
| **Validate** | Check graph validity before saving |
| **Run** | Execute against the current project |

### Workflow Agent (AI Authoring)

A specialised agent that helps design workflows via chat:

```
User: "I need a workflow for security assessments"

Agent: "Here's a proposed workflow:
  1. Planner scans the codebase and produces findings
  2. AI Decision: classify severity (critical / acceptable / needs_review)
  3. If critical → Human Decision: CISO approves remediation plan
  4. Coder implements fixes
  5. Verifier confirms fixes

  [Graph proposal attached]
  Shall I create this?"

User: "Add a notification to the team lead after the scan"

Agent: [modifies graph, adds Action node, returns updated proposal]
```

The agent produces a `graph_proposal` (nodes + edges JSON) that the frontend renders as a visual preview. On approval, it's saved as a WorkflowDefinition.

### Graph Validation

Before saving or executing, the graph is validated:

| Check | Rule |
|-------|------|
| Reachability | All nodes reachable from Start |
| Termination | All paths reach an End node |
| Decision coverage | Decision nodes have at least 2 outgoing edges |
| Edge conditions | Every outgoing edge from a decision node has a condition |
| Default edge | Decision nodes should have a default/fallback edge |
| Agent existence | Referenced agents exist in the catalog |
| No cycles without exit | Loops must have an exit condition |
| Sub-workflow existence | Referenced sub-workflows exist and are enabled |
| Budget feasibility | Sum of per-node budgets fits within project budget |

---

## Workflow Scoping

| Scope | `ProjectId` | Who Can Use | Example |
|-------|-------------|-------------|---------|
| **Platform template** | `null` | Any project can instantiate | "Standard Security Assessment", "Basic Onboarding" |
| **Project workflow** | set | Only within that project | "Payments Module OWASP Review" |

Platform templates are created by Platform Admins. Project workflows are created by Project Owners/Operators. Both can be authored via the visual editor or the Workflow Agent.

---

## Template Variables and Data Flow

Nodes can reference outputs from previous nodes and project context via template syntax:

```
{{project.name}}                    — project name
{{project.objective}}               — project objective
{{project.pcd.identity.tech_stack}} — PCD field
{{scan-auth.output.findings_count}} — output from node "scan-auth"
{{scan-auth.output.findings}}       — structured data from previous node
{{classify-severity.decision}}      — AI decision output
{{review-findings.output}}          — human choice
```

The interpreter resolves these from `WorkflowState` before passing to each node.

---

## Execution Observability

Each workflow run produces:

| What | Where |
|------|-------|
| Node-by-node execution timeline | `WorkflowRun` entity + events |
| Decision audit trail | Each decision (human or AI) recorded with timestamp, who/what decided, reasoning |
| Cost per node | LLM calls tagged with `workflow_run_id` + `node_id` |
| Visual execution state | Frontend renders the graph with node statuses (pending/running/completed/failed) in real-time via SignalR |
| Durable Task dashboard | DTS provides Gantt/sequence views of the orchestration |

---

## Consequences

- **WorkflowInterpreter** is a new service — a generic Durable Task orchestrator that reads and walks any graph definition
- Workflows are **data, not code** — new processes can be created and executed without code deployment
- **AI Decision** nodes use `RunAsync<T>()` with constrained output schema — the agent must choose from defined options, not invent paths
- **Human Decision** nodes enforce **segregation of duties** via `required_role` and `triggered_by != approved_by`
- Graph validation prevents invalid workflows from being saved or executed
- Visual editor requires React Flow (or similar) integration in the frontend
- **Sub-workflows** enable composition — complex processes are built from smaller, tested workflows
- Template variables create coupling between nodes — validation must check that referenced node outputs exist
- Workflow versioning means running executions pin to the version they started with — mid-flight edits create a new version, not modify the running one

### Principle Compliance

- **P14 Secure by Default:** New workflows default to disabled/draft — only explicitly enabled workflows can be executed. Workflow Agent (AI authoring) output requires human approval before saving.
- **P15 Backend Owns All Logic:** The `WorkflowInterpreter` runs entirely server-side. The React Flow editor is a visual authoring tool only. Graph validation, edge resolution, and execution decisions never run in the client.
- **P16 Single Source of Truth:** The `WorkflowDefinition` entity in the database is the single authoritative source for a workflow's graph. The React Flow canvas state is a rendering artifact, not the source of truth. If they diverge, the DB wins.
- **P18 Idempotency:** Each activity call in the interpreter (`ExecuteAgentTask`, `ExecuteAction`, etc.) is idempotent. Durable Task replays orchestrations on recovery, so activities may be called multiple times for the same node.
- **P19 Bounded Resource Usage:** Explicit limits: max nodes per workflow, max parallel branches, max sub-workflow depth, and per-node timeout enforcement. The interpreter enforces a global workflow execution timeout.
- **P20 Version Everything:** Node/edge JSON schema is versioned so the interpreter can handle older workflow definitions after schema evolution. Running executions pin to the version they started with.
- **P21 Explicit Over Implicit:** Template variable resolution (`{{node.output.field}}`) fails loudly if a referenced variable doesn't exist — never silently resolves to empty. All available template variables are declared, not discovered.
