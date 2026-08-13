namespace Clinic.Domain.Entities;

public class MedicalRecord : Common.Entity
{
    public long PatientId { get; set; }
    public long DoctorId { get; set; }
    public DateTime VisitAt { get; set; }
    public string ChiefComplaint { get; set; } = string.Empty;
    public string? PresentIllness { get; set; }
    public string? Exam { get; set; }
    public bool ExamNa { get; set; }
    public string? AuxiliaryExam { get; set; }
    public bool AuxiliaryNa { get; set; }
    public string Diagnosis { get; set; } = string.Empty;
    public string? Plan { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
