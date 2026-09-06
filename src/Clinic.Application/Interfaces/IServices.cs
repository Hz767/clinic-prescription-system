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

    /// <summary>
    /// 更新当前登录用户的显示名称（用于处方签名）。
    /// 同步更新数据库和会话状态。
    /// </summary>
    /// <param name="newDisplayName">新的显示名称</param>
    /// <param name="ct">取消令牌</param>
    Task UpdateDisplayNameAsync(string newDisplayName, CancellationToken ct = default);
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
        string? tags = null,
        CancellationToken ct = default);

    /// <summary>P2预备：更新患者档案信息</summary>
    Task<bool> UpdatePatientAsync(
        long id, string name, string gender, DateOnly? dob,
        string phone, string? allergies, string? history,
        string? chronicTags, string? tags = null, CancellationToken ct = default);

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
        decimal consultationFee = 0m,
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

    Task<bool> SavePrescriptionAsync(long prescriptionId, string? overrideReason = null, CancellationToken ct = default);

    /// <summary>
    /// 保存前校验：返回阻断性问题的清单（过敏匹配 / 药物相互作用 Major / 禁忌症匹配）。
    /// 列表为空表示可正常保存；非空时医生需填写临床覆盖理由（OverrideReason）后方可保存。
    /// </summary>
    Task<IReadOnlyList<string>> GetPrescriptionBlockersAsync(long prescriptionId, CancellationToken ct = default);

    /// <summary>
    /// 发药（药房配药发药）：仅「已收费」状态处方可发药，发药时按 FIFO 扣减库存并生成出库流水。
    /// 药师 / 护士 / 医生角色均可执行。已扣减过库存的处方（历史数据）不会重复扣减。
    /// </summary>
    Task<bool> DispensePrescriptionAsync(long prescriptionId, CancellationToken ct = default);

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

    /// <summary>
    /// 导入国内药品清单（国家医保目录等）到药品主数据。
    /// 按「通用名」去重：已存在同通用名的药品跳过；自动解析剂型/类别、识别抗菌药物分级。
    /// 走与手工录入一致的业务入口（不含库存数量，仅药品目录）。
    /// </summary>
    Task<DrugImportResultDto> ImportNationalDrugListAsync(
        IReadOnlyList<NationalDrugEntryDto> entries, CancellationToken ct = default);
}

/// <summary>
/// 国内药品清单数据源（从 GitHub 下载医保药品目录 CSV 并解析）。
/// 默认数据源：lrpopeyou/MedicalInsuranceKG 的《药品信息.csv》（国家医保目录 + 各省增补）。
/// </summary>
public interface INationalDrugListSource
{
    /// <summary>默认数据源 URL（GitHub raw）</summary>
    string DefaultSourceUrl { get; }

    /// <summary>从指定 URL 下载并解析药品清单 CSV，返回条目列表（表头/坏行自动跳过）</summary>
    Task<IReadOnlyList<NationalDrugEntryDto>> DownloadAndParseAsync(
        string? url = null, CancellationToken ct = default);

    /// <summary>从本地 CSV 文件解析药品清单，返回条目列表（表头/坏行自动跳过）</summary>
    Task<IReadOnlyList<NationalDrugEntryDto>> ParseFromFileAsync(
        string filePath, CancellationToken ct = default);
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

/// <summary>
/// 医生档案服务接口。处理医生注册、档案查询、更新。
/// </summary>
public interface IDoctorProfileService
{
    /// <summary>注册新医生（创建用户+档案）</summary>
    Task<long> RegisterDoctorAsync(
        string username, string password, string fullName,
        string gender, string? phone, string? email,
        string? medicalLicenseNo, string? practiceLicenseNo,
        string? specialty, string? title, string? department,
        CancellationToken ct = default);

    /// <summary>根据用户ID获取医生档案</summary>
    Task<DoctorProfileDto?> GetByUserIdAsync(long userId, CancellationToken ct = default);

    /// <summary>更新医生档案</summary>
    Task<bool> UpdateProfileAsync(long userId, DoctorProfileUpdateDto update, CancellationToken ct = default);

    /// <summary>获取所有医生列表</summary>
    Task<IReadOnlyList<DoctorProfileDto>> GetAllDoctorsAsync(CancellationToken ct = default);
}

// ── DTO 定义 ──

public record PatientDto(
    long Id, string Name, string Gender, DateOnly? Dob,
    string Phone, string? Allergies, string? History, string? ChronicTags,
    decimal? Weight, decimal? Temperature, int? SystolicBP, int? DiastolicBP, int? HeartRate,
    /// <summary>自定义标签（逗号分隔）</summary>
    string? Tags = null);

public record ExpiryAlertDto(
    long DrugId, string DrugName, string BatchNo,
    DateOnly ExpiryDate, int DaysRemaining, decimal QtyRemaining);

public record DailyReportDto(
    DateTime Date, int PrescriptionCount, decimal TotalAmount,
    decimal CashAmount, decimal PosAmount);

/// <summary>国内药品清单条目（医保目录 CSV 一行：药品名称 / 类别剂型 / 来源地区）</summary>
public record NationalDrugEntryDto(string MedName, string MedKind, string MedPlc);

/// <summary>国内药品清单导入结果统计</summary>
public record DrugImportResultDto(
    int Total, int Imported, int SkippedExisting, int SkippedInvalid,
    int AntibioticCount, int RestrictedCount,
    IReadOnlyList<string> SampleErrors);

/// <summary>医生档案DTO</summary>
public record DoctorProfileDto(
    long Id, long UserId, string FullName, string Gender,
    string? BirthDate, string? IdCard, string? Phone, string? Email, string? Address,
    string? MedicalLicenseNo, string? MedicalLicenseIssueDate, string? MedicalLicenseExpiryDate,
    string? PracticeLicenseNo, string? PracticeLicenseIssueDate, string? PracticeLicenseExpiryDate,
    string? Specialty, string? Title, string? Department, string? Hospital,
    string? AvatarPath, string? IdCardFrontPath, string? IdCardBackPath,
    string? MedicalLicensePhotoPath, string? PracticeLicensePhotoPath,
    int Status, string? Remark, string CreatedAt, string? UpdatedAt);

/// <summary>医生档案更新DTO</summary>
public record DoctorProfileUpdateDto(
    string FullName, string Gender, string? BirthDate, string? IdCard,
    string? Phone, string? Email, string? Address,
    string? MedicalLicenseNo, string? MedicalLicenseIssueDate, string? MedicalLicenseExpiryDate,
    string? PracticeLicenseNo, string? PracticeLicenseIssueDate, string? PracticeLicenseExpiryDate,
    string? Specialty, string? Title, string? Department, string? Hospital,
    string? AvatarPath, string? IdCardFrontPath, string? IdCardBackPath,
    string? MedicalLicensePhotoPath, string? PracticeLicensePhotoPath,
    string? Remark);
