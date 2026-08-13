namespace Clinic.Domain.Entities;

public class PrescriptionItem : Common.Entity
{
    public long PrescriptionId { get; set; }
    public long DrugId { get; set; }
    public string DrugName { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public decimal Dose { get; set; }
    public string DoseUnit { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public decimal Qty { get; set; }
    public long? BatchIdOut { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
}
