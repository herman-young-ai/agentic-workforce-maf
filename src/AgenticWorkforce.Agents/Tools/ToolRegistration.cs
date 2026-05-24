using Microsoft.Extensions.DependencyInjection;

namespace AgenticWorkforce.Agents.Tools;

/// <summary>
/// Registers the tool registry singleton. Concrete tool registrations live
/// in feature-specific extension methods invoked from here in later phases
/// (project.* in Phase 7, web.* / file.* / shell.* via Dynamic Sessions in
/// Phase 7+). Phase 6 ships an empty registry.
/// </summary>
internal static class ToolRegistration
{
    public static IServiceCollection AddAgentTools(this IServiceCollection services)
    {
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        return services;
    }
}
