namespace Clinic.Domain.Entities;

public class DrugStock : Common.Entity
{
    public long DrugId { get; set; }
    public string BatchNo { get; set; } = string.Empty;
    public DateOnly ExpiryDate { get; set; }
    public decimal QtyRemaining { get; set; }
    public decimal CostPrice { get; set; }
    public string? Supplier { get; set; }
    public DateTime ReceivedAt { get; set; }
}
