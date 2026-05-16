# R11: MCP and A2A Protocol Integration in MAF

## Prompt for claude.ai

---

I am building an AI agent orchestration platform in C# using Microsoft Agent Framework (MAF) on Azure. The platform needs to integrate with external tools (security scanners, code analysis, internal bank APIs) and potentially expose agents for consumption by other platforms. MAF has native support for both MCP (Model Context Protocol) and A2A (Agent-to-Agent protocol).

Give me a **practical integration guide** — what's production-ready today, what's experimental, and code patterns for each.

### Our Integration Needs

**External tools we need to integrate (via MCP or function tools):**

| Tool | What It Does | Integration Pattern |
|---|---|---|
| Security scanner (Bandit, Semgrep) | Static analysis on code | Run in container, return findings as JSON |
| Dependency checker (Safety, Snyk) | Vulnerability scanning | API call or container execution |
| Code quality (SonarQube) | Quality metrics | REST API call |
| Internal compliance API | Check against bank policies | REST API (Entra ID auth) |
| Document store (SharePoint) | Read playbooks and policies | Microsoft Graph API |
| Knowledge base (Azure AI Search) | Semantic search over internal docs | Azure SDK |
| Git operations | Clone, diff, commit, branch | Shell execution in sandbox |
| Web search (Perplexity, Tavily, Brave) | Research capability | REST API with failover chain |

**Agent interop (A2A):**
- Expose our Mission Control agents as A2A endpoints (so other bank platforms can invoke them)
- Consume external A2A agents (e.g. a compliance checking agent hosted by another team)
- Future: federated agent teams across business units

### Questions to Answer

**Section 1: MCP in MAF**

1. What are MAF "Hosted MCP Tools" vs "Local MCP Tools"? What's the difference?
2. Which MAF providers support MCP tools? (from the support matrix)
3. How do I build a custom MCP server in C# that wraps our security scanner? Show the minimal server skeleton.
4. How do I register a local MCP server with a MAF agent? Show the C# code.
5. Can MCP tools run in a separate process/container from the agent (important for isolation)?
6. What's the MCP tool approval pattern in MAF? Can I require human approval before a tool executes?
7. Performance: what's the overhead of MCP (stdio/HTTP transport) vs native function tools?
8. Can an MCP server maintain state across tool calls within the same agent session?

**Section 2: A2A Protocol in MAF**

1. What is A2A and how does it work at a protocol level? (brief — 3 sentences)
2. How do I expose a MAF agent as an A2A endpoint? Show the ASP.NET Core setup.
3. How do I consume a remote A2A agent from MAF? Show creating an `A2AAgent` proxy.
4. Can A2A agents participate in MAF workflows (as executors)?
5. What auth does A2A use? Can it integrate with Entra ID?
6. What's the current maturity level? Production-ready or experimental?
7. Does A2A support streaming responses?

**Section 3: Function Tools vs MCP — When to Use Which**

For each of our integration needs above, recommend whether to use:
- **Native function tool** (C# method registered with `AIFunctionFactory.Create`)
- **Local MCP tool** (separate MCP server process)
- **Hosted MCP tool** (Foundry-hosted)
- **A2A agent** (remote agent invocation)

Give the recommendation as a table with rationale.

**Section 4: Web Search Integration**

Our agents need web search with a failover chain (Perplexity → Tavily → Brave). Show:
1. How to implement a web search function tool with failover in C#
2. Does MAF have a built-in web search tool? What does it use?
3. Can we use Foundry's hosted web search tool?

### Output Format

- Answer each question concisely (1-3 sentences + code where relevant)
- Code sketches should be 10-20 lines C#
- Recommendation table for tool integration approach
- Flag anything that's not production-ready

Keep total response under 2500 words.

---

## After Research

Save claude.ai's response as: `docs/098-research/R11-response-mcp-a2a.md`
