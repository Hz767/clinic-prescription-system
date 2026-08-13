using Clinic.Application.DTOs;

namespace Clinic.Application.Interfaces;

/// <summary>
/// 认证服务接口。处理用户登录、登出、会话管理。
/// </summary>
public interface IAuthService
{
    Task<bool> LoginAsync(string username, string password, CancellationToken ct = default);
    void Logout();
    bool IsAuthenticated { get; }
    long? CurrentUserId { get; }
    string? CurrentUserName { get; }
}

/// <summary>
/// 患者档案服务接口。处理患者建档、查询、更新。
/// </summary>
public interface IPatientService
{
    Task<long> CreatePatientAsync(
        string name, string gender, DateOnly? dob,
        string phone, string? allergies, string? history,
        string? chronicTags,
        decimal? weight = null, decimal? temperature = null,
        int? systolicBP = null, int? diastolicBP = null, int? heartRate = null,
        CancellationToken ct = default);

    /// <summary>P2预备：更新患者档案信息</summary>
    Task<bool> UpdatePatientAsync(
        long id, string name, string gender, DateOnly? dob,
        string phone, string? allergies, string? history,
        string? chronicTags, CancellationToken ct = default);

    /// <summary>更新患者体征信息（体重、体温、血压、心率）</summary>
    Task<bool> UpdateVitalsAsync(
        long id, decimal? weight, decimal? temperature,
        int? systolicBP, int? diastolicBP, int? heartRate,
        CancellationToken ct = default);

    Task<PatientDto?> GetPatientByIdAsync(long id, CancellationToken ct = default);

    Task<PatientDto?> FindByPhoneAsync(string phone, CancellationToken ct = default);

    Task<IReadOnlyList<PatientDto>> SearchByNameAsync(string nameKeyword, CancellationToken ct = default);

    Task<IReadOnlyList<PatientDto>> GetAllPatientsAsync(CancellationToken ct = default);
}

/// <summary>
/// 处方开具服务接口。核心业务流程。
/// </summary>
public interface IPrescriptionService
{
    Task<long> CreatePrescriptionAsync(
        long patientId, long doctorId, string? chiefComplaint, string diagnosisText,
        string? diagnosisCode, int prescriptionType,
        string? extendedReason,
        decimal? weight = null, decimal? temperature = null,
        int? systolicBP = null, int? diastolicBP = null, int? heartRate = null,
        CancellationToken ct = default);

    Task<long> AddPrescriptionItemAsync(
        long prescriptionId, long drugId, decimal dose, string doseUnit,
        string frequency, string route, int durationDays, decimal qty,
        CancellationToken ct = default);

    /// <summary>更新处方明细（支持内联编辑后持久化，仅 Draft 状态可修改）</summary>
    Task<bool> UpdatePrescriptionItemAsync(
        long prescriptionId, long itemId, decimal dose, string doseUnit,
        string frequency, string route, int durationDays, decimal qty,
        CancellationToken ct = default);

    /// <summary>删除处方中的指定药品明细（仅 Draft 状态可删除）</summary>
    Task RemovePrescriptionItemAsync(long prescriptionId, long itemId, CancellationToken ct = default);

    Task<bool> SavePrescriptionAsync(long prescriptionId, CancellationToken ct = default);

    /// <summary>
    /// 药师审核处方（《处方管理办法》要求药师审核后方可收费）。
    /// 仅 Saved 状态处方可审核，审核后变为 Reviewed 状态。
    /// </summary>
    Task<bool> ReviewPrescriptionAsync(long prescriptionId, string? reviewNote, CancellationToken ct = default);

    /// <summary>
    /// AI 辅助预审处方（辅助药师审核，不替代人工决定）。
    /// 返回用药合理性、药物相互作用、剂量范围等建议。
    /// </summary>
    Task<string> GetAiReviewSuggestionsAsync(long prescriptionId, CancellationToken ct = default);

