using AgenticWorkforce.Agents.Tools.Common;
using AgenticWorkforce.Agents.Tools.Project;
using AgenticWorkforce.Agents.Tools.Research;
using AgenticWorkforce.Agents.Tools.Security;
using AgenticWorkforce.Agents.Tools.Software;
using AgenticWorkforce.Agents.Tools.Supervisor;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticWorkforce.Agents.Tools;

/// <summary>
/// Registers tool plumbing: the singleton <see cref="IToolRegistry"/> for
/// sandbox tools that have no per-execution state (Phase 7d ships throwing
/// stubs; Phase 11 replaces them with real ACA Dynamic Sessions dispatch),
/// and the <see cref="IPlatformToolResolver"/> +
/// <see cref="IPlatformToolFactory"/> set for in-process Platform tools that
/// capture <c>projectId</c> at construction.
/// </summary>
internal static class ToolRegistration
{
    public static IServiceCollection AddAgentTools(this IServiceCollection services)
    {
        services.AddSingleton<IToolRegistry>(_ => BuildSandboxRegistry());

        // Per-execution Platform tool resolver. The resolver itself is scoped because
        // each agent execution composes its own AITool list against its captured projectId;
        // the factories are singletons (stateless adapters).
        services.AddScoped<IPlatformToolResolver, PlatformToolResolver>();

        // Phase 7c read-only Platform tools.
        services.AddSingleton<IPlatformToolFactory, GetProjectInfoTool.Factory>();
        services.AddSingleton<IPlatformToolFactory, GetProjectTeamTool.Factory>();
        services.AddSingleton<IPlatformToolFactory, GetPcdTool.Factory>();
        services.AddSingleton<IPlatformToolFactory, GetHistoryTool.Factory>();
        services.AddSingleton<IPlatformToolFactory, GetPlanTool.Factory>();
        services.AddSingleton<IPlatformToolFactory, ListWorkflowsTool.Factory>();
        services.AddSingleton<IPlatformToolFactory, GetArtifactsTool.Factory>();
        services.AddSingleton<IPlatformToolFactory, GetLearningsTool.Factory>();

        // Phase 7d supervisor Platform tools.
        services.AddSingleton<IPlatformToolFactory, GetRecentOutcomesTool.Factory>();
        services.AddSingleton<IPlatformToolFactory, GetPastDecisionsTool.Factory>();

        // Phase 7d write Platform tools (require IPlatformActor).
        services.AddSingleton<IPlatformToolFactory, RunObjectiveTool.Factory>();
        services.AddSingleton<IPlatformToolFactory, StartResearchTool.Factory>();
        services.AddSingleton<IPlatformToolFactory, AddPrincipleTool.Factory>();

        return services;
    }

    /// <summary>
    /// Builds the singleton sandbox <see cref="IToolRegistry"/> with every Phase 7d
    /// throwing stub pre-registered. Each binding declares
    /// <see cref="ExecutionDomain.Sandbox"/>; <see cref="ToolRegistry.Resolve"/>
    /// throws if a future manifest sets <c>RequiresApproval</c> on any of these
    /// (deferred to Phase 8 alongside ApprovalRequiredAIFunction).
    /// </summary>
    private static IToolRegistry BuildSandboxRegistry()
    {
        var registry = new ToolRegistry();
        registry.Register(new ToolBinding(FileReadTool.ToolName),       FileReadTool.CreateBinding());
        registry.Register(new ToolBinding(FileWriteTool.ToolName),      FileWriteTool.CreateBinding());
        registry.Register(new ToolBinding(FileSearchTool.ToolName),     FileSearchTool.CreateBinding());
        registry.Register(new ToolBinding(ShellExecuteTool.ToolName),   ShellExecuteTool.CreateBinding());
        registry.Register(new ToolBinding(WebSearchTool.ToolName),      WebSearchTool.CreateBinding());
        registry.Register(new ToolBinding(WebFetchTool.ToolName),       WebFetchTool.CreateBinding());
        registry.Register(new ToolBinding(CodeScanTool.ToolName),       CodeScanTool.CreateBinding());
        registry.Register(new ToolBinding(DependencyScanTool.ToolName), DependencyScanTool.CreateBinding());
        registry.Register(new ToolBinding(SecretScanTool.ToolName),     SecretScanTool.CreateBinding());
        registry.Register(new ToolBinding(VulnLookupTool.ToolName),     VulnLookupTool.CreateBinding());
        registry.Register(new ToolBinding(DeepSearchTool.ToolName),     DeepSearchTool.CreateBinding());
        registry.Register(new ToolBinding(ContentExtractTool.ToolName), ContentExtractTool.CreateBinding());
        registry.Register(new ToolBinding(SourceEvaluateTool.ToolName), SourceEvaluateTool.CreateBinding());
        registry.Register(new ToolBinding(CodeAnalysisTool.ToolName),   CodeAnalysisTool.CreateBinding());
        registry.Register(new ToolBinding(ArchitectureMapTool.ToolName),ArchitectureMapTool.CreateBinding());
        registry.Register(new ToolBinding(TestRunTool.ToolName),        TestRunTool.CreateBinding());
        return registry;
    }
}
