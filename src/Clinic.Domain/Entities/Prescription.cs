using Clinic.Shared.Enums;

namespace Clinic.Domain.Entities;

public class Prescription : Common.Entity
{
    public string NoYearSeq { get; set; } = string.Empty;
    public long PatientId { get; set; }
    public long DoctorId { get; set; }
    public long? VisitId { get; set; }
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
    public decimal TotalAmount { get; set; }
    public PrescriptionType Type { get; set; }
    public string? ExtendedReason { get; set; }
    public string? PdfPath { get; set; }
    public PrescriptionStatus Status { get; set; }
    public string? VoidReason { get; set; }
    public bool IsPaperSigned { get; set; }
}
