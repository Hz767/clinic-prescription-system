using Clinic.Shared.Enums;

namespace Clinic.Domain.Entities;

public class StockCheck : Common.Entity
{
    public DateTime OccurredAt { get; set; }
    public long OperatorId { get; set; }
    public StockCheckMode Mode { get; set; }
    public string? DiffJson { get; set; }
    public string? Note { get; set; }
    public string? SignaturesJson { get; set; }
}
