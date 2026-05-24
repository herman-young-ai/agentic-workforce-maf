using AgenticWorkforce.Agents.Prompts;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace AgenticWorkforce.Agents.Tests.Unit.Prompts;

public class PromptAssemblerTests
{
    private static AgentCatalog MakeCatalog(string? type = "system", string? systemPrompt = "Be precise.") => new()
    {
        AgentName = "test.agent",
        AgentType = type,
        AgentVersion = "1.0.0",
        SystemPrompt = systemPrompt,
        Enabled = true,
        Keywords = Array.Empty<string>()
    };

    private static Project MakeProject(string? brief = "We are building a thing.") => new()
    {
        Name = "Test Project",
        Objective = "Do the thing",
        Brief = brief,
        Status = ProjectStatus.Active
    };

    [Fact]
    public async Task AssembleAsync_Includes_All_FiveLayers_WhenPresent()
    {
        var sut = new PromptAssembler();
        var projectAgent = new ProjectAgent { UserPrompt = "Match house style." };

        var result = await sut.AssembleAsync(MakeCatalog(), MakeProject(), projectAgent);

        result.Should().Contain("# Organization");
        result.Should().Contain("# Category");
        result.Should().Contain("Be precise.");
        result.Should().Contain("# Project Brief");
        result.Should().Contain("We are building a thing.");
        result.Should().Contain("Match house style.");
    }

    [Fact]
    public async Task AssembleAsync_Omits_OptionalLayers_WhenAbsent()
    {
        var sut = new PromptAssembler();
        var catalog = MakeCatalog(systemPrompt: null);
        var project = MakeProject(brief: null);

        var result = await sut.AssembleAsync(catalog, project, projectAgent: null);

        result.Should().Contain("# Organization");
        result.Should().Contain("# Category");
        result.Should().NotContain("# Agent\n");
        result.Should().NotContain("# Project Brief");
        result.Should().NotContain("# Project-Specific Instructions");
    }

    [Fact]
    public async Task AssembleAsync_UnknownAgentType_Throws()
    {
        var sut = new PromptAssembler();
        var catalog = MakeCatalog(type: "totally-made-up");

        var act = async () => await sut.AssembleAsync(catalog, MakeProject(), projectAgent: null);

        await act.Should().ThrowAsync<InvalidStateException>().WithMessage("*totally-made-up*");
    }
}
