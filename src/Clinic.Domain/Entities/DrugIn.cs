namespace Clinic.Domain.Entities;

public class DrugIn : Common.Entity
{
    public long DrugId { get; set; }
    public string BatchNo { get; set; } = string.Empty;
    public DateOnly ExpiryDate { get; set; }
    public decimal Qty { get; set; }
    public decimal CostPrice { get; set; }
    public string? Supplier { get; set; }
    public DateTime ReceivedAt { get; set; }
    public long OperatorId { get; set; }
}
