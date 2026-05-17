using System.ComponentModel.DataAnnotations.Schema;

namespace AgenticWorkforce.Domain.Entities;

public class MilestoneSummary : ProjectScopedEntity
{
    public string Title { get; set; } = null!;
    public string Summary { get; set; } = null!;

    [Column(TypeName = "jsonb")]
    public string? WorkflowRunIds { get; set; }

    [Column(TypeName = "jsonb")]
    public string? KeyOutcomes { get; set; }

    public string[] DomainTags { get; set; } = [];
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
}