    Task<bool> VoidPrescriptionAsync(long prescriptionId, string voidReason, CancellationToken ct = default);

    /// <summary>获取处方完整信息（含明细列表）</summary>
    Task<PrescriptionDto?> GetPrescriptionByIdAsync(long prescriptionId, CancellationToken ct = default);

    /// <summary>
    /// 生成处方 PDF 文件。加载处方完整数据，调用 PDF 服务渲染，更新 PdfPath。
    /// </summary>
    /// <param name="prescriptionId">处方 ID</param>
    /// <param name="outputDir">PDF 输出目录</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>生成的 PDF 文件路径；处方不存在时返回 null</returns>
    Task<string?> GeneratePrescriptionPdfAsync(long prescriptionId, string outputDir, CancellationToken ct = default);

    /// <summary>获取处方历史记录（可按患者姓名或处方编号筛选）</summary>
    Task<IReadOnlyList<PrescriptionHistoryDto>> GetPrescriptionHistoryAsync(
        string? searchKeyword = null, DateTime? fromDate = null, DateTime? toDate = null,
        CancellationToken ct = default);
}

/// <summary>
/// 药品进销存服务接口。
/// </summary>
public interface IInventoryService
{
    Task<long> StockInAsync(
        long drugId, string batchNo, DateOnly expiryDate,
        decimal qty, decimal costPrice, string? supplier,
        long operatorId, CancellationToken ct = default);

    /// <summary>手动出库（报损/调拨等），按 FIFO 扣减库存并生成 DrugOut 记录</summary>
    Task StockOutAsync(long drugId, int qty, string reason, long operatorId, CancellationToken ct = default);

    Task<decimal> GetStockQuantityAsync(long drugId, CancellationToken ct = default);

    Task<IReadOnlyList<ExpiryAlertDto>> GetExpiryAlertsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<DrugDto>> GetAllDrugsAsync(CancellationToken ct = default);

    /// <summary>获取所有药品的库存汇总（含总库存量、批次数、最早效期）</summary>
    Task<IReadOnlyList<DrugStockSummaryDto>> GetAllStockSummaryAsync(CancellationToken ct = default);

    /// <summary>获取指定药品的库存批次明细</summary>
    Task<IReadOnlyList<StockBatchDto>> GetStockBatchesAsync(long drugId, CancellationToken ct = default);

    /// <summary>获取入库流水记录（可按日期范围筛选）</summary>
    Task<IReadOnlyList<StockInRecordDto>> GetStockInHistoryAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken ct = default);
}

/// <summary>
/// 收费结算服务接口。
/// </summary>
public interface IBillingService
{
    Task<long> RecordPaymentAsync(
        long prescriptionId, int method, decimal amount,
        long operatorId, string? posSerialNo, string? note,
        CancellationToken ct = default);

    /// <summary>独立退费：将已收费处方退费，生成反转支付记录、恢复库存、处方状态改为 Voided</summary>
    Task RefundAsync(long prescriptionId, long operatorId, string? reason, CancellationToken ct = default);

    Task<DailyReportDto?> GetDailyReportAsync(DateTime date, CancellationToken ct = default);

    /// <summary>获取收费流水记录（可按日期范围筛选）</summary>
    Task<IReadOnlyList<PaymentRecordDto>> GetPaymentHistoryAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken ct = default);
}

// ── DTO 定义 ──

public record PatientDto(
    long Id, string Name, string Gender, DateOnly? Dob,
    string Phone, string? Allergies, string? History, string? ChronicTags,
    decimal? Weight, decimal? Temperature, int? SystolicBP, int? DiastolicBP, int? HeartRate);

public record ExpiryAlertDto(
    long DrugId, string DrugName, string BatchNo,
    DateOnly ExpiryDate, int DaysRemaining, decimal QtyRemaining);

public record DailyReportDto(
    DateTime Date, int PrescriptionCount, decimal TotalAmount,
    decimal CashAmount, decimal PosAmount);
