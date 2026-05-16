# ADR-017: Project Orchestration — Director, Dispatch Engine, and Supervisor

**Status:** Accepted
**Date:** 2026-05-12
**Decision Makers:** Architecture team
**Validated against:** Mission Control prototype (`mission/director`, `dispatch/loop.py`, `mission/supervisor`)
**Depends on:** [ADR-001](ADR-001-workflow-engine.md) (Durable Task), [ADR-003](ADR-003-agent-model-design.md) (MAF integration), [ADR-013](ADR-013-workflow-design.md) (Workflows), [ADR-016](ADR-016-agent-design.md) (Agent Design)

---

## Context

The platform needs a coordination model that determines how work gets done within a project. The prototype validated a three-role pattern where the coordination logic is split between agents (for non-deterministic decisions) and code (for deterministic execution). The core principle:

**"Deterministic where possible, with non-deterministic decisions at the edges."**

The coordinator must be code, not an agent. LLM reasoning is reserved for the decisions that genuinely require judgment — not for topological sort, DAG traversal, input resolution, or gate routing.

## Decision

**Three-role orchestration: Project Director (agent) + Dispatch Engine (code) + Project Supervisor (agent)**

```
Human ←→ Project Director (agent — conversational delegate)
              │
              ├── queries state (PCD, history, team, plan)
              ├── delegates planning (→ Planner Agent)
              ├── triggers work ─────────────────────┐
              └── reports results                     │
                                                      ▼
                                              Dispatch Engine (CODE)
                                              ├── topological sort DAG
                                              ├── layer-by-layer execution
                                              ├── upstream input resolution
                                              ├── agent invocation (→ AgentFactory)
                                              ├── verification (tier 1/2/3)
                                              ├── gate checkpoints (HITL)
                                              ├── cost tracking
                                              └── event publishing
                                                      │
                                                      ▼ on completion
                                              Project Supervisor (agent)
                                              ├── evaluates outcomes
                                              └── decides: wait | advance | refine | complete | escalate
                                                      │
                                                      ▼ if advance/refine
                                              Dispatch Engine (next cycle)
```

---

## Role 1: Project Director (Agent)

The human's delegate within a project. **Auto-assigned to every project.** The primary chat agent — when a user opens the Chat tab, they talk to the Director.

### What It Does

The Director is a **manager, not a worker.** It delegates everything. It has tools to query state and trigger work, but it never does specialist work itself.

| Capability | How |
|-----------|-----|
| Answer project questions | Queries PCD, history, team, learnings via tools |
| Trigger ad-hoc work | Calls `run_objective` (creates + dispatches a task) |
| Trigger workflows | Calls `run_workflow` (starts a workflow definition) |
| Manage the plan | Delegates to Planner via `refine_plan`; summarises changes to human |
| Approve tasks | Calls `approve_tasks` (only in `ai_assisted` mode; blocked in `interactive` mode) |
| Start research | Calls `start_research` (delegates to research agents) |
| Report results | Summarises execution outcomes; suggests next steps |
| Manage settings | Calls `add_principle`, `update_budget` on behalf of user |

### What It Does NOT Do

- Does NOT orchestrate agents directly — the Dispatch Engine does that
- Does NOT create task plans — delegates to the Planner Agent via `refine_plan`
- Does NOT do specialist work (coding, scanning, research) — delegates to specialist agents
- Does NOT bypass the Dispatch Engine — all executions go through the standard pipeline
- Does NOT make up data — always uses tools to query real state
- Does NOT approve tasks in `interactive` gate mode — the user retains full control

### Agent Configuration

```yaml
agent_name: project.director
agent_type: horizontal
category: project
description: >-
  Conversational project delegate. Answers questions about the project,
  triggers runs, manages the plan, and reports results on behalf of the user.
  Manager, not worker — delegates everything.
enabled: true
chat_enabled: true
version: "1.0.0"

model:
  provider: foundry-anthropic
  name: claude-sonnet-4-6
  temperature: 0.0
  max_tokens: 16384

tools:
  # Read tools (query state)
  - project.get_info
  - project.get_team
  - project.get_pcd
  - project.get_history
  - project.get_plan
  - project.list_workflows
  - project.get_artifacts
  - project.get_learnings
  # Plan tools
  - project.refine_plan          # delegates to Planner Agent
  - project.approve_tasks        # only works in ai_assisted mode
  # Action tools
  - project.run_objective        # ad-hoc: create + execute task
  - project.run_workflow         # trigger a named workflow
  - project.start_research       # delegates to research agents
  # Settings tools
  - project.add_principle        # add to PCD principles
  - project.update_budget        # extend project budget

max_budget_usd: 5.00
max_tool_calls: 20
```

