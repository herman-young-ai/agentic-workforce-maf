using AgenticWorkforce.Agents.Tools.Project;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticWorkforce.Agents.Tools;

/// <summary>
/// Registers tool plumbing: the singleton <see cref="IToolRegistry"/> for
/// sandbox / MCP tools that have no per-execution state, and the
/// <see cref="IPlatformToolResolver"/> + every <see cref="IPlatformToolFactory"/>
/// for in-process Platform tools that capture <c>projectId</c> at construction.
/// </summary>
internal static class ToolRegistration
{
    public static IServiceCollection AddAgentTools(this IServiceCollection services)
    {
        services.AddSingleton<IToolRegistry, ToolRegistry>();

        // Per-execution Platform tool resolver. The resolver itself is scoped because
        // each agent execution composes its own AITool list against its captured projectId;
        // the factories are singletons (stateless adapters).
        services.AddScoped<IPlatformToolResolver, PlatformToolResolver>();

        // Phase 7c read-only Platform tools. The write tools (run_objective,
        // start_research, add_principle) and the supervisor tools register in 7d
        // once the platform-actor identity is wired.
        services.AddSingleton<IPlatformToolFactory, GetProjectInfoTool.Factory>();
        services.AddSingleton<IPlatformToolFactory, GetProjectTeamTool.Factory>();
        services.AddSingleton<IPlatformToolFactory, GetPcdTool.Factory>();
        services.AddSingleton<IPlatformToolFactory, GetHistoryTool.Factory>();
        services.AddSingleton<IPlatformToolFactory, GetPlanTool.Factory>();
        services.AddSingleton<IPlatformToolFactory, ListWorkflowsTool.Factory>();
        services.AddSingleton<IPlatformToolFactory, GetArtifactsTool.Factory>();
        services.AddSingleton<IPlatformToolFactory, GetLearningsTool.Factory>();

        return services;
    }
}
