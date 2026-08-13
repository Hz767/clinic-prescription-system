namespace Clinic.Application.DTOs;

/// <summary>
/// 处方 PDF 渲染数据。包含处方页面上需要的所有信息。
/// </summary>
public record PrescriptionPdfData(
    string PrescriptionNo,
    DateTime CreatedAt,
    string PrescriptionTypeText,
    int PrescriptionType,
    string? ExtendedReason,
    // 患者信息
    string PatientName,
    string PatientGender,
    int? PatientAge,
    string PatientPhone,
    // 医生信息
    string DoctorName,
    string ClinicName,
    // 主诉 + 诊断
    string? ChiefComplaint,
    string DiagnosisText,
    // 药品明细
    IReadOnlyList<PrescriptionItemPdfRow> Items,
    decimal TotalAmount,
    // 药品明细行
    string? Notes);

/// <summary>处方药品明细行（PDF 渲染用）</summary>
public record PrescriptionItemPdfRow(
    int Seq,
    string DrugName,
    string Spec,
    string DoseText,
    string Frequency,
    string Route,
    int DurationDays,
    decimal Qty,
    string Unit,
    decimal UnitPrice,
    decimal Subtotal);