### System Prompt (Layer 3 — key excerpt)

```markdown
You are the Project Director. You are the user's delegate — you manage the project on their behalf.

## Your Role
You are a conversational agent with tools. You sit between the user and the platform infrastructure. When the user asks you to do something, you use your tools to make it happen. When they ask questions, you query state and answer from real data.

## Behavioral Guidelines
- **Delegate, don't DIY.** You are a manager, not a worker. For research, use `start_research`. For code tasks, use `run_objective`. Never try to do agent work yourself.
- **Confirm before costly actions.** Before triggering run_workflow, confirm with the user what you're about to do and the budget impact.
- **Use tools for data.** Do not guess or make up information. Always query with tools first.
- **Be concise.** Summarise, don't repeat tool output verbatim.

## What You Do NOT Do
- You do NOT create TaskPlans — the Planner Agent does that (you delegate via `refine_plan`).
- You do NOT call specialist agents directly — the Dispatch Engine handles agent assignment.
- You do NOT bypass the dispatch infrastructure — all executions go through the standard pipeline.
- You do NOT approve tasks in `interactive` mode — the user retains full control.
- You do NOT make up data — always use tools to query real state.
```

---

## Role 2: Dispatch Engine (Code — NOT an Agent)

The deterministic coordinator. Implemented as a Durable Task orchestrator (ADR-001). Pure code — no LLM reasoning.

### Why Code, Not Agent

| Concern | Code (deterministic) | Agent (LLM) |
|---------|---------------------|-------------|
| Topological sort of task DAG | O(V+E), correct every time | Unnecessary LLM call, risk of error |
| Layer-by-layer parallel execution | `Task.WhenAll()` | LLM can't parallelise |
| Upstream input resolution | Dictionary lookup by task ID | LLM might hallucinate input mappings |
| Budget enforcement | Arithmetic comparison | LLM might approximate |
| Gate routing | State machine: check mode, present choices | LLM adds cost and latency |
| Event publishing | Structured event emit | LLM adds nothing |

**Every dollar spent on LLM reasoning for deterministic operations is waste.** The LLM should only be involved when judgment is needed — planning (which tasks to create), verification (quality judgment), and supervision (what to do next).

### What It Does (All Deterministic)

```csharp
public sealed class DispatchEngine
{
    public async Task<DispatchResult> ExecuteAsync(
        Guid projectId,
        TaskOrchestrationContext ctx,    // Durable Task context for durability
        CancellationToken ct)
    {
        // 1. Load approved tasks from DB
        var tasks = await _taskRepo.GetApprovedAsync(projectId, ct);
        if (tasks.Count == 0) return DispatchResult.NothingToDispatch;

        // 2. Topological sort into execution layers
        var layers = TopologicalSort.ByDependencies(tasks);

        // 3. Pre-dispatch gate (if gate mode requires)
        await CheckGateAsync(ctx, "pre_dispatch", tasks, ct);

        // 4. Execute layer by layer
        var results = new List<TaskResult>();
        foreach (var (layerIndex, layer) in layers.Index())
        {
            // Execute tasks in parallel within each layer
            var layerTasks = layer.Select(task =>
                ctx.CallActivityAsync<TaskResult>(
                    nameof(ExecuteTaskActivity),
                    new TaskActivityInput(task, projectId)));

            var layerResults = await Task.WhenAll(layerTasks);
            results.AddRange(layerResults);

            // Post-layer gate (if configured)
            await CheckGateAsync(ctx, "post_layer", layerResults, ct);

            // Budget check — fail fast if exceeded
            var totalCost = results.Sum(r => r.CostUsd);
            var budget = await _budgetService.CheckAsync(projectId, ct);
            if (budget.Status == BudgetStatus.Exceeded)
                return DispatchResult.BudgetExceeded(totalCost, budget.Ceiling);
        }

        // 5. Post-dispatch: publish completion event
        await _eventBus.PublishAsync(new DispatchCompletedEvent(projectId, results));

        return new DispatchResult(results);
    }
}
```

