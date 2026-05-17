using System.ComponentModel.DataAnnotations.Schema;

namespace AgenticWorkforce.Domain.Entities;

public class ModelPricing
{
    public string Model { get; set; } = null!;
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "numeric(12,6)")]
    public decimal PricePerMtokInput { get; set; }

    [Column(TypeName = "numeric(12,6)")]
    public decimal PricePerMtokOutput { get; set; }

    [Column(TypeName = "numeric(12,6)")]
    public decimal PricePerMtokCacheRead { get; set; }

    [Column(TypeName = "numeric(12,6)")]
    public decimal PricePerMtokCacheCreate { get; set; }
}
