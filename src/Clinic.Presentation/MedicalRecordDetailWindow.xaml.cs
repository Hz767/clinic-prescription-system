using System.Windows;
using Clinic.Application.Interfaces;
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
        _ = LoadDataAsync(recordId);
        DataContext = this;
    }

    private async System.Threading.Tasks.Task LoadDataAsync(long recordId)
    {
        using var scope = App.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMedicalRecordService>();

        var record = await service.GetByIdAsync(recordId);
        if (record is null)
        {
            _dialogService.ShowWarning("病历不存在");
            Close();
            return;
        }

        PatientName = record.PatientName;
        PatientGender = record.PatientGender;
        DoctorName = record.DoctorName;
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
