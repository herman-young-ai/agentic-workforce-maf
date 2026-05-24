namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// Tunable thresholds for <see cref="BudgetService"/>. Bound from
/// configuration section <c>Budget</c>.
/// </summary>
public sealed class BudgetServiceOptions
{
    public const string SectionName = "Budget";

    /// <summary>
    /// Fraction of the project budget ceiling at which a warning is emitted
    /// to the structured log. 0.80 = warn at 80% utilisation. Must be in
    /// (0, 1].
    /// </summary>
    public decimal WarningThreshold { get; set; } = 0.80m;
}
