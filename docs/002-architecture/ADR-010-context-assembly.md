# ADR-010: Context Assembly Pipeline

**Status:** Accepted
**Date:** 2026-05-10
**Decision Makers:** Architecture team
**Research:** [R10-response-context-assembly.md](../098-research/R10-response-context-assembly.md)

---

## Context

The Agentic Workforce Platform's most critical subsystem determines what each agent knows before executing a task. A multi-layer context packet is assembled with priority-based token budget trimming. Layers include PCD, task definition, upstream inputs, learnings, code map, and execution history. Prompts are assembled from 5 ordered layers. Long sessions are compressed via rolling summary.

## Decision

**`AIContextProvider` for per-turn context injection + `DelegatingChatClient` for per-model-call injection + `Microsoft.ML.Tokenizers` / Anthropic `count_tokens` API for token counting**

### Context Assembly Architecture

```
┌─── Per Agent Turn ────────────────────────────────────┐
│                                                        │
│  1. PromptAssembler.Assemble(agent, project)           │
│     → 5-layer system prompt → Instructions string      │
│                                                        │
│  2. ProjectContextProvider : AIContextProvider          │
│     InvokingAsync() → loads PCD, task, learnings,      │
│     code map, history → returns AIContext               │
│     { Messages = [system context blocks],              │
│       Tools = [dynamic tools for this task] }           │
│                                                        │
│  3. ChatClientAgent merges:                            │
│     Instructions + AIContext.Instructions (appended)    │
│     + AIContext.Messages (prepended to user messages)   │
│     + AIContext.Tools (merged with agent tools)         │
│                                                        │
│  4. InvokedAsync() → post-turn:                        │
│     - Check session token count                        │
│     - Fire rolling summary compression if over budget  │
│     - Persist messages to session store                 │
└────────────────────────────────────────────────────────┘
```

### Context Provider Implementation

```csharp
internal sealed class ProjectContextProvider : AIContextProvider
{
    private readonly IContextAssembler _assembler;
    private readonly AIAgent _summarizer;

    public override string StateKey => nameof(ProjectContextProvider);

    protected override async ValueTask<AIContext> InvokingAsync(
        InvokingContext ctx, CancellationToken ct)
    {
        var projectCtx = ProjectContext.Current!;
        var packet = await _assembler.BuildAsync(
            projectCtx.ProjectId, projectCtx.TaskDefinition,
            projectCtx.AgentConfig, projectCtx.DomainTags, ct);

        return new AIContext
        {
            Messages = packet.ContextMessages,   // PCD, task, learnings, history as system msgs
            Instructions = packet.AdditionalInstructions, // appended to agent instructions
            Tools = packet.DynamicTools,          // task-specific tools
        };
    }

    protected override async ValueTask InvokedAsync(
        InvokedContext ctx, CancellationToken ct)
    {
        // Rolling summary compression
        var tokenCount = await _tokenCounter.CountAsync(ctx.Session);
        if (tokenCount > _compressionThreshold)
        {
            var transcript = SerializeMessages(ctx.Session);
            var summary = await _summarizer.RunAsync(transcript, session: null, ct: ct);
            await _sessionStore.UpdateRollingSummaryAsync(
                ctx.SessionId, summary.Text, ct);
        }
    }
}
```

### Context Assembler (Token Budget Management)

