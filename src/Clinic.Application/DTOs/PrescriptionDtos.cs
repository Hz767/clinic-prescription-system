namespace Clinic.Application.DTOs;

/// <summary>处方明细 DTO（可变属性，支持 DataGrid 内联编辑）</summary>
public class PrescriptionItemDto
{
    public long Id { get; set; }
    public long DrugId { get; set; }
    public string DrugName { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public decimal Dose { get; set; }
    public string DoseUnit { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }

    /// <summary>重新计算数量=剂量×每日次数×天数，及小计=数量×单价</summary>
    public void Recalculate()
    {
        var timesPerDay = Frequency switch
        {
            "每日一次" => 1, "每日两次" => 2, "每日三次" => 3,
            "每晚一次" => 1, "必要时" => 1, _ => 3
        };
        Qty = Dose * timesPerDay * DurationDays;
        Subtotal = Math.Round(UnitPrice * Qty, 2, MidpointRounding.AwayFromZero);
    }
}

/// <summary>处方概览 DTO（含本次就诊体征数据）</summary>
public record PrescriptionDto(
    long Id,
    string NoYearSeq,
    long PatientId,
    string PatientName,
    long DoctorId,
    string DoctorName,
    string? ChiefComplaint,
    string DiagnosisText,
    string? DiagnosisCode,
    decimal TotalAmount,
    int Type,
    int Status,
    DateTime CreatedAt,
    IReadOnlyList<PrescriptionItemDto> Items,
    // 本次就诊体征（实时变量，非患者长期数据）
    decimal? Weight = null,
    decimal? Temperature = null,
    int? SystolicBP = null,
    int? DiastolicBP = null,
    int? HeartRate = null);

/// <summary>药品目录 DTO</summary>
public record DrugDto(
    long Id,
    string GenericNameCn,
    string GenericNameEn,
    string Spec,
    string Unit,
    string? DefaultUsage,
    bool IsAntibiotic,
    int AntibioticLevel,
    bool IsToxicDrug,
    string? ContraindicationTags,
    decimal? RetailPriceRef);

/// <summary>库存批次 DTO</summary>
public record StockBatchDto(
    long Id,
    long DrugId,
    string DrugName,
    string BatchNo,
    DateOnly ExpiryDate,
    decimal QtyRemaining,
    decimal CostPrice,
    string? Supplier,
    DateTime ReceivedAt);

/// <summary>用户信息 DTO</summary>
public record UserDto(
    long Id,
    string Username,
    string DisplayName,
    int Role,
    bool IsActive,
    DateTime? LastLoginAt);

/// <summary>药品库存汇总 DTO</summary>
public record DrugStockSummaryDto(
    long DrugId,
    string DrugName,
    string Spec,
    string Unit,
    decimal TotalQty,
    int BatchCount,
    DateOnly? EarliestExpiry,
    decimal? RetailPrice);

/// <summary>入库流水记录 DTO</summary>
public record StockInRecordDto(
    long Id,
    long DrugId,
    string DrugName,
    string BatchNo,
    DateOnly ExpiryDate,
    decimal Qty,
    decimal CostPrice,
    string? Supplier,
    DateTime ReceivedAt,
    long OperatorId,
    string OperatorName);

/// <summary>收费流水记录 DTO</summary>
public record PaymentRecordDto(
    long Id,
    long PrescriptionId,
    string PrescriptionNo,
    string PatientName,
    int Method,
    string MethodText,
    decimal Amount,
    DateTime OccurredAt,
    long OperatorId,
    string OperatorName,
    string? PosSerialNo,
    string? Note,
    bool IsReversal);

/// <summary>处方历史记录 DTO</summary>
public record PrescriptionHistoryDto(
    long Id,
    string NoYearSeq,
    long PatientId,
    string PatientName,
    string DoctorName,
    string DiagnosisText,
    decimal TotalAmount,
    int Type,
    int Status,
    DateTime CreatedAt,
    int ItemCount);

/// <summary>哈希链验证结果 DTO</summary>
public record HashChainVerificationResult(
    bool IsValid,
    int TotalRecords,
    int VerifiedRecords,
    long? FirstBrokenId,
    string? ErrorMessage);
