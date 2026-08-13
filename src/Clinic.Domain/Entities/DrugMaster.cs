using Clinic.Shared.Enums;

namespace Clinic.Domain.Entities;

public class DrugMaster : Common.Entity
{
    public string GenericNameCn { get; set; } = string.Empty;
    public string GenericNameEn { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string? DefaultUsage { get; set; }
    public bool IsAntibiotic { get; set; }
    public AntibioticLevel AntibioticLevel { get; set; }
    public bool IsToxicDrug { get; set; }
    public string? ContraindicationTags { get; set; }
    public decimal? CostPriceRef { get; set; }
    public decimal? RetailPriceRef { get; set; }
}
