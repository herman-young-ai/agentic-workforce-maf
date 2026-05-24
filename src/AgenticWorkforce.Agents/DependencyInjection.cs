using System.Threading.Channels;
using AgenticWorkforce.Agents.Context;
using AgenticWorkforce.Agents.Prompts;
using AgenticWorkforce.Agents.Runtime;
using AgenticWorkforce.Agents.Services;
using AgenticWorkforce.Agents.Tools;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgenticWorkforce.Agents;

public static class AgentServiceExtensions
{
    /// <summary>
    /// Registers everything in <c>AgenticWorkforce.Agents</c>. Hard prerequisites
    /// (registered by <c>AddInfrastructure</c>):
    ///   - IBudgetService, IModelPricingService, ITokenCounter
    ///   - ILlmCallRepository, IAgentCatalogRepository, IProjectAgentRepository, IProjectRepository
    ///   - IProjectContextService, ILearningRepository
    ///   - IMemoryCache, TimeProvider
    ///
    /// All hardcoded runtime constants (timeouts, batch sizes, channel capacity,
    /// cache bounds, default provider/model) are bound from configuration section
    /// <see cref="AgentRuntimeOptions.SectionName"/> so ops can tune without code changes.
    /// </summary>
    public static IServiceCollection AddAgentServices(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        // Options
        var optionsBuilder = services.AddOptions<AgentRuntimeOptions>();
        if (configuration is not null)
            optionsBuilder.Bind(configuration.GetSection(AgentRuntimeOptions.SectionName));
        optionsBuilder.Validate(
            o => o.LlmCallChannelCapacity > 0
              && o.LlmCallDrainBatchSize > 0
              && o.MaxCachedChatClientPipelines > 0
              && o.DefaultExecutionTimeout > TimeSpan.Zero
              && o.LlmCallDrainFlushInterval > TimeSpan.Zero
              && o.ChatClientPipelineExpiration > TimeSpan.Zero
              && o.BudgetWarningThreshold is > 0 and <= 1
              && !string.IsNullOrWhiteSpace(o.DefaultProvider)
              && !string.IsNullOrWhiteSpace(o.DefaultModel),
            "AgentRuntimeOptions has invalid values (non-positive numeric, empty default provider/model, or warn-threshold outside (0,1]).");

        // Runtime
        services.AddScoped<IAgentRuntime, AgentRuntime>();
        services.AddSingleton<IAgentFactory, AgentFactory>();
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();

        // Prompts
        services.AddSingleton<IPromptAssembler, PromptAssembler>();

        // Tools (empty registry in Phase 6; tool plugins land in later phases)
        services.AddAgentTools();

        // Context (Phase 7 will wire the provider into AIContextProviders on the agent)
        services.AddScoped<IProjectContextProviderFactory, ProjectContextProviderFactory>();
        services.AddScoped<IContextAssembler, ContextAssembler>();

        // Middleware channels (bounded — Principle 19). Capacity from options.
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AgentRuntimeOptions>>().Value;
            return Channel.CreateBounded<LlmCall>(
                new BoundedChannelOptions(opts.LlmCallChannelCapacity) { FullMode = BoundedChannelFullMode.Wait });
        });
        services.AddSingleton(sp => sp.GetRequiredService<Channel<LlmCall>>().Writer);
        services.AddSingleton(sp => sp.GetRequiredService<Channel<LlmCall>>().Reader);
        services.AddHostedService<LlmCallDrainService>();

        return services;
    }
}
