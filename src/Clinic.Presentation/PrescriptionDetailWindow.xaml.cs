using System.Collections.ObjectModel;
using System.Windows;
using Clinic.Application.Interfaces;
using Clinic.Presentation.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation;

public partial class PrescriptionDetailWindow : Window
{
    private readonly IDialogService _dialogService;

    public string PrescriptionNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string DiagnosisText { get; set; } = string.Empty;
    public string? ChiefComplaint { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string PrescriptionTypeText { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int ItemCount { get; set; }
    public ObservableCollection<PrescriptionItemDisplay> Items { get; set; } = new();

    public PrescriptionDetailWindow(long prescriptionId)
    {
        InitializeComponent();
        _dialogService = App.Services.GetRequiredService<IDialogService>();
        _ = LoadDataAsync(prescriptionId);
        DataContext = this;
    }

    private async System.Threading.Tasks.Task LoadDataAsync(long prescriptionId)
    {
        using var scope = App.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();

        var rx = await service.GetPrescriptionByIdAsync(prescriptionId);
        if (rx is null)
        {
            _dialogService.ShowWarning("处方不存在");
            Close();
            return;
        }

        PrescriptionNo = rx.NoYearSeq;
        PatientName = rx.PatientName;
        DoctorName = rx.DoctorName;
        DiagnosisText = rx.DiagnosisText;
        ChiefComplaint = rx.ChiefComplaint;
        CreatedAt = rx.CreatedAt.ToString("yyyy-MM-dd HH:mm");
        TotalAmount = rx.TotalAmount;

        PrescriptionTypeText = rx.Type switch
        {
            0 => "普通处方",
            1 => "急诊处方",
            2 => "儿科处方",
            _ => "未知"
        };

        StatusText = rx.Status switch
        {
            0 => "草稿",
            1 => "已保存(待审核)",
            2 => "已收费(待发药)",
            3 => "已作废",
            4 => "已审核(待收费)",
            5 => "已发药",
            _ => "未知"
        };

        ItemCount = rx.Items.Count;
        foreach (var item in rx.Items.OrderBy(i => i.Id))
        {
            Items.Add(new PrescriptionItemDisplay
            {
                DrugName = item.DrugName,
                Spec = item.Spec,
                DoseText = $"{item.Dose:0.##}{item.DoseUnit}",
                Frequency = item.Frequency,
                Route = item.Route,
                DurationDays = item.DurationDays,
                QtyText = $"{item.Qty:0.##}",
                UnitPrice = item.UnitPrice,
                Subtotal = item.Subtotal
            });
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

public class PrescriptionItemDisplay
{
    public string DrugName { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public string DoseText { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public string QtyText { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
}
