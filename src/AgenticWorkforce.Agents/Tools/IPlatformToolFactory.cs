using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools;

/// <summary>
/// Builds the <see cref="AITool"/> for a single Platform tool, capturing
/// <c>projectId</c> at construction so the model-facing surface never
/// receives it as a parameter. Implementations resolve their own
/// repository dependencies from the injected <see cref="IServiceProvider"/>.
/// </summary>
internal interface IPlatformToolFactory
{
    /// <summary>Stable, dot-separated manifest name, e.g. <c>"project.get_info"</c>.</summary>
    string ToolName { get; }

    AITool Create(IServiceProvider services, Guid projectId);
}
