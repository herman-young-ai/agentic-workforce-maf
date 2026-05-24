using System.Text.Json;
using AgenticWorkforce.Agents.Verification;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace AgenticWorkforce.Agents.Tests.Unit.Verification;

public class SchemaVerifierTests
{
    private static AgentCatalog AgentWithSchema(string? schemaJson, bool producesArtifact = false) => new()
    {
        AgentName = "test.agent",
        AgentVersion = "1.0.0",
        ProducesArtifact = producesArtifact,
        Interface = schemaJson is null
            ? null
            : JsonSerializer.Serialize(new AgentInterface(
                InputSchema: null,
                OutputSchema: JsonDocument.Parse(schemaJson).RootElement.Clone()),
                AgentJsonShapes.Options)
    };

    [Fact]
    public void Verify_NoInterface_Passes()
    {
        var sut = new SchemaVerifier();

        var result = sut.Verify("anything", AgentWithSchema(schemaJson: null));

        result.Passed.Should().BeTrue();
    }

    [Fact]
    public void Verify_NonJsonOutput_FailsTier1()
    {
        var sut = new SchemaVerifier();
        var agent = AgentWithSchema("""{ "type": "object", "required": ["x"] }""");

        var result = sut.Verify("not json at all", agent);

        result.Passed.Should().BeFalse();
        result.FailedAt.Should().Be(FailureTier.Tier1Structural);
        result.Reason.Should().Contain("not valid JSON");
    }

    [Fact]
    public void Verify_MissingRequiredField_FailsTier1()
    {
        var sut = new SchemaVerifier();
        var agent = AgentWithSchema("""{ "type": "object", "required": ["passed", "reason"] }""");

        var result = sut.Verify("""{ "passed": true }""", agent);

        result.Passed.Should().BeFalse();
        result.FailedAt.Should().Be(FailureTier.Tier1Structural);
        result.Reason.Should().Contain("'reason'");
    }

    [Fact]
    public void Verify_WrongTopLevelType_FailsTier1()
    {
        var sut = new SchemaVerifier();
        var agent = AgentWithSchema("""{ "type": "object" }""");

        var result = sut.Verify("""[1, 2, 3]""", agent);

        result.Passed.Should().BeFalse();
        result.FailedAt.Should().Be(FailureTier.Tier1Structural);
        result.Reason.Should().Contain("array");
        result.Reason.Should().Contain("object");
    }

    [Fact]
    public void Verify_ValidShape_Passes()
    {
        var sut = new SchemaVerifier();
        var agent = AgentWithSchema("""{ "type": "object", "required": ["passed", "reason"] }""");

        var result = sut.Verify("""{ "passed": true, "reason": "looks good", "extra": 42 }""", agent);

        result.Passed.Should().BeTrue();
    }

    [Fact]
    public void Verify_MalformedInterfaceColumn_FailsTier1()
    {
        var sut = new SchemaVerifier();
        var agent = new AgentCatalog
        {
            AgentName = "broken.agent",
            AgentVersion = "1.0.0",
            Interface = "not valid json"
        };

        var result = sut.Verify("""{ "ok": true }""", agent);

        result.Passed.Should().BeFalse();
        result.FailedAt.Should().Be(FailureTier.Tier1Structural);
        result.Reason.Should().Contain("not valid JSON");
    }
}