### ExecuteTaskActivity (Durable Task Activity — runs one Task)

```csharp
[Activity]
public async Task<TaskResult> ExecuteTaskActivity(TaskActivityInput input)
{
    var task = input.Task;
    var project = await _projectRepo.GetAsync(input.ProjectId);

    // 1. Resolve upstream inputs from completed tasks
    var inputs = await _inputResolver.ResolveAsync(task, ct);

    // 2. Set project context for this execution
    using var _ = ProjectContext.Set(new ProjectContext
    {
        ProjectId = input.ProjectId,
        AgentName = task.AgentName,
        TaskId = task.Id,
    });

    // 3. Build the agent from catalog (ADR-016)
    var catalog = await _catalogRepo.GetAsync(task.AgentName, ct);
    var projectAgent = await _projectAgentRepo.GetAsync(input.ProjectId, catalog.Id, ct);
    var agent = _agentFactory.Create(catalog, project, projectAgent);

    // 4. Execute the agent
    var session = await agent.CreateSessionAsync(ct);
    var response = await agent.RunAsync(
        FormatTaskPrompt(task, inputs),
        session,
        ct: ct);

    // 5. Record result
    var result = new TaskResult(
        TaskId: task.Id,
        Status: TaskResultStatus.Completed,
        Output: response.Text,
        CostUsd: CalculateCost(response),
        Duration: elapsed);

    // 6. Run verification (deterministic tiers, then optional agent tier)
    result = await _verifier.VerifyAsync(task, result, ct);

    // 7. Persist
    await _taskRepo.UpdateWithResultAsync(task.Id, result, ct);

    // 8. Emit event
    await _eventBus.PublishAsync(new TaskCompletedEvent(task.Id, result));

    return result;
}
```

### Gate Checkpoints

Gates are code-driven state machines, not agent decisions:

```csharp
private async Task CheckGateAsync(
    TaskOrchestrationContext ctx, string gateType, object context, CancellationToken ct)
{
    var project = await _projectRepo.GetAsync(_projectId, ct);
    var gateMode = project.GateMode; // off | interactive | ai_assisted | autonomous

    if (gateMode == "off") return; // no gates

    if (gateMode == "interactive" || gateMode == "ai_assisted")
    {
        // Create a human_decision task
        var gateTask = await _taskRepo.CreateAsync(new AgenticTask
        {
            ProjectId = _projectId,
            Type = TaskType.HumanDecision,
            Status = TaskStatus.Running,
            Objective = $"Review {gateType} checkpoint",
            Source = TaskSource.System,
        }, ct);

        // Publish approval request
        await _eventBus.PublishAsync(new GateRequestedEvent(_projectId, gateTask.Id, gateType));

        // Wait for human response (Durable Task external event)
        var decision = await ctx.WaitForExternalEvent<GateDecision>(
            $"gate:{gateTask.Id}",
            TimeSpan.FromHours(4));  // 4h timeout, escalate to 24h

        if (decision.Action == "abort")
            throw new GateAbortedException(gateType, decision.Reason);
    }
    // autonomous mode: no gates, proceed automatically
}
```

---

## Role 3: Project Supervisor (Agent)

The post-run decision maker. Called automatically after each dispatch cycle completes. Lightweight (Haiku), structured output, low cost (~$0.003 per decision).

### What It Does

Evaluates execution outcomes and decides the next action via **constrained structured output** (AI Decision node pattern from ADR-013):

```csharp
public enum SupervisorDecision
{
    Wait,       // nothing to do — all tasks completed successfully
    Advance,    // approve and dispatch the next wave of tasks
    Refine,     // task failed — delegate to Planner to adjust
    Complete,   // all objectives met — propose project completion
    Escalate    // situation requires human judgment
}

public record SupervisorOutput(
    SupervisorDecision Decision,
    string Reasoning,           // why this decision (auditable)
    string[]? TasksToAdvance,   // if Advance: which tasks to approve
    string? RefinementRequest   // if Refine: what to tell the Planner
);
```

### Agent Configuration

