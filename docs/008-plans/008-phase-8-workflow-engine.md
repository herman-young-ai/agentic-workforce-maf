# Phase 8: Workflow Engine

**Status:** Not Started
**Depends On:** Phase 7 (Agent Catalog & Tools)
**Verification:** Integration test: define workflow → execute → agent node runs → human gate pauses → approve → completes

---

## Pre-flight

Complete the checklist in [000-phase-overview.md § Pre-flight for every phase](000-phase-overview.md#pre-flight-for-every-phase):

1. Read `.codemap/map.md` — type/method inventory from the previous phase. Do not recreate anything already present.
2. Read `.codemap/quality.md` — current CQI baseline. Work must not regress the score.
3. Verify the previous phase's exit criteria still hold:
   - `dotnet build AgenticWorkforce.slnx` exits 0
   - `dotnet test AgenticWorkforce.slnx` exits 0

---

## Objective

Implement the workflow execution engine: a generic `WorkflowInterpreter` that reads stored workflow definitions (JSON graphs) and walks them node-by-node using Durable Task SDK for durability. Each node type has an executor. Human decision nodes pause via `WaitForExternalEvent`. Tasks are created as first-class entities for every node execution. After this phase, repeatable multi-step processes run autonomously with human checkpoints.

---

## Architecture (from ADR-001, ADR-013)

```
WorkflowDefinition (JSON graph: nodes + edges)
        │
        ▼
RunWorkflow endpoint (or project.run_workflow tool)
        │ creates WorkflowRun entity
        │ enqueues to Redis Stream
        ▼
Worker picks up via XREADGROUP
        │
        ▼
Durable Task Orchestrator: WorkflowOrchestration
        │ reads WorkflowDefinition
        │ calls WorkflowInterpreter
        ▼
WorkflowInterpreter.ExecuteAsync()
        │ walks the graph from Start node
        │ for each node, calls appropriate NodeExecutor
        ▼
NodeExecutors:
├── AgentTaskExecutor — creates Task entity, calls IAgentRuntime, records result
├── HumanDecisionExecutor — creates Task + HumanInputRequest, WaitForExternalEvent
├── AiDecisionExecutor — creates Task, calls agent with constrained output schema
├── ParallelExecutor — fans out to multiple paths, waits for all
├── SubWorkflowExecutor — starts a child orchestration
└── ActionExecutor — runs platform actions (notify, update PCD, create artifact)
```

---

## 1. Durable Task Integration

### Package Additions

```xml
<PackageVersion Include="Microsoft.DurableTask.Client" Version="1.5.0" />
<PackageVersion Include="Microsoft.DurableTask.Worker" Version="1.5.0" />
<PackageVersion Include="Microsoft.DurableTask.Worker.Grpc" Version="1.5.0" />
```

### Worker Registration

```csharp
// Worker/Program.cs
builder.Services.AddDurableTaskWorker(b =>
{
    b.UseGrpc(); // connects to Azure Durable Task Scheduler (or sidecar in dev)
    b.AddTasks(tasks =>
    {
        tasks.AddOrchestratorFunc<WorkflowRunInput>("WorkflowOrchestration", WorkflowOrchestration.RunAsync);
        tasks.AddActivityFunc<AgentTaskInput, AgentTaskOutput>("ExecuteAgentTask", AgentTaskActivity.RunAsync);
        tasks.AddActivityFunc<ActionInput, ActionOutput>("ExecuteAction", ActionActivity.RunAsync);
    });
});

builder.Services.AddDurableTaskClient(b => b.UseGrpc());
```

### Backend Strategies (3 environments)

| Environment | Backend | Persistence | Use Case |
|-------------|---------|-------------|----------|
| Integration tests | In-memory emulator | None (test lifetime) | Fast, deterministic, no external deps |
| Local dev (Aspire) | Durable Task sidecar container | Container volume | Survives restarts, realistic behaviour |
| Production (Azure) | Azure Durable Task Scheduler (gRPC) | Managed, HA | Full durability, auto-scaling |

```csharp
// Worker/Program.cs
if (builder.Environment.IsDevelopment())
{
    // Aspire will provide the sidecar; fallback to in-memory if not available
    builder.Services.AddDurableTaskWorker(b =>
    {
        b.UseGrpc(builder.Configuration.GetConnectionString("durableTask")
            ?? "http://localhost:4001"); // sidecar default
        // ... task registrations
    });
}
else
{
    builder.Services.AddDurableTaskWorker(b =>
    {
        b.UseGrpc(); // Azure Durable Task Scheduler endpoint from config
        // ... task registrations
    });
}
```

For integration tests, `ApiWebApplicationFactory` overrides with in-memory:

```csharp
services.AddDurableTaskWorker(b =>
{
    b.UseInMemoryBackend();
    // ... same task registrations
});
```

---

## 2. WorkflowOrchestration (Durable Task Orchestrator)

### File: `src/AgenticWorkforce.Worker/Orchestrations/WorkflowOrchestration.cs`

```csharp
public static class WorkflowOrchestration
{
    public static async Task<WorkflowRunOutput> RunAsync(
        TaskOrchestrationContext context, WorkflowRunInput input)
    {
        var interpreter = new WorkflowInterpreter(context);

        try
        {
            var result = await interpreter.ExecuteAsync(input);

            // Update WorkflowRun status to Completed
            await context.CallActivityAsync("UpdateWorkflowRunStatus",
                new StatusUpdate(input.WorkflowRunId, WorkflowRunStatus.Completed, result.Summary));

            return result;
        }
        catch (Exception ex)
        {
            await context.CallActivityAsync("UpdateWorkflowRunStatus",
                new StatusUpdate(input.WorkflowRunId, WorkflowRunStatus.Failed, ex.Message));
            throw;
        }
    }
}

public record WorkflowRunInput(
    Guid WorkflowRunId,
    Guid ProjectId,
    Guid WorkflowDefinitionId,
    string NodesJson,
    string EdgesJson,
    string? Context);

public record WorkflowRunOutput(string Summary, decimal TotalCostUsd, int TasksCompleted);
```

---

## 3. WorkflowInterpreter

### File: `src/AgenticWorkforce.Worker/Orchestrations/WorkflowInterpreter.cs`

The interpreter is a pure graph walker. It does NOT reference MAF or EF Core — it only uses the `TaskOrchestrationContext` for durability.

```csharp
internal sealed class WorkflowInterpreter(TaskOrchestrationContext context)
{
    public async Task<WorkflowRunOutput> ExecuteAsync(WorkflowRunInput input)
    {
        var nodes = JsonSerializer.Deserialize<List<WorkflowNodeDef>>(input.NodesJson)!;
        var edges = JsonSerializer.Deserialize<List<WorkflowEdgeDef>>(input.EdgesJson)!;
        var graph = new WorkflowGraph(nodes, edges);

        var startNode = graph.GetStartNode()
            ?? throw new InvalidOperationException("Workflow has no Start node.");

        var variables = new Dictionary<string, object>();
        var totalCost = 0m;
        var tasksCompleted = 0;

        // Walk from start, following edges
        var currentNodes = new Queue<WorkflowNodeDef>();
        currentNodes.Enqueue(startNode);
        var visited = new HashSet<string>();

        while (currentNodes.Count > 0)
        {
            var node = currentNodes.Dequeue();
            if (visited.Contains(node.Id)) continue;
            visited.Add(node.Id);

            // Check dependencies (all incoming edges must be satisfied)
            if (!graph.AreDependenciesMet(node.Id, visited)) continue;

            var result = node.Type switch
            {
                "start" => NodeResult.Success(),
                "end" => NodeResult.End(),
                "agent_task" => await ExecuteAgentTaskNode(node, input, variables),
                "human_decision" => await ExecuteHumanDecisionNode(node, input),
                "ai_decision" => await ExecuteAiDecisionNode(node, input, variables),
                "parallel" => await ExecuteParallelNode(node, graph, input, variables),
                "action" => await ExecuteActionNode(node, input, variables),
                "sub_workflow" => await ExecuteSubWorkflowNode(node, input),
                _ => throw new InvalidOperationException($"Unknown node type: {node.Type}")
            };

            if (result.IsEnd) break;

            totalCost += result.CostUsd;
            if (result.IsTaskCompleted) tasksCompleted++;

            // Store output in variables for downstream nodes
            if (result.Output != null)
                variables[$"node:{node.Id}:output"] = result.Output;

            // Follow outgoing edges (conditional or default)
            var nextEdges = graph.GetOutgoingEdges(node.Id);
            foreach (var edge in nextEdges)
            {
                if (edge.Condition == null || EvaluateCondition(edge.Condition, result, variables))
                    currentNodes.Enqueue(graph.GetNode(edge.TargetNodeId));
            }
        }

        return new WorkflowRunOutput($"Completed {tasksCompleted} tasks", totalCost, tasksCompleted);
    }
}
```

---

## 4. Node Executors

### AgentTaskExecutor

```csharp
private async Task<NodeResult> ExecuteAgentTaskNode(
    WorkflowNodeDef node, WorkflowRunInput input, Dictionary<string, object> variables)
{
    var config = JsonSerializer.Deserialize<AgentTaskNodeConfig>(node.Config)!;

    // Create Task entity via activity
    var taskInput = new AgentTaskInput(
        ProjectId: input.ProjectId,
        WorkflowRunId: input.WorkflowRunId,
        NodeId: node.Id,
        AgentName: config.AgentName,
        Objective: InterpolateVariables(config.Objective, variables),
        Inputs: config.Inputs);

    var taskOutput = await context.CallActivityAsync<AgentTaskOutput>(
        "ExecuteAgentTask", taskInput);

    return new NodeResult(
        Output: taskOutput.Output,
        CostUsd: taskOutput.CostUsd,
        IsTaskCompleted: true,
        ChosenPath: null);
}
```

### HumanDecisionExecutor

```csharp
private async Task<NodeResult> ExecuteHumanDecisionNode(
    WorkflowNodeDef node, WorkflowRunInput input)
{
    var config = JsonSerializer.Deserialize<HumanDecisionNodeConfig>(node.Config)!;

    // Create HumanInputRequest via activity
    await context.CallActivityAsync("CreateHumanInputRequest", new HumanInputInput(
        ProjectId: input.ProjectId,
        WorkflowRunId: input.WorkflowRunId,
        NodeId: node.Id,
        PromptMessage: config.PromptMessage,
        Choices: config.Choices,
        TimeoutHours: config.TimeoutHours ?? 4));

    // Pause: wait for external event (human response)
    var timeout = TimeSpan.FromHours(config.TimeoutHours ?? 4);
    var response = await context.WaitForExternalEvent<HumanDecisionResponse>(
        $"human_decision:{node.Id}", timeout);

    // If timed out, response is null
    if (response == null)
    {
        await context.CallActivityAsync("TimeoutHumanInput",
            new TimeoutInput(input.WorkflowRunId, node.Id));
        return NodeResult.Failed("Human decision timed out");
    }

    return new NodeResult(
        Output: response.Response,
        CostUsd: 0,
        IsTaskCompleted: true,
        ChosenPath: response.Choice);
}
```

### AiDecisionExecutor

```csharp
private async Task<NodeResult> ExecuteAiDecisionNode(
    WorkflowNodeDef node, WorkflowRunInput input, Dictionary<string, object> variables)
{
    var config = JsonSerializer.Deserialize<AiDecisionNodeConfig>(node.Config)!;

    // Agent runs with constrained output schema — must choose from defined options
    var objective = $"""
        {InterpolateVariables(config.Question, variables)}

        You MUST respond with exactly one of these choices: {string.Join(", ", config.Choices)}
        Respond with JSON: {{ "choice": "<one of the above>", "reasoning": "..." }}
        """;

    var taskOutput = await context.CallActivityAsync<AgentTaskOutput>(
        "ExecuteAgentTask", new AgentTaskInput(
            input.ProjectId, input.WorkflowRunId, node.Id,
            config.AgentName ?? "system.verifier", objective, null));

    var decision = ParseDecision(taskOutput.Output, config.Choices);

    return new NodeResult(
        Output: taskOutput.Output,
        CostUsd: taskOutput.CostUsd,
        IsTaskCompleted: true,
        ChosenPath: decision.Choice);
}
```

### ParallelExecutor

```csharp
private async Task<NodeResult> ExecuteParallelNode(
    WorkflowNodeDef node, WorkflowGraph graph, WorkflowRunInput input,
    Dictionary<string, object> variables)
{
    var outgoingEdges = graph.GetOutgoingEdges(node.Id);
    var parallelTasks = new List<Task<NodeResult>>();

    foreach (var edge in outgoingEdges)
    {
        var targetNode = graph.GetNode(edge.TargetNodeId);
        // Execute each branch as a sub-orchestration or inline
        parallelTasks.Add(ExecuteNodeAsync(targetNode, input, variables));
    }

    var results = await Task.WhenAll(parallelTasks);
    var totalCost = results.Sum(r => r.CostUsd);

    return new NodeResult(Output: "Parallel complete", CostUsd: totalCost,
        IsTaskCompleted: false, ChosenPath: null);
}
```

### ActionExecutor

```csharp
private async Task<NodeResult> ExecuteActionNode(
    WorkflowNodeDef node, WorkflowRunInput input, Dictionary<string, object> variables)
{
    var config = JsonSerializer.Deserialize<ActionNodeConfig>(node.Config)!;

    var actionOutput = await context.CallActivityAsync<ActionOutput>(
        "ExecuteAction", new ActionInput(
            input.ProjectId, input.WorkflowRunId, node.Id,
            config.ActionType, config.Parameters, variables));

    return new NodeResult(
        Output: actionOutput.Result,
        CostUsd: 0,
        IsTaskCompleted: true,
        ChosenPath: null);
}
```

---

## 5. Durable Task Activities

### File: `src/AgenticWorkforce.Worker/Activities/AgentTaskActivity.cs`

```csharp
public static class AgentTaskActivity
{
    public static async Task<AgentTaskOutput> RunAsync(
        TaskActivityContext context, AgentTaskInput input)
    {
        // Resolve from DI (activities get IServiceProvider)
        var sp = context.GetServiceProvider();
        var agentRuntime = sp.GetRequiredService<IAgentRuntime>();
        var taskRepo = sp.GetRequiredService<ITaskRepository>();
        var eventPublisher = sp.GetRequiredService<IEventPublisher>();
        var verifier = sp.GetRequiredService<IVerifier>();

        // 1. Create AgenticTask entity
        var task = new AgenticTask
        {
            ProjectId = input.ProjectId,
            WorkflowRunId = input.WorkflowRunId,
            WorkflowNodeId = input.NodeId,
            Type = TaskType.AgentTask,
            Status = TaskStatus.Running,
            Objective = input.Objective,
            AgentName = input.AgentName,
            Source = TaskSource.Workflow,
            StartedAt = DateTime.UtcNow
        };
        await taskRepo.CreateAsync(task);
        await eventPublisher.PublishAsync(new ProjectEvent { ... EventType = EventTypes.TaskStarted });

        // 2. Execute agent
        var projectContext = new ProjectContext(input.ProjectId, input.Objective, null, null);
        var result = await agentRuntime.RunAsync(input.AgentName, input.Objective, projectContext);

        // 3. Verify output
        var catalog = await sp.GetRequiredService<IAgentCatalogResolver>()
            .ResolveAsync(input.AgentName);
        var verification = await verifier.VerifyAsync(task, result.Output, catalog!);

        // 4. Update task
        task.Status = verification.Passed ? TaskStatus.Completed : TaskStatus.Failed;
        task.Outputs = result.Output;
        task.OutputSummary = result.Output[..Math.Min(result.Output.Length, 200)];
        task.CostUsd = result.CostUsd;
        task.DurationSeconds = result.Duration.TotalSeconds;
        task.CompletedAt = DateTime.UtcNow;
        await taskRepo.UpdateAsync(task);

        // 5. Publish completion event
        await eventPublisher.PublishAsync(new ProjectEvent { ... EventType = EventTypes.TaskCompleted });

        return new AgentTaskOutput(task.Id, result.Output, result.CostUsd, verification.Passed);
    }
}
```

### File: `src/AgenticWorkforce.Worker/Activities/ActionActivity.cs`

Handles action nodes: notify, update PCD, create artifact, set variable:

```csharp
public static class ActionActivity
{
    public static async Task<ActionOutput> RunAsync(
        TaskActivityContext context, ActionInput input)
    {
        var sp = context.GetServiceProvider();

        return input.ActionType switch
        {
            "update_pcd" => await UpdatePcd(sp, input),
            "create_artifact" => await CreateArtifact(sp, input),
            "notify" => await Notify(sp, input),
            "set_variable" => SetVariable(input),
            _ => throw new InvalidOperationException($"Unknown action: {input.ActionType}")
        };
    }
}
```

---

## 6. IWorkflowEngine Implementation

### File: `src/AgenticWorkforce.Infrastructure/Services/WorkflowEngine.cs`

```csharp
internal sealed class WorkflowEngine(
    DurableTaskClient client,
    IWorkflowRepository workflowRepo,
    ILogger<WorkflowEngine> logger) : IWorkflowEngine
{
    public async Task<Guid> StartAsync(
        Guid projectId, Guid workflowDefinitionId, string? triggerType,
        string? context, CancellationToken ct = default)
    {
        var definition = await workflowRepo.GetByIdAsync(workflowDefinitionId, ct)
            ?? throw new NotFoundException("WorkflowDefinition", workflowDefinitionId);

        var run = new WorkflowRun
        {
            ProjectId = projectId,
            WorkflowDefinitionId = workflowDefinitionId,
            WorkflowName = definition.Name,
            WorkflowVersion = definition.Version,
            Status = WorkflowRunStatus.Running,
            TriggerType = triggerType,
            Context = context
        };
        await workflowRepo.CreateRunAsync(run, ct);

        var input = new WorkflowRunInput(
            run.Id, projectId, workflowDefinitionId,
            definition.Nodes, definition.Edges, context);

        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            "WorkflowOrchestration", input, ct);

        logger.LogInformation("Started workflow {WorkflowName} v{Version} as {InstanceId}",
            definition.Name, definition.Version, instanceId);

        return run.Id;
    }

    public async Task PauseAsync(Guid workflowRunId, CancellationToken ct = default)
    {
        var run = await workflowRepo.GetRunByIdAsync(workflowRunId, ct)
            ?? throw new NotFoundException("WorkflowRun", workflowRunId);
        run.Status = WorkflowRunStatus.Paused;
        await workflowRepo.UpdateRunAsync(run, ct);
        await client.SuspendInstanceAsync(run.Id.ToString(), ct);
    }

    public async Task ResumeAsync(Guid workflowRunId, CancellationToken ct = default)
    {
        var run = await workflowRepo.GetRunByIdAsync(workflowRunId, ct)
            ?? throw new NotFoundException("WorkflowRun", workflowRunId);
        run.Status = WorkflowRunStatus.Running;
        await workflowRepo.UpdateRunAsync(run, ct);
        await client.ResumeInstanceAsync(run.Id.ToString(), ct);
    }

    public async Task CancelAsync(Guid workflowRunId, CancellationToken ct = default)
    {
        var run = await workflowRepo.GetRunByIdAsync(workflowRunId, ct)
            ?? throw new NotFoundException("WorkflowRun", workflowRunId);
        run.Status = WorkflowRunStatus.Cancelled;
        await workflowRepo.UpdateRunAsync(run, ct);
        await client.TerminateInstanceAsync(run.Id.ToString(), ct);
    }

    public async Task SubmitHumanInputAsync(
        Guid requestId, HumanDecisionType decision, string? response,
        Guid responderId, CancellationToken ct = default)
    {
        var request = await workflowRepo.GetHumanInputRequestAsync(requestId, ct)
            ?? throw new NotFoundException("HumanInputRequest", requestId);

        request.Decision = decision;             // queryable enum (Approved/Rejected/Escalated/Overridden)
        request.Response = response;             // free-text justification or chosen value
        request.ResponderId = responderId;
        request.ResolvedAt = DateTime.UtcNow;
        request.Status = HumanInputRequestStatus.Completed;
        await workflowRepo.UpdateHumanInputAsync(request, ct);

        // Raise external event to resume the paused orchestration
        await client.RaiseEventAsync(
            request.WorkflowRunId.ToString(),
            $"human_decision:{request.TaskId}",
            new HumanDecisionResponse(decision, response), ct);
    }
}
```

---

## 7. Dispatch Engine (Ad-hoc Tasks)

### File: `src/AgenticWorkforce.Worker/Services/TaskDispatchService.cs`

For ad-hoc tasks (not part of a workflow), dispatches approved tasks to Durable Task:

```csharp
internal sealed class TaskDispatchService(
    DurableTaskClient client,
    ITaskRepository taskRepo,
    ILogger<TaskDispatchService> logger) : ITaskDispatchService
{
    public async Task DispatchApprovedAsync(Guid projectId, CancellationToken ct)
    {
        var tasks = await taskRepo.GetByProjectIdAsync(projectId, TaskStatus.Approved, ct);

        foreach (var task in tasks)
        {
            task.Status = TaskStatus.Queued;
            await taskRepo.UpdateAsync(task, ct);

            await client.ScheduleNewOrchestrationInstanceAsync(
                "SingleTaskOrchestration",
                new SingleTaskInput(task.Id, task.ProjectId, task.AgentName!, task.Objective), ct);

            logger.LogInformation("Dispatched task {TaskId} for agent {AgentName}",
                task.Id, task.AgentName);
        }
    }
}
```

---

## 8. Kill Switch (Emergency Stop)

### File: `src/AgenticWorkforce.Worker/Services/EmergencyStopService.cs`

```csharp
internal sealed class EmergencyStopService(
    DurableTaskClient client,
    ITaskRepository taskRepo,
    IEventPublisher eventPublisher) : IEmergencyStopService
{
    public async Task StopAllAsync(Guid projectId, string reason, CancellationToken ct)
    {
        // 1. Cancel all running workflow instances for this project
        var runningTasks = await taskRepo.GetByProjectIdAsync(projectId, TaskStatus.Running, ct);
        foreach (var task in runningTasks)
        {
            task.Status = TaskStatus.Cancelled;
            await taskRepo.UpdateAsync(task, ct);

            if (task.WorkflowRunId.HasValue)
                await client.TerminateInstanceAsync(task.WorkflowRunId.Value.ToString(), ct);
        }

        // 2. Publish critical event
        await eventPublisher.PublishAsync(new ProjectEvent
        {
            ProjectId = projectId,
            EventType = "emergency.stop",
            Severity = EventSeverity.Critical,
            Data = JsonSerializer.Serialize(new { reason, tasksKilled = runningTasks.Count })
        }, ct);
    }
}
```

---

## 9. Graph Model (Value Objects)

### File: `src/AgenticWorkforce.Worker/Orchestrations/WorkflowGraph.cs`

```csharp
internal sealed class WorkflowGraph
{
    private readonly Dictionary<string, WorkflowNodeDef> _nodes;
    private readonly ILookup<string, WorkflowEdgeDef> _outgoing;
    private readonly ILookup<string, WorkflowEdgeDef> _incoming;

    public WorkflowGraph(IEnumerable<WorkflowNodeDef> nodes, IEnumerable<WorkflowEdgeDef> edges)
    {
        _nodes = nodes.ToDictionary(n => n.Id);
        _outgoing = edges.ToLookup(e => e.SourceNodeId);
        _incoming = edges.ToLookup(e => e.TargetNodeId);
    }

    public WorkflowNodeDef? GetStartNode() => _nodes.Values.FirstOrDefault(n => n.Type == "start");
    public WorkflowNodeDef GetNode(string id) => _nodes[id];
    public IEnumerable<WorkflowEdgeDef> GetOutgoingEdges(string nodeId) => _outgoing[nodeId];
    public bool AreDependenciesMet(string nodeId, HashSet<string> visited)
        => _incoming[nodeId].All(e => visited.Contains(e.SourceNodeId));
}

public record WorkflowNodeDef(string Id, string Type, string Name, string? Config);
public record WorkflowEdgeDef(string SourceNodeId, string TargetNodeId, string? Condition, string? Label);
```

---

## File Summary

### Files to CREATE (~20 files)

```
src/AgenticWorkforce.Worker/Orchestrations/WorkflowOrchestration.cs
src/AgenticWorkforce.Worker/Orchestrations/WorkflowInterpreter.cs
src/AgenticWorkforce.Worker/Orchestrations/WorkflowGraph.cs
src/AgenticWorkforce.Worker/Orchestrations/NodeResult.cs
src/AgenticWorkforce.Worker/Orchestrations/NodeConfigs.cs
src/AgenticWorkforce.Worker/Orchestrations/SingleTaskOrchestration.cs
src/AgenticWorkforce.Worker/Activities/AgentTaskActivity.cs
src/AgenticWorkforce.Worker/Activities/ActionActivity.cs
src/AgenticWorkforce.Worker/Activities/CreateHumanInputActivity.cs
src/AgenticWorkforce.Worker/Activities/UpdateWorkflowRunStatusActivity.cs
src/AgenticWorkforce.Worker/Services/TaskDispatchService.cs
src/AgenticWorkforce.Worker/Services/ITaskDispatchService.cs
src/AgenticWorkforce.Worker/Services/EmergencyStopService.cs
src/AgenticWorkforce.Worker/Services/IEmergencyStopService.cs
src/AgenticWorkforce.Infrastructure/Services/WorkflowEngine.cs
tests/AgenticWorkforce.Api.Tests.Integration/Workflows/WorkflowExecutionTests.cs
tests/AgenticWorkforce.Domain.Tests.Unit/Workflows/WorkflowGraphTests.cs
tests/AgenticWorkforce.Domain.Tests.Unit/Workflows/WorkflowInterpreterTests.cs
```

### Files to MODIFY

```
src/AgenticWorkforce.Worker/Program.cs — Add Durable Task registration
src/AgenticWorkforce.Worker/AgenticWorkforce.Worker.csproj — Add Durable Task packages
src/AgenticWorkforce.Infrastructure/DependencyInjection.cs — Register IWorkflowEngine
Directory.Packages.props — Add Durable Task package versions
```

---

## Verification Criteria

1. `dotnet build AgenticWorkforce.slnx` exits 0
2. `dotnet test` — all tests pass:
   - `WorkflowGraphTests`: parses nodes/edges, finds start, resolves paths, detects cycles
   - `WorkflowInterpreterTests`: walks linear graph, follows conditional edges, handles parallel
   - `WorkflowExecutionTests`: full integration — create workflow → run → agent node executes → human gate pauses → submit response → completes
3. `WorkflowEngine.StartAsync` creates WorkflowRun entity and schedules orchestration
4. Human decision node creates `HumanInputRequest` and pauses
5. `SubmitHumanInputAsync` resumes paused orchestration with response
6. Emergency stop cancels all running tasks for a project
7. Task entities are created for every node execution (visible on Kanban board)
8. AgentTaskActivity calls `IAgentRuntime.RunAsync` and records results

---

## Goal Command

```
/goal Workflow engine complete: WorkflowInterpreter walks JSON graph definitions node-by-node using Durable Task SDK for durability. Node executors handle AgentTask (calls IAgentRuntime), HumanDecision (WaitForExternalEvent with timeout), AiDecision (constrained output), Parallel (fan-out/join), Action (PCD update, notify). IWorkflowEngine.StartAsync creates WorkflowRun and schedules orchestration. SubmitHumanInputAsync raises external event to resume. Emergency stop cancels all. Tasks created as first-class entities for every node. Verify: dotnet build exits 0, dotnet test exits 0 with workflow execution integration test. Stop after 40 turns.
```