```csharp
public sealed class ContextAssembler
{
    public async Task<ContextPacket> BuildAsync(
        Guid projectId, TaskDefinition task,
        AgentConfig agent, string[] domainTags, CancellationToken ct)
    {
        var budget = new TokenBudget(_config.ContextTokenBudget);
        var messages = new List<ChatMessage>();

        // Layer 0: PCD — NEVER trimmed
        var pcd = await _pcdRepo.LoadAsync(projectId, ct);
        budget.Reserve(pcd, trim: false);
        messages.Add(new(ChatRole.System, $"<pcd>{pcd.ToJson()}</pcd>"));

        // Layer 1a: Task definition — NEVER trimmed
        budget.Reserve(task.ToPrompt(), trim: false);
        messages.Add(new(ChatRole.System, $"<task>{task.ToPrompt()}</task>"));

        // Layer 1b: Upstream inputs — summarized if over budget
        if (task.UpstreamInputs is { Count: > 0 })
        {
            var inputs = await _inputResolver.ResolveAsync(task, ct);
            var text = budget.FitOrSummarize(inputs, _summarizer);
            messages.Add(new(ChatRole.System, $"<upstream>{text}</upstream>"));
        }

        // Layer 2: Platform learnings (promoted, domain-matched)
        if (budget.Remaining > _config.PlatformLearningsReserveTokens)
        {
            var platformLearnings = await _learningRepo.GetPlatformLearningsAsync(
                domainTags, max: 3, ct);
            if (platformLearnings.Any())
            {
                messages.Add(new(ChatRole.System,
                    $"<platform-knowledge>{FormatLearnings(platformLearnings)}</platform-knowledge>"));
                budget.Consume(platformLearnings);
            }
        }

        // Layer 3: Project learnings (active, domain-matched, by confidence)
        if (budget.Remaining > _config.LearningsReserveTokens)
        {
            var projectLearnings = await _learningRepo.GetActiveAsync(
                projectId, domainTags, _config.LearningsMaxEntries, ct);
            if (projectLearnings.Any())
            {
                messages.Add(new(ChatRole.System,
                    $"<project-learnings>{FormatLearnings(projectLearnings)}</project-learnings>"));
                budget.Consume(projectLearnings);
            }
        }

        // Layer 4: Active decisions (relevant domain)
        var decisions = await _decisionRepo.GetActiveAsync(projectId, domainTags, ct);
        if (decisions.Any())
        {
            messages.Add(new(ChatRole.System,
                $"<decisions>{FormatDecisions(decisions)}</decisions>"));
            budget.Consume(decisions);
        }

        // Layer 4a: Knowledge graph context (dependency/compliance chains)
        // Only for tasks with relevant domain tags — see ADR-015
        if (domainTags.Overlaps(["security", "compliance", "architecture", "dependencies"])
            && budget.Remaining > _config.GraphContextReserveTokens)
        {
            var graphContext = await _graphContextBuilder.BuildAsync(projectId, task, ct);
            if (graphContext is not null)
            {
                messages.Add(new(ChatRole.System,
                    $"<knowledge-graph>\n{graphContext.FormattedText}\n</knowledge-graph>"));
                budget.Consume(graphContext);
            }
        }

        // Layer 5: Existing findings dedup (research projects only)
        if (projectType == "research")
        {
            var existing = await _learningRepo.GetAllAsync(projectId, kind: "domain_insight", ct);
            if (existing.Any())
            {
                messages.Add(new(ChatRole.System,
                    "<existing-knowledge>You have already reported these findings:\n" +
                    $"{FormatExistingFindings(existing)}\n" +
                    "Do NOT repeat these. Report only genuinely new information.</existing-knowledge>"));
                budget.Consume(existing);
            }
        }

        // Layer 6: Uploaded document chunks (semantically relevant)
        var docChunks = await _docSearchService.SearchRelevantAsync(
            projectId, task.Objective, max: 5, ct);
        if (docChunks.Any())
        {
            messages.Add(new(ChatRole.System,
                $"<reference-documents>{FormatChunks(docChunks)}</reference-documents>"));
            budget.Consume(docChunks);
        }

        // Layer 7: Code Map — only for coding tasks, token-capped
        if (domainTags.Contains("coding"))
        {
            var codeMap = await _codeMapService.GenerateAsync(projectId, ct);
            var trimmed = _tokenCounter.TrimToFit(codeMap, _config.CodeMapMaxTokens);
            messages.Add(new(ChatRole.System, $"<codemap>{trimmed}</codemap>"));
            budget.Consume(trimmed);
        }

        // Layer 8: Execution history — trimmed FIRST if over budget
        var history = await _historyService.GetRecentAsync(projectId, ct);
        var historyText = budget.FitRemaining(history);
        if (!string.IsNullOrEmpty(historyText))
            messages.Add(new(ChatRole.System, $"<history>{historyText}</history>"));

        return new ContextPacket(messages);
    }
}
```

### Token Counting Strategy

| Model Family | Tokenizer | Method |
|---|---|---|
| GPT-4o, GPT-5 | `Microsoft.ML.Tokenizers` 2.0.0 | `TiktokenTokenizer.CreateForModel("gpt-4o").CountTokens(text)` |
| Claude (all) | Anthropic `count_tokens` API | `POST /v1/messages/count_tokens` (free, rate-limited) |
| Claude (offline approx) | Linear estimator | Calibrate against API; multiply by 1.1-1.5 safety factor |

```csharp
// GPT tokenizer (local, synchronous)
var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
int count = tokenizer.CountTokens(text);

// Claude tokenizer (API call, async)
var count = await anthropicClient.Messages.CountMessageTokensAsync(
    new MessageCountTokenParameters { Model = "claude-sonnet-4-6", Messages = [...] });
```

**Critical**: Tiktoken is NOT accurate for Claude. Opus 4.7's new tokenizer produces up to 1.47x more tokens than 4.6 on the same input.

### Prompt Layering (5 layers → single Instructions string)

```csharp
public class PromptAssembler
{
    public string Assemble(AgentCatalogEntry agent, ProjectContext project)
    {
        var sb = new StringBuilder();
        sb.AppendLine(LoadFromDisk("prompts/organization.md"));       // L1: global
        sb.AppendLine(LoadFromDisk($"prompts/{agent.AgentType}.md")); // L2: category
        sb.AppendLine(agent.SystemPrompt);                            // L3: agent (DB, versioned)
        if (project.Brief is not null)
            sb.AppendLine(project.Brief);                             // L4: project brief
        var userPrompt = GetUserPrompt(project.Id, agent.Id);
        if (userPrompt is not null)
            sb.AppendLine(userPrompt);                                // L5: per-agent per-project
        return sb.ToString();
    }
}
```