```yaml
agent_name: project.supervisor
agent_type: horizontal
category: project
description: >-
  Post-run decision maker. Classifies outcomes and routes next action.
  Haiku-class — classifies, does not reason deeply. ~$0.003 per decision.
enabled: true
chat_enabled: true        # owner can chat to understand past decisions
version: "1.0.0"

model:
  provider: foundry-anthropic
  name: claude-haiku-4-5
  temperature: 0.0
  max_tokens: 1000

tools:
  - project.get_plan
  - project.get_recent_outcomes
  - project.get_past_decisions
```

### How It's Called

The Supervisor is invoked automatically after the Dispatch Engine completes, as a Durable Task activity:

```csharp
// In the Durable Task orchestrator, after dispatch completes:
var dispatchResult = await ctx.CallActivityAsync<DispatchResult>(
    nameof(DispatchEngine.ExecuteAsync), projectId);

// Invoke Supervisor to decide next action
var supervisorOutput = await ctx.CallActivityAsync<SupervisorOutput>(
    nameof(SupervisorActivity), new SupervisorInput(projectId, dispatchResult));

// Route based on Supervisor's decision (deterministic routing of non-deterministic decision)
switch (supervisorOutput.Decision)
{
    case SupervisorDecision.Wait:
        // Nothing to do — orchestration completes
        break;

    case SupervisorDecision.Advance:
        // Approve specified tasks and dispatch again
        await ctx.CallActivityAsync(nameof(ApproveTasks), supervisorOutput.TasksToAdvance);
        await ctx.CallSubOrchestratorAsync(nameof(DispatchOrchestrator), projectId);
        break;

    case SupervisorDecision.Refine:
        // Delegate to Planner Agent to adjust the plan
        await ctx.CallActivityAsync(nameof(RefinePlan), supervisorOutput.RefinementRequest);
        await ctx.CallSubOrchestratorAsync(nameof(DispatchOrchestrator), projectId);
        break;

    case SupervisorDecision.Complete:
        // Propose completion — notify human
        await ctx.CallActivityAsync(nameof(ProposeCompletion), projectId);
        break;

    case SupervisorDecision.Escalate:
        // Create a human_decision task and wait
        var gateId = await ctx.CallActivityAsync<Guid>(nameof(CreateEscalation), projectId);
        await ctx.WaitForExternalEvent<GateDecision>($"gate:{gateId}", TimeSpan.FromHours(24));
        break;
}
```

### System Prompt (key excerpt)

```markdown
You are the Project Supervisor. You evaluate execution outcomes and decide what happens next.

You are called automatically after each execution cycle. You receive the plan, the recent outcomes, and any past decisions you've made.

Your job is to classify the situation and return a structured decision. You are NOT a planner — you don't create tasks. You are NOT a worker — you don't execute tasks. You CLASSIFY and ROUTE.

## Decision Options
- **wait** — all tasks completed successfully, nothing left to do
- **advance** — some tasks completed, the next wave is ready to run
- **refine** — a task failed, the plan needs adjustment (specify what to tell the Planner)
- **complete** — all objectives appear to be met, propose project completion to the human
- **escalate** — the situation requires human judgment (ambiguous failures, conflicting results, budget concerns)

## Rules
- Always check the plan first (`get_plan`) to see what's left
- Always check recent outcomes (`get_recent_outcomes`) to see what just happened
- If ALL remaining tasks are in `completed` status → `complete`
- If a task failed but it's retriable → `refine` with feedback about what went wrong
- If a task failed and you're unsure whether to retry → `escalate`
- If tasks are proposed/approved and their dependencies are met → `advance`
- Never `advance` a task whose dependencies haven't completed
- Never `advance` more than 2 iterations without human check-in → `escalate`
```

---

## The Orchestration Loop

The complete lifecycle of work within a project:

```
1. Human chats with Director: "Scan the payments module for OWASP Top 10"

2. Director delegates to Planner: refine_plan("Scan payments for OWASP Top 10")
   → Planner creates task DAG in the Task table (status: proposed)

3. Director summarises the plan to the human

4. Human approves tasks (interactive mode) OR Director approves (ai_assisted mode)
   → Task status: proposed → approved

5. Dispatch Engine runs (Durable Task orchestrator):
   a. Topological sort → execution layers
   b. For each layer: execute tasks in parallel
      - Build agent from catalog (AgentFactory)
      - Run agent with context (PCD, learnings, upstream inputs)
      - Verify results (3-tier verification)
      - Record results in Task table
   c. Gate checkpoints between layers (if configured)
   d. Budget enforcement (fail fast if exceeded)

6. Supervisor evaluates outcomes:
   - All done? → complete (notify human)
   - More tasks ready? → advance (approve next wave, re-dispatch)
   - Task failed? → refine (tell Planner to adjust, re-dispatch)
   - Unclear? → escalate (create human_decision task)

7. Loop repeats from step 5 until complete or escalated

8. Director reports final outcome to human
```

