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
    /// <summary>零售价（每包装价格，如每盒10元）</summary>
    public decimal UnitPrice { get; set; }
    /// <summary>每包装数量（从规格解析，如12片/盒）</summary>
    public decimal PackQuantity { get; set; } = 1;
    /// <summary>每单位价格（零售价÷包装数量，如每片0.83元）</summary>
    public decimal UnitPricePerUnit { get; set; }
    public decimal Subtotal { get; set; }

    /// <summary>从规格字符串解析每包装数量，如 "0.25g×12片" → 12</summary>
    public static decimal ParsePackQuantity(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return 1;
        // 匹配 ×数字 或 *数字 模式
        var match = System.Text.RegularExpressions.Regex.Match(spec, @"[×x*]\s*(\d+(?:\.\d+)?)\s*(片|粒|袋|支|瓶|包|盒|枚|贴)");
        if (match.Success && decimal.TryParse(match.Groups[1].Value, out var qty))
            return qty;
        // 匹配 数字+单位 在末尾
        match = System.Text.RegularExpressions.Regex.Match(spec, @"(\d+(?:\.\d+)?)\s*(片|粒|袋|支|瓶|包|枚|贴)");
        if (match.Success && decimal.TryParse(match.Groups[1].Value, out qty))
            return qty;
        return 1;
    }

    /// <summary>重新计算数量=剂量×每日次数×天数，及小计（按整盒计价，向上取整）</summary>
    public void Recalculate()
    {
        var timesPerDay = Frequency switch
        {
            "每日一次" => 1, "每日两次" => 2, "每日三次" => 3,
            "每晚一次" => 1, "必要时" => 1, _ => 3
        };
        Qty = Dose * timesPerDay * DurationDays;
        RecalculateSubtotalOnly();
    }

    /// <summary>
    /// 仅重新计算金额（手动修改数量时调用）。
    /// 计价规则：包装数量>1时按整盒计价（向上取整），包装数量=1时按实际数量计价。
    /// 例如：12片/盒，单价10元，开9片 → 1盒 × 10元 = 10元；开24片 → 2盒 × 10元 = 20元。
    /// </summary>
    public void RecalculateSubtotalOnly()
    {
        // 每单位价格（用于显示参考）
        var pricePerUnit = PackQuantity > 0 ? UnitPrice / PackQuantity : UnitPrice;
        UnitPricePerUnit = Math.Round(pricePerUnit, 4, MidpointRounding.AwayFromZero);

        if (PackQuantity > 1 && Qty > 0)
        {
            // 按整盒计价：向上取整(数量 / 每盒数量) × 每盒单价
            var packsNeeded = Math.Ceiling(Qty / PackQuantity);
            Subtotal = Math.Round(packsNeeded * UnitPrice, 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            // 包装数量=1（如中药饮片按克计价），按实际数量计价
            Subtotal = Math.Round(UnitPrice * Qty, 2, MidpointRounding.AwayFromZero);
        }
    }
}

/// <summary>循证医学辅助建议 DTO</summary>
public record EvidenceBasedAdvice(
    string DifferentialDiagnoses,
    string SuggestedExams,
    string TreatmentOptions,
    string MedicationReference,
    string RiskWarnings,
    string EvidenceLevel,
    string? RawOutput = null);

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
    string? GenericNameEn,
    string Spec,
    string Unit,
    string? DefaultUsage,
    bool IsAntibiotic,
    int AntibioticLevel,
    bool IsToxicDrug,
    string? ContraindicationTags,
    decimal? RetailPriceRef,
    /// <summary>补货阈值（null 表示未设置补货线）</summary>
    decimal? ReorderLevel = null,
    /// <summary>当前可用库存总量</summary>
    decimal AvailableQty = 0m,
    /// <summary>是否低于补货线（库存不足）</summary>
    bool IsLowStock = false);

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
    decimal? RetailPrice,
    /// <summary>补货阈值（null 表示未设置补货线）</summary>
    decimal? ReorderLevel = null,
    /// <summary>是否低于补货线</summary>
    bool IsLowStock = false);

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

/// <summary>病历列表 DTO</summary>
public record MedicalRecordListDto(
    long Id,
    long PatientId,
    string PatientName,
    string PatientGender,
    string DoctorName,
    DateTime VisitAt,
    string ChiefComplaint,
    string Diagnosis,
    string? PresentIllness,
    string? Exam,
    string? Plan);

/// <summary>病历详情 DTO（含完整诊疗字段与患者/医生信息）</summary>
public record MedicalRecordDetailDto(
    long Id,
    long PatientId,
    string PatientName,
    string PatientGender,
    long DoctorId,
    string DoctorName,
    DateTime VisitAt,
    string ChiefComplaint,
    string? PresentIllness,
    string? Exam,
    bool ExamNa,
    string? AuxiliaryExam,
    bool AuxiliaryNa,
    string Diagnosis,
    string? Plan,
    DateTime? UpdatedAt);
