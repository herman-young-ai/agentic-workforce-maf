namespace AgenticWorkforce.Agents;

/// <summary>
/// Public type token used by host wiring to obtain a reference to the
/// <see cref="System.Reflection.Assembly"/> that owns the embedded YAML
/// seed resources. No business logic — keeping it as a marker means the
/// Agents project does not have to expose the seed loader publicly just
/// to let downstream wiring resolve the resource stream.
/// </summary>
public sealed class AgentsAssemblyMarker
{
    private AgentsAssemblyMarker() { }
}