Set once at agent construction via `ChatClientAgentOptions.Instructions`. Dynamic per-turn context goes through `AIContextProvider.InvokingAsync()` which appends to instructions.

### Rolling Summary Compression

```csharp
// Triggered in AIContextProvider.InvokedAsync when message count exceeds threshold
if (messagesSinceAnchor > _config.CompressionThresholdMessages)
{
    // Call Haiku summarizer (cheap, fast)
    var summary = await _summarizerAgent.RunAsync(
        $"Summarize this conversation preserving key decisions and open tasks:\n{transcript}",
        session: null, ct: ct);

    await _sessionStore.UpdateAsync(sessionId, s =>
    {
        s.RollingSummary = summary.Text;
        s.RollingSummaryAnchor = currentMessageIndex;
        s.RollingSummaryVersion++;
    }, ct);
}
```

### PCD Path-Based Mutations (EF Core 10 + jsonb)

```csharp
// Typed path mutation via ExecuteUpdateAsync → translates to jsonb_set
await db.ProjectContexts
    .Where(c => c.ProjectId == projectId && c.Version == expectedVersion)
    .ExecuteUpdateAsync(s => s
        .SetProperty(c => c.ContextData.Architecture.Components, newComponents)
        .SetProperty(c => c.ContextData.CurrentState.LastUpdated, DateTimeOffset.UtcNow));

// Optimistic concurrency: check affected rows
if (rows == 0) throw new DbUpdateConcurrencyException("PCD was modified concurrently");
```

### Domain-Tag-Driven Layer Inclusion

```csharp
// Tags on the task drive which context layers are included
if (domainTags.Contains("coding"))     → include Code Map layer
if (domainTags.Contains("security"))   → include security assessment config
if (domainTags.Contains("research"))   → include web search history
if (!domainTags.Contains("coding"))    → skip Code Map entirely
```

## Key Packages

```xml
<PackageReference Include="Microsoft.ML.Tokenizers" Version="2.0.0" />
<PackageReference Include="Anthropic" Version="12.20.0" />  <!-- count_tokens API -->
```

## Related

- [ADR-014: Knowledge and Memory Management](ADR-014-knowledge-memory.md) — defines the knowledge taxonomy, read-write cycle, deduplication, and human control that this context assembler consumes
- [ADR-015: Knowledge Graph](ADR-015-knowledge-graph.md) — Apache AGE graph layer for dependency chains, compliance traceability, and impact analysis (Layer 4a)

## Consequences

- No local Claude tokenizer — must call Anthropic `count_tokens` API (free but rate-limited); cache aggressively
- Tiktoken is NOT accurate for Claude — do not use as a substitute
- `AIContextProvider` is reused across sessions — put per-session state in `AgentSession` via `ProviderSessionState<T>`
- `ExecuteUpdateAsync` bypasses EF change tracker — must include version check in WHERE clause manually for concurrency
- EF Core 10 JSONB `Contains` LINQ regression (#3745) — test affected queries; use `EF.Functions.JsonContains` as fallback
- Context assembly adds latency (DB reads, token counting, optional summarization) — cache PCD and learnings; batch token counts
- Rolling summary uses a separate Haiku agent call — factor cost into budget
- Knowledge extraction runs a Haiku agent after every task (~$0.001 per extraction) — factored into project budget
- Research projects inject existing findings as "do not repeat" context — scales with learning count; top-N filtering prevents token blow-up
- Platform learnings are loaded across all projects — keep promoted count reasonable (curated, not automatic)

### Principle Compliance

- **P14 Secure by Default:** Context layers default to empty/excluded. Each layer must be explicitly enabled via domain tags or configuration. Agents receive no context beyond PCD/task unless explicitly allowed.
- **P15 Backend Owns All Logic:** All prompt assembly, context injection, and token budget decisions happen server-side. The frontend never constructs or modifies system prompts or context layers.
- **P17 Human Authority:** Humans can override context assembly decisions — force-include or force-exclude specific learnings/layers for a particular execution. PCD principles (human-authored) are never trimmed.
- **P18 Idempotency:** PCD updates via `ExecuteUpdateAsync` with version checks are naturally idempotent. Rolling summary writes are also idempotent — same input transcript produces the same summary, no duplicate versions.
- **P19 Bounded Resource Usage:** Explicit limits on: total context token budget (~100K), max document chunks (5), max code map tokens (~8K), max learnings (5), and a timeout on the Anthropic `count_tokens` API call with circuit breaker fallback.
- **P21 Explicit Over Implicit:** The mapping of domain tags to context layers is declared in a configuration manifest — not scattered across `if` statements in code. All layer inclusion rules are enumerable and auditable.
