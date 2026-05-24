using AgenticWorkforce.Agents.Prompts;
using AgenticWorkforce.Agents.Tools;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgenticWorkforce.Agents.Runtime;

/// <summary>
/// 6-step agent construction:
///   1. Resolve shared IChatClient pipeline for (provider, model)
///   2. Assemble 5-layer Instructions string
///   3. Resolve tool manifest -> IList&lt;AITool&gt;
///   4. (MCP resolution — wired in Phase 7+)
///   5. (Per-execution ProjectContextProvider — wired into AIContextProviders in Phase 7+)
///   6. Construct ChatClientAgent (wrapped in TaggingChatClient so middleware sees per-call tags)
/// </summary>
internal sealed class AgentFactory(
    IChatClientFactory chatClients,
    IPromptAssembler prompts,
    IToolRegistry tools,
    IOptions<AgentRuntimeOptions> options) : IAgentFactory
{
    private readonly AgentRuntimeOptions _opts = options.Value;

    public async Task<AIAgent> CreateAsync(
        AgentCatalog catalog,
        Project project,
        ProjectAgent? projectAgent,
        AgentExecutionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(context);

        // Step 1 — shared pipeline for (provider, model). ModelConfig parsing
        // lands in Phase 7; until then every catalog entry routes through the
        // configured default (AgentRuntimeOptions.DefaultProvider / DefaultModel).
        var (provider, model) = ResolveProviderAndModel(catalog);
        var sharedPipeline = chatClients.GetOrCreate(provider, model);

        // Per-execution tagging wrapper — sets AWP tags on every ChatOptions
        // so all middleware below has project/task/session context.
        IChatClient chat = new TaggingChatClient(sharedPipeline, context, provider, agentRole: null);

        // Step 2 — Instructions (static per agent construction).
        var instructions = await prompts.AssembleAsync(catalog, project, projectAgent, ct).ConfigureAwait(false);

        // Step 3 — Tool manifest. Catalog.Tools is jsonb (manifest names); Phase 7 parses it.
        // Phase 6 ships with an empty manifest (Principle 14: Secure by Default).
        IList<AITool> resolvedTools = tools.Resolve(Array.Empty<ToolBinding>());

        // Step 6 — Construct ChatClientAgent via the positional-args constructor.
        // (ChatClientAgentOptions has no Instructions property, so we use the
        // constructor that takes instructions directly.)
        return new ChatClientAgent(
            chatClient: chat,
            instructions: instructions,
            name: catalog.AgentName,
            description: catalog.Description,
            tools: resolvedTools.Count > 0 ? resolvedTools : null);
    }

    private (string Provider, string Model) ResolveProviderAndModel(AgentCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(catalog.AgentVersion))
            throw new InvalidStateException(
                $"AgentCatalog '{catalog.AgentName}' is missing AgentVersion. Catalog rows must declare a version.");

        // Phase 7 will parse catalog.ModelConfig (jsonb). Phase 6 routes through the configured default,
        // which is explicit via AgentRuntimeOptions rather than a magic string in code.
        if (string.IsNullOrWhiteSpace(_opts.DefaultProvider) || string.IsNullOrWhiteSpace(_opts.DefaultModel))
            throw new InvalidStateException(
                "AgentRuntimeOptions.DefaultProvider and DefaultModel must both be set (catalog ModelConfig parsing arrives in Phase 7; no implicit fallback in code).");
        return (_opts.DefaultProvider, _opts.DefaultModel);
    }
}
