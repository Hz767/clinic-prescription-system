namespace Clinic.Domain.Entities;

public class DrugOut : Common.Entity
{
    public long DrugId { get; set; }
    public string BatchNo { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public long? PrescriptionId { get; set; }
    public DateTime OccurredAt { get; set; }
    public long OperatorId { get; set; }
    public bool IsReversal { get; set; }
}
