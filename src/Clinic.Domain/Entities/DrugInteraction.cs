using Clinic.Shared.Enums;

namespace Clinic.Domain.Entities;

public class DrugInteraction : Common.Entity
{
    public int DdinterIdA { get; set; }
    public string DrugNameA { get; set; } = string.Empty;
    public int DdinterIdB { get; set; }
    public string DrugNameB { get; set; } = string.Empty;
    public DrugInteractionLevel Level { get; set; }
    public string? SourceAtcCode { get; set; }
    public DateTime ImportedAt { get; set; }
}
