# R05: Real-time Events — SignalR vs Azure Web PubSub vs SSE

## Prompt for claude.ai

---

I am building an AI agent orchestration platform in C# / ASP.NET Core deployed on Azure Container Apps. The platform needs real-time event streaming to multiple consumer surfaces. Give me a **concise architectural comparison** with a clear recommendation.

### Our Real-time Requirements

**Event types:**
- Agent execution events (task started, task completed, task failed, verification result)
- Agent console output (streaming LLM response chunks during execution)
- Mission state transitions (plan updated, gate approval requested, budget warning)
- Notifications (approval needed, execution complete, error alert)
- SSE keepalive pings for disconnect detection

**Consumer surfaces:**

| Surface | Transport Constraint | Auth |
|---|---|---|
| React SPA (browser) | WebSocket or SSE | JWT Bearer |
| CLI (terminal) | SSE only (no WebSocket in terminal HTTP clients) | Short-lived SSE token (30s TTL) |
| TUI (terminal app) | SSE or WebSocket via HTTP | API key |
| Telegram bot | Webhook push (outbound from server) | Bot token |
| External API consumers | SSE or webhook | API key |

**Channel patterns:**
- Per-mission channel: all events for a mission (any session)
- Per-session channel: events filtered to one execution session
- Per-user notification channel: approvals, completions, failures for a specific user
- Inline response stream: single chat/agent response (not persisted to channel — ephemeral)

**Scale:**
- 10-50 concurrent missions
- 5-20 connected consumers at peak
- Event throughput: ~100-500 events/second during active execution bursts
- This is NOT a consumer-scale product — it's an internal platform tool

**Current implementation (Python):**
- Redis pub/sub for event distribution
- Server-Sent Events (SSE) endpoints in FastAPI
- Redis channels named `mission_events:{mission_id}` and `notify:{user_id}`
- SSE auth via single-use Redis tokens (30s TTL) or JWT fallback

### Options to Compare

1. **ASP.NET Core SSE** — raw `StreamingResponse` / `IAsyncEnumerable` over HTTP, backed by Redis pub/sub or in-memory channels
2. **Azure SignalR Service** — managed WebSocket/SSE/long-polling hub with Azure-hosted backplane
3. **Azure Web PubSub** — managed WebSocket service with pub/sub topics
4. **SignalR (self-hosted)** — SignalR hub in the Container App with Redis backplane

### Comparison Table

| Criterion | ASP.NET SSE | Azure SignalR | Azure Web PubSub | Self-hosted SignalR |
|---|---|---|---|---|
| WebSocket support | | | | |
| SSE fallback (for CLI) | | | | |
| Per-channel pub/sub | | | | |
| Auth integration (JWT + SSE tokens) | | | | |
| Backpressure / slow consumer handling | | | | |
| Auto-reconnect on client side | | | | |
| Azure Container Apps compatibility | | | | |
| Managed vs self-hosted | | | | |
| Cost at our scale (5-20 consumers) | | | | |
| Complexity to implement | | | | |
| MAF workflow event integration | | | | |
| Aspire integration | | | | |

### Specific Questions

1. Can Azure SignalR Service do SSE fallback for CLI consumers that can't use WebSocket?
2. How does SignalR handle the "ephemeral inline stream" pattern (single agent response stream that's not a persistent channel)?
3. What's the pattern for publishing MAF workflow events (`ExecutorCompletedEvent`, `WorkflowOutputEvent`) to the real-time channel?
4. At our scale (5-20 consumers), is a managed service justified, or is self-hosted SignalR + Redis simpler?

### Output Format

- Filled comparison table
- Answer each specific question
- **Recommendation** with rationale
- **10-line code sketch** of the recommended pattern (Hub definition + event publishing)

Keep total response under 1500 words.

---

## After Research

Save claude.ai's response as: `docs/098-research/R05-response-realtime-events.md`
