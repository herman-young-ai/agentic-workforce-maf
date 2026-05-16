# R06: Container Isolation for Agent Code Execution

## Prompt for claude.ai

---

I am building an AI agent platform in C# on Azure Container Apps. Our agents can execute code (write files, run commands, run tests) as part of their tasks. This code execution must be **sandboxed** — an agent must not be able to affect the host platform, other missions, or access resources outside its scope.

Give me a **concise comparison** of sandboxing options on Azure. Tables and recommendations.

### Our Code Execution Requirements

**What agents do:**
- Read/write files in a project workspace (git repo clone)
- Run shell commands (build, test, lint, static analysis)
- Execute Python/Node/dotnet scripts
- Search files by pattern (glob, grep)
- Run git operations (diff, commit, branch)

**Isolation requirements:**
- Each mission gets its own isolated workspace
- Agent code execution cannot affect other missions
- Agent cannot access the host filesystem, network services, or secrets outside its scope
- Workspace persists across multiple agent task executions within the same mission
- Workspace can be cleaned up when the mission completes
- File changes must be retrievable (git diff, file download)

**Current implementation (Python):**
Three execution modes:
1. **Local** — agent runs directly on host filesystem (dev only, no isolation)
2. **Worktree** — git worktree branch per mission (filesystem isolation, no process isolation)
3. **Container** — Docker/OrbStack container per mission (full isolation)

### Options to Compare on Azure

1. **Azure Container Apps Dynamic Sessions** — ephemeral container sessions with code execution
2. **Azure Container Instances (ACI)** — on-demand container per mission
3. **Azure Container Apps Jobs** — event-driven container execution
4. **MAF Foundry Code Interpreter** — Foundry's hosted code execution tool
5. **Self-managed Docker-in-Docker on Container Apps** — run Docker inside our Container App
6. **Azure Kubernetes Service (AKS) pod-per-mission** — dedicated pod with ephemeral storage

### Comparison Table

| Criterion | Dynamic Sessions | ACI | CA Jobs | Foundry Code Interp | DinD | AKS Pod |
|---|---|---|---|---|---|---|
| Startup latency | | | | | | |
| Persistent workspace across tasks | | | | | | |
| Custom runtime (any language/tools) | | | | | | |
| Git operations supported | | | | | | |
| File upload/download | | | | | | |
| Shell command execution | | | | | | |
| Network isolation | | | | | | |
| Max session duration | | | | | | |
| Cost model | | | | | | |
| Operational complexity | | | | | | |
| Aspire/Container Apps integration | | | | | | |

### Specific Questions

1. Can Azure Container Apps Dynamic Sessions run arbitrary shell commands, or only Python/JS code snippets?
2. Can ACI mount an Azure File Share as persistent workspace that survives across multiple agent task invocations?
3. For the MAF Foundry Code Interpreter — what languages are supported? Can it run git, build tools, linters?
4. What's the pattern for a MAF function tool that delegates execution to a remote container? Show a code sketch.
5. Security: which option provides the strongest isolation guarantees for a bank?

### Output Format

- Filled comparison table
- Answer each specific question
- **Recommendation** with rationale for a regulated bank
- **Architecture sketch**: how the chosen option integrates with our MAF agent platform (agent calls tool → tool provisions/reuses container → executes command → returns result)

Keep total response under 1500 words.

---

## After Research

Save claude.ai's response as: `docs/098-research/R06-response-container-isolation.md`
