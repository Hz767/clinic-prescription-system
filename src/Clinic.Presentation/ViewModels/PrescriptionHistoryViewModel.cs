using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Clinic.Presentation.Helpers;
using Clinic.Presentation.Services;
using Clinic.Shared.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation.ViewModels;

/// <summary>
/// 处方历史查询 ViewModel。
/// 功能：按关键词/日期查询处方历史 + 重新生成并打印处方 PDF + 作废处方。
/// </summary>
public partial class PrescriptionHistoryViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDialogService _dialogService;

    /// <summary>导航到收费页面的事件（由 MainViewModel 订阅）</summary>
    public event Action<long>? NavigateToBillingRequested;

    [ObservableProperty]
    private ObservableCollection<PrescriptionHistoryDto> _prescriptions = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReprintPdfCommand))]
    [NotifyCanExecuteChangedFor(nameof(VoidPrescriptionCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToBillingCommand))]
    private PrescriptionHistoryDto? _selectedPrescription;

    [ObservableProperty]
    private string? _searchKeyword;

    [ObservableProperty]
    private DateTime? _fromDate;

    [ObservableProperty]
    private DateTime? _toDate;

    /// <summary>作废原因输入</summary>
    [ObservableProperty]
    private string _voidReason = string.Empty;
/// <summary>是否正在执行异步操作（防重复提交）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReprintPdfCommand))]
    [NotifyCanExecuteChangedFor(nameof(VoidPrescriptionCommand))]
    private bool _isBusy;

    public PrescriptionHistoryViewModel(IServiceScopeFactory scopeFactory, IDialogService dialogService)
    {
        _scopeFactory = scopeFactory;
        _dialogService = dialogService;
    }

    /// <summary>当前选中的处方是否可作废（仅已收费状态可作废）</summary>
    public bool CanVoidSelected =>
        SelectedPrescription is not null
        && SelectedPrescription.Status == (int)PrescriptionStatus.Paid;

    /// <summary>当前选中的处方是否可跳转收费（已保存或已审核状态可收费）</summary>
    public bool CanGoToBillingSelected =>
        SelectedPrescription is not null
        && (SelectedPrescription.Status == (int)PrescriptionStatus.Saved
            || SelectedPrescription.Status == (int)PrescriptionStatus.Reviewed);

    /// <summary>选中处方变化时刷新 CanVoidSelected 和 CanGoToBillingSelected</summary>
    partial void OnSelectedPrescriptionChanged(PrescriptionHistoryDto? value)
    {
        OnPropertyChanged(nameof(CanVoidSelected));
        OnPropertyChanged(nameof(CanGoToBillingSelected));
    }

    /// <summary>按关键词/日期搜索处方历史</summary>
    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var prescriptionService = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();
            var records = await prescriptionService.GetPrescriptionHistoryAsync(SearchKeyword, FromDate, ToDate);
            Prescriptions.Clear();
            foreach (var r in records)
                Prescriptions.Add(r);
            StatusMessage = $"查询到 {records.Count} 条处方记录";
        }
        catch (Exception ex)
        {
            ErrorMessage = ExceptionFormatter.GetMessage(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSearch() => !IsBusy;

    private bool CanReprintPdf() => !IsBusy && SelectedPrescription != null;

    /// <summary>重新生成 PDF 并用默认阅读器打开</summary>
    [RelayCommand(CanExecute = nameof(CanReprintPdf))]
    private async Task ReprintPdfAsync()
    {
        if (SelectedPrescription is null) return;
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var prescriptionService = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            var outputDir = config["Pdf:OutputDir"] ?? "Prescriptions";
            var fullDir = Path.Combine(AppContext.BaseDirectory, outputDir);
            Directory.CreateDirectory(fullDir);

            var pdfPath = await prescriptionService.GeneratePrescriptionPdfAsync(
                SelectedPrescription.Id, fullDir);

            if (pdfPath is not null && File.Exists(pdfPath))
            {
                StatusMessage = $"PDF 已生成：{Path.GetFileName(pdfPath)}";
                // 用默认 PDF 阅读器打开
                Process.Start(new ProcessStartInfo(pdfPath) { UseShellExecute = true });
            }
            else
            {
                ErrorMessage = "PDF 生成失败，处方可能不存在";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ExceptionFormatter.GetMessage(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>作废选中的处方（仅已收费状态可作废）</summary>
    [RelayCommand(CanExecute = nameof(CanVoidPrescription))]
    private async Task VoidPrescriptionAsync()
    {
        if (SelectedPrescription is null) return;

        // 弹出确认对话框
        var confirm = _dialogService.ShowConfirm(
            $"确定要作废处方 {SelectedPrescription.NoYearSeq} 吗？\n" +
            $"患者：{SelectedPrescription.PatientName}\n" +
            $"金额：¥{SelectedPrescription.TotalAmount:F2}\n\n" +
            "作废后库存将回退，已收费将生成退款冲正记录，此操作不可撤销。",
            "确认作废处方");

        if (!confirm)
            return;

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var prescriptionService = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();

            var reason = string.IsNullOrWhiteSpace(VoidReason)
                ? "医生手动作废"
                : VoidReason.Trim();

            var success = await prescriptionService.VoidPrescriptionAsync(
                SelectedPrescription.Id, reason);

            if (success)
            {
                StatusMessage = $"处方 {SelectedPrescription.NoYearSeq} 已作废";
                VoidReason = string.Empty;
                // 刷新列表
                await SearchAsync();
            }
            else
            {
                ErrorMessage = "作废失败：处方不存在或状态不可作废";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"作废失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanVoidPrescription() => !IsBusy && CanVoidSelected;

    /// <summary>跳转到收费页面（携带选中的处方ID，自动填充收费表单）</summary>
    [RelayCommand(CanExecute = nameof(CanGoToBilling))]
    private void GoToBilling()
    {
        if (SelectedPrescription is null) return;
        NavigateToBillingRequested?.Invoke(SelectedPrescription.Id);
    }

    private bool CanGoToBilling() => !IsBusy && CanGoToBillingSelected;
}
