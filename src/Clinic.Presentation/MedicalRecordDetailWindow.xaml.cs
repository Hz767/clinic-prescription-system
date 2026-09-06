using System.Windows;
using Clinic.Infrastructure.Data;
using Clinic.Presentation.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation;

public partial class MedicalRecordDetailWindow : Window
{
    private readonly IDialogService _dialogService;

    public long RecordId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientGender { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string VisitAt { get; set; } = string.Empty;
    public string ChiefComplaint { get; set; } = string.Empty;
    public string? PresentIllness { get; set; }
    public string? Exam { get; set; }
    public string? AuxiliaryExam { get; set; }
    public string Diagnosis { get; set; } = string.Empty;
    public string? Plan { get; set; }

    public MedicalRecordDetailWindow(long recordId)
    {
        InitializeComponent();
        _dialogService = App.Services.GetRequiredService<IDialogService>();
        RecordId = recordId;
        LoadData(recordId);
        DataContext = this;
    }

    private void LoadData(long recordId)
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();

        var record = db.MedicalRecords.FirstOrDefault(m => m.Id == recordId);
        if (record is null)
        {
            _dialogService.ShowWarning("病历不存在");
            Close();
            return;
        }

        var patient = db.Patients.FirstOrDefault(p => p.Id == record.PatientId);
        var doctor = db.SysUsers.FirstOrDefault(u => u.Id == record.DoctorId);

        PatientName = patient?.Name ?? "—";
        PatientGender = patient?.Gender ?? "—";
        DoctorName = doctor?.DisplayName ?? "—";
        VisitAt = record.VisitAt.ToString("yyyy-MM-dd HH:mm");
        ChiefComplaint = record.ChiefComplaint;
        PresentIllness = record.PresentIllness;
        Exam = record.Exam;
        AuxiliaryExam = record.AuxiliaryExam;
        Diagnosis = record.Diagnosis;
        Plan = record.Plan;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
