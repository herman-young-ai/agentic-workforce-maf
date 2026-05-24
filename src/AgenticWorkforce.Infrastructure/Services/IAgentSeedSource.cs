namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// Supplies the set of agent seed definitions to <see cref="AgentSeedService"/>.
/// Production binds <see cref="EmbeddedYamlAgentSeedSource"/>; integration tests
/// can substitute a fixture source to avoid relying on the Agents assembly's
/// embedded YAMLs.
/// </summary>
internal interface IAgentSeedSource
{
    IReadOnlyList<AgentSeedDefinition> Load();
}
