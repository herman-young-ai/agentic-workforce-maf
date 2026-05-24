using System.Globalization;

namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// Strict numeric Major.Minor.Patch parser used by <see cref="AgentSeedService"/>
/// to compare YAML <c>agent_version</c> against the DB row. Lex tuple comparison;
/// equal versions are skipped, strictly-greater versions overwrite.
///
/// <para><b>No tolerant parsing.</b></para>
/// The seeder is part of host startup and a malformed YAML version is a deployment
/// bug — fail loud (Principle 8). No "v1.0.0", no "1.0.0-beta", no missing segments.
/// </summary>
internal readonly record struct AgentSemver(int Major, int Minor, int Patch)
    : IComparable<AgentSemver>
{
    public static AgentSemver Parse(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        var parts = raw.Split('.');
        if (parts.Length != 3)
            throw new FormatException(
                $"Invalid agent_version '{raw}'. Expected 'Major.Minor.Patch' (three integer segments).");

        return new AgentSemver(
            ParseSegment(parts[0], raw, "Major"),
            ParseSegment(parts[1], raw, "Minor"),
            ParseSegment(parts[2], raw, "Patch"));
    }

    public int CompareTo(AgentSemver other)
    {
        var cmp = Major.CompareTo(other.Major);
        if (cmp != 0) return cmp;
        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0) return cmp;
        return Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    private static int ParseSegment(string segment, string raw, string name)
    {
        if (!int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new FormatException(
                $"Invalid agent_version '{raw}': {name} segment '{segment}' must be a non-negative integer.");
        return value;
    }
}
