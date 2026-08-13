namespace Clinic.Application.Validators;

// ── 请求 DTO：将服务方法参数封装为 record，供 FluentValidation 验证 ──

/// <summary>登录请求</summary>
public record LoginRequest(string Username, string Password);

/// <summary>创建患者请求</summary>
public record CreatePatientRequest(
    string Name, string Gender, DateOnly? Dob,
    string Phone, string? Allergies, string? History, string? ChronicTags);

/// <summary>创建处方请求（含本次就诊体征数据）</summary>
public record CreatePrescriptionRequest(
    long PatientId, long DoctorId, string? ChiefComplaint, string DiagnosisText,
    string? DiagnosisCode, int PrescriptionType, string? ExtendedReason,
    decimal? Weight = null, decimal? Temperature = null,
    int? SystolicBP = null, int? DiastolicBP = null, int? HeartRate = null);

/// <summary>添加处方明细请求</summary>
public record AddPrescriptionItemRequest(
    long PrescriptionId, long DrugId, decimal Dose, string DoseUnit,
    string Frequency, string Route, int DurationDays, decimal Qty);

/// <summary>入库请求</summary>
public record StockInRequest(
    long DrugId, string BatchNo, DateOnly ExpiryDate,
    decimal Qty, decimal CostPrice, string? Supplier, long OperatorId);

/// <summary>记录收费请求</summary>
public record RecordPaymentRequest(
    long PrescriptionId, int Method, decimal Amount,
    long OperatorId, string? PosSerialNo, string? Note);
