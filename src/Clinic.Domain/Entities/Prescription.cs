using Clinic.Shared.Enums;

namespace Clinic.Domain.Entities;

public class Prescription : Common.Entity
{
    public string NoYearSeq { get; set; } = string.Empty;
    public long PatientId { get; set; }
    public long DoctorId { get; set; }
    public long? VisitId { get; set; }
    /// <summary>关联的病历ID（同一次就诊的病历与处方对应）</summary>
    public long? MedicalRecordId { get; set; }
    public string? DiagnosisCode { get; set; }
    public string? ChiefComplaint { get; set; }

    /// <summary>本次就诊体重（kg），每次就诊实时测量</summary>
    public decimal? Weight { get; set; }
    /// <summary>本次就诊体温（°C），每次就诊实时测量</summary>
    public decimal? Temperature { get; set; }
    /// <summary>本次就诊收缩压（mmHg）</summary>
    public int? SystolicBP { get; set; }
    /// <summary>本次就诊舒张压（mmHg）</summary>
    public int? DiastolicBP { get; set; }
    /// <summary>本次就诊心率（次/分）</summary>
    public int? HeartRate { get; set; }

    public string DiagnosisText { get; set; } = string.Empty;
    /// <summary>诊疗费（元），默认0，不含在药品明细中</summary>
    public decimal ConsultationFee { get; set; }
    public decimal TotalAmount { get; set; }
    public PrescriptionType Type { get; set; }
    public string? ExtendedReason { get; set; }
    public string? PdfPath { get; set; }
    public PrescriptionStatus Status { get; set; }
    public string? VoidReason { get; set; }
    public bool IsPaperSigned { get; set; }

    /// <summary>临床覆盖理由：存在阻断项（过敏/交互/禁忌症）时医生坚持开具的理由，入审计日志</summary>
    public string? OverrideReason { get; set; }

    /// <summary>发药时间（药师按处方配药发药完成时记录）</summary>
    public DateTime? DispensedAt { get; set; }

    /// <summary>发药操作人 ID（药师/护士/医生）</summary>
    public long? DispensedBy { get; set; }
}
