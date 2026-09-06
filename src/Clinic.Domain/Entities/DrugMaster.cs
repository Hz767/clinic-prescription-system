using Clinic.Shared.Enums;

namespace Clinic.Domain.Entities;

public class DrugMaster : Common.Entity
{
    public string GenericNameCn { get; set; } = string.Empty;
    public string? GenericNameEn { get; set; }
    public string Spec { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string? DefaultUsage { get; set; }
    public bool IsAntibiotic { get; set; }
    public AntibioticLevel AntibioticLevel { get; set; }
    public bool IsToxicDrug { get; set; }
    public string? ContraindicationTags { get; set; }
    public decimal? CostPriceRef { get; set; }
    public decimal? RetailPriceRef { get; set; }

    /// <summary>补货阈值：库存总量低于该值视为低库存，提醒补货</summary>
    public decimal? ReorderLevel { get; set; }
}
