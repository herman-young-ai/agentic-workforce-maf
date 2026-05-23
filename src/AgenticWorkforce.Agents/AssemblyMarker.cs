namespace AgenticWorkforce.Agents;

/// <summary>
/// Placeholder marker for the <c>AgenticWorkforce.Agents</c> assembly.
///
/// <para><b>Status</b></para>
/// Phase 6+ — this project will host MAF agent wrappers, tool
/// definitions, prompts, and the <c>IAgentRuntime</c> implementation that
/// the Worker consumes. It exists today as a wired-up but otherwise empty
/// csproj so the dependency graph (Worker → Agents → Domain) and CI
/// pipelines are in place; rather than deleting it (and the Worker's
/// ProjectReference) only to restore them in Phase 6, the assembly is
/// kept as a stable contract surface with this single internal marker
/// type to give the C# compiler something to produce.
///
/// <para>Replace this file with real content as Phase 6 begins.</para>
/// </summary>
internal static class AssemblyMarker
{
    /// <summary>Phase identifier — bumped when this assembly first hosts
    /// production types so reviewers can grep for the marker.</summary>
    internal const string Phase = "6+ pending";
}
