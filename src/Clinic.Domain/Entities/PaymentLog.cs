using Clinic.Shared.Enums;

namespace Clinic.Domain.Entities;

public class PaymentLog : Common.Entity
{
    public long PrescriptionId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public DateTime OccurredAt { get; set; }
    public long OperatorId { get; set; }
    public string? PosSerialNo { get; set; }
    public string? PosImagePath { get; set; }
    public string? Note { get; set; }
    /// <summary>是否冲正记录（作废已收费处方时生成，对应退款）</summary>
    public bool IsReversal { get; set; }
}