### Gate Modes

| Mode | Behaviour | Who Controls |
|------|-----------|-------------|
| `off` | No gates — Dispatch Engine runs without interruption | — |
| `interactive` | Gates pause for human approval at configured checkpoints | Human via Planner Board |
| `ai_assisted` | Director can auto-approve tasks; human can still veto | Director + human veto |
| `autonomous` | Supervisor can advance/refine without human input (max 2 iterations before escalation) | Supervisor (human can interrupt) |

---

## Differences from Mission Control Prototype

| Aspect | Prototype | Our Design |
|--------|-----------|------------|
| Director tools | Mission-specific | Project-scoped, includes settings management |
| Dispatch loop | Python async code | Durable Task orchestrator (survives pod restarts) |
| Supervisor | Session followup primitive | Explicit Durable Task activity after dispatch |
| Task identity | JSON shadow in plan | First-class relational entity (Task is the primitive) |
| Gate mode | 4 modes (off/interactive/ai_assisted/autonomous) | Same 4 modes, same semantics |
| Post-run routing | Supervisor decides via structured output | Same — `wait/advance/refine/complete/escalate` |
| Planner delegation | Director calls `refine_plan` tool | Same pattern |
| Max autonomous iterations | Configurable (`max_supervisor_iterations`) | Hard cap: 2 iterations before escalation |

---

## Consequences

- Director is auto-assigned to every project — it's always the first chat agent
- Dispatch Engine is pure code in a Durable Task orchestrator — no LLM cost for coordination
- Supervisor is Haiku-class (~$0.003 per decision) — cheap enough to call after every dispatch cycle
- Maximum 2 autonomous iterations before escalation — prevents runaway loops (Principle 19: Bounded Resource Usage)
- Gate modes are configurable per project — projects can start interactive and progress to autonomous as trust builds
- Supervisor decisions are auditable — `SupervisorOutput.Reasoning` is recorded for every decision
- The Director-Dispatch-Supervisor loop is the primary execution path — workflows (ADR-013) are a specialisation that predefines the task DAG
- `ExecuteTaskActivity` is a Durable Task activity — independently retriable, idempotent (Principle 18)
- All three roles are visible as Tasks on the Kanban board — Director interactions, dispatch activities, and supervisor decisions are all first-class Tasks (Principle 1: no hidden side-effects)

### Principle Compliance

- **P14 Secure by Default:** Director cannot approve tasks in `interactive` mode — blocked at the tool level. Autonomous mode has a hard 2-iteration cap before mandatory escalation. New projects default to `interactive` gate mode.
- **P15 Backend Owns All Logic:** All orchestration decisions (topological sort, gate routing, budget checks, supervisor routing) are server-side. The Director's tools call backend APIs — they don't compute locally.
- **P16 Single Source of Truth:** The `Task` table is the single source for task state. The Dispatch Engine reads and writes Task status. The Supervisor reads outcomes from Task table. No shadow state.
- **P17 Human Authority:** Gate modes ensure humans can control the level of autonomy. `interactive` = full human control. Even in `autonomous`, a hard 2-iteration cap forces escalation. Kill switch halts all dispatch.
- **P18 Idempotency:** `ExecuteTaskActivity` is a Durable Task activity — replays are safe because it checks for existing results before executing. Supervisor decisions are idempotent — same inputs produce the same classification.
- **P19 Bounded Resource Usage:** Max 2 autonomous iterations. Supervisor has $0.50 budget ceiling. Director has $5.00 budget ceiling and 20 tool call limit. Dispatch has per-layer budget checks.
- **P20 Version Everything:** Director, Supervisor, and Planner are versioned agent catalog entries. Gate mode changes are tracked in project audit history. Supervisor decisions are recorded with reasoning.
- **P21 Explicit Over Implicit:** The three roles have explicit, non-overlapping responsibilities. The Dispatch Engine is code (explicit deterministic logic), not an agent (implicit LLM reasoning). Gate checkpoints are explicitly configured, not auto-inserted.
