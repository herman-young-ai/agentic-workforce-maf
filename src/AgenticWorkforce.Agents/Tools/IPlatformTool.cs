namespace AgenticWorkforce.Agents.Tools;

/// <summary>
/// Marker interface for in-process Platform-domain tools. The architecture
/// test asserts no implementer transitively depends on HttpClient, System.IO.File,
/// or System.Diagnostics.Process — Platform tools are limited to internal DB
/// reads through repository interfaces.
/// </summary>
internal interface IPlatformTool
{
}
