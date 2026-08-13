using System.Collections.ObjectModel;
using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Clinic.Presentation.Helpers;
using Clinic.Shared.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation.ViewModels;

/// <summary>
/// 收费管理 ViewModel。
/// 功能：记录收费（现金/POS）+ 日结报表查询。
/// 权限：仅 Doctor 可收费（CanBill），所有角色可查看日报。
/// </summary>
public partial class BillingViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IUserSession _session;

    // ── 收费表单 ──

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecordPaymentCommand))]
    private string _prescriptionIdInput = string.Empty;

    [ObservableProperty]
    private int _paymentMethod; // 0=现金, 1=POS

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecordPaymentCommand))]
    private string _amountInput = string.Empty;

    [ObservableProperty]
    private string? _posSerialNo;

    [ObservableProperty]
    private string? _note;

    // ── 日结报表 ──

    [ObservableProperty]
    private DateTime _reportDate = DateTime.Today;

    [ObservableProperty]
    private int _reportPrescriptionCount;

    [ObservableProperty]
    private decimal _reportTotalAmount;

    [ObservableProperty]
    private decimal _reportCashAmount;

    [ObservableProperty]
    private decimal _reportPosAmount;

    [ObservableProperty]
    private bool _hasReportData;

    // ── 状态 ──

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>是否正在执行异步操作（防重复提交）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecordPaymentCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadDailyReportCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadPaymentHistoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefundCommand))]
    [NotifyCanExecuteChangedFor(nameof(AiPreReviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReviewPrescriptionCommand))]
    private bool _isBusy;

    // ── 收费流水查询 ──

    [ObservableProperty]
    private ObservableCollection<PaymentRecordDto> _paymentHistory = new();

    [ObservableProperty]
    private DateTime? _historyFromDate;

    [ObservableProperty]
    private DateTime? _historyToDate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefundCommand))]
    private PaymentRecordDto? _selectedPaymentRecord;

    // ── 药师审核 ──

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReviewPrescriptionCommand))]
    private string _reviewPrescriptionIdInput = string.Empty;

    [ObservableProperty]
    private string? _reviewNote;

    [ObservableProperty]
    private string? _aiReviewResult;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReviewPrescriptionCommand))]
    [NotifyCanExecuteChangedFor(nameof(AiPreReviewCommand))]
    private bool _hasAiReviewResult;

    [ObservableProperty]
    private bool _canReview; // Doctor或Nurse可审核

    // ── 待收费处方信息（从处方页面跳转时自动加载） ──

    [ObservableProperty] private string? _pendingPrescriptionNo;
    [ObservableProperty] private string? _pendingPatientName;
    [ObservableProperty] private string? _pendingDiagnosis;
    [ObservableProperty] private string? _pendingAmount;
    [ObservableProperty] private string? _pendingStatusText;
    [ObservableProperty] private bool _hasPendingPrescription;

    /// <summary>是否为 POS 收费（控制 POS 序列号输入框显示）</summary>
    public bool IsPos => PaymentMethod == 1;

    /// <summary>当前用户是否有收费权限</summary>
    public bool CanBill => _session.Role == UserRole.Doctor;

    public BillingViewModel(IServiceScopeFactory scopeFactory, IUserSession session)
    {
        _scopeFactory = scopeFactory;
        _session = session;
        _canReview = _session.Role == UserRole.Doctor || _session.Role == UserRole.Nurse;
    }

    /// <summary>从处方页面跳转过来时预填处方ID到审核区和收费区，并显示患者信息</summary>
    public async Task SetPendingPrescription(long prescriptionId)
    {
        var idStr = prescriptionId.ToString();
        ReviewPrescriptionIdInput = idStr;
        PrescriptionIdInput = idStr;

        // 查询处方完整信息并填充
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var rxSvc = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();
            var rx = await rxSvc.GetPrescriptionByIdAsync(prescriptionId);
            if (rx is not null)
            {
                AmountInput = rx.TotalAmount.ToString("F2");
                PendingPrescriptionNo = rx.NoYearSeq;
                PendingPatientName = rx.PatientName;
                PendingDiagnosis = rx.DiagnosisText;
                PendingAmount = $"¥{rx.TotalAmount:F2}";
                PendingStatusText = GetStatusText(rx.Status);
                HasPendingPrescription = true;
                StatusMessage = $"已加载处方 {rx.NoYearSeq}，患者：{rx.PatientName}，金额 ¥{rx.TotalAmount:F2}，状态：{GetStatusText(rx.Status)}";
            }
        }
        catch { /* 查询失败不影响手动输入 */ }
    }

    private static string GetStatusText(int status) => status switch
    {
        0 => "草稿", 1 => "已保存", 2 => "已收费", 3 => "已作废", 4 => "已审核", _ => "未知"
    };

    /// <summary>支付方式变更时更新 IsPos 属性</summary>
    partial void OnPaymentMethodChanged(int value)
    {
        OnPropertyChanged(nameof(IsPos));
    }

    /// <summary>记录收费</summary>
    [RelayCommand(CanExecute = nameof(CanRecordPayment))]
    private async Task RecordPaymentAsync()
    {
        IsBusy = true;
        try
        {
            if (!long.TryParse(PrescriptionIdInput.Trim(), out var prescriptionId) || prescriptionId <= 0)
            {
                ErrorMessage = "请输入有效的处方 ID";
                return;
            }

            if (!decimal.TryParse(AmountInput.Trim(), out var amount) || amount <= 0)
            {
                ErrorMessage = "请输入有效的收费金额";
                return;
            }

            if (_session.UserId is null)
            {
                ErrorMessage = "未登录，请先登录";
                return;
            }

            ErrorMessage = null;
            StatusMessage = null;

            using var scope = _scopeFactory.CreateScope();
            var billingService = scope.ServiceProvider.GetRequiredService<IBillingService>();

            var paymentId = await billingService.RecordPaymentAsync(
                prescriptionId,
                PaymentMethod,
                amount,
                _session.UserId.Value,
                PaymentMethod == 1 ? PosSerialNo?.Trim() : null,
                Note?.Trim());

            var methodText = PaymentMethod == 0 ? "现金" : "POS";
            StatusMessage = $"收费成功：{methodText} ¥{amount:F2}（流水号 {paymentId}）";

            // 清空表单
            PrescriptionIdInput = string.Empty;
            AmountInput = string.Empty;
            PosSerialNo = null;
            Note = null;

            // 清空待收费处方信息卡片（收费完成后不再显示）
            HasPendingPrescription = false;
            PendingPrescriptionNo = null;
            PendingPatientName = null;
            PendingDiagnosis = null;
            PendingAmount = null;
            PendingStatusText = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"收费失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRecordPayment()
        => CanBill
           && !IsBusy
           && !string.IsNullOrWhiteSpace(PrescriptionIdInput)
           && !string.IsNullOrWhiteSpace(AmountInput);

    /// <summary>查询日结报表</summary>
    [RelayCommand(CanExecute = nameof(CanLoadDailyReport))]
    private async Task LoadDailyReportAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var billingService = scope.ServiceProvider.GetRequiredService<IBillingService>();

            var report = await billingService.GetDailyReportAsync(ReportDate);

            if (report is not null)
            {
                ReportPrescriptionCount = report.PrescriptionCount;
                ReportTotalAmount = report.TotalAmount;
                ReportCashAmount = report.CashAmount;
                ReportPosAmount = report.PosAmount;
                HasReportData = true;
                StatusMessage = $"已加载 {ReportDate:yyyy-MM-dd} 日结报表";
            }
            else
            {
                HasReportData = false;
                StatusMessage = $"{ReportDate:yyyy-MM-dd} 无收费记录";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"查询报表失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLoadDailyReport() => !IsBusy;

    /// <summary>查询收费流水历史</summary>
    [RelayCommand(CanExecute = nameof(CanLoadPaymentHistory))]
    private async Task LoadPaymentHistoryAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var billingService = scope.ServiceProvider.GetRequiredService<IBillingService>();
            var records = await billingService.GetPaymentHistoryAsync(HistoryFromDate, HistoryToDate);
            PaymentHistory.Clear();
            foreach (var r in records)
                PaymentHistory.Add(r);
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

    private bool CanLoadPaymentHistory() => !IsBusy;

    /// <summary>当前选中的收费记录是否可退费（非冲正记录 + 有收费权限）</summary>
    public bool CanRefundSelected =>
        SelectedPaymentRecord is not null
        && !SelectedPaymentRecord.IsReversal
        && CanBill;

    /// <summary>选中收费记录变化时刷新 CanRefundSelected</summary>
    partial void OnSelectedPaymentRecordChanged(PaymentRecordDto? value)
    {
        RefundCommand.NotifyCanExecuteChanged();
    }

    /// <summary>退费（冲正）：为选中的收费记录生成冲正记录</summary>
    [RelayCommand(CanExecute = nameof(CanRefund))]
    private async Task RefundAsync(PaymentRecordDto? record)
    {
        // 如果通过参数传入记录，则使用参数；否则使用选中的记录
        var target = record ?? SelectedPaymentRecord;
        if (target is null) return;

        // 弹出确认对话框
        var confirm = System.Windows.MessageBox.Show(
            $"确定要退费吗？\n" +
            $"处方编号：{target.PrescriptionNo}\n" +
            $"收费方式：{target.MethodText}\n" +
            $"金额：¥{target.Amount:F2}\n\n" +
            "退费后处方将作废，库存将回退，此操作不可撤销。",
            "确认退费",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes)
            return;

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            if (_session.UserId is null)
            {
                ErrorMessage = "未登录，请先登录";
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var billingService = scope.ServiceProvider.GetRequiredService<IBillingService>();

            await billingService.RefundAsync(
                target.PrescriptionId,
                _session.UserId.Value,
                Note?.Trim());

            StatusMessage = $"退费成功：处方 {target.PrescriptionNo} ¥{target.Amount:F2}";

            // 刷新收费流水
            var records = await billingService.GetPaymentHistoryAsync(HistoryFromDate, HistoryToDate);
            PaymentHistory.Clear();
            foreach (var r in records)
                PaymentHistory.Add(r);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"退费失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRefund() => !IsBusy && CanRefundSelected;

    // ── 药师审核 ──

    /// <summary>智能预审处方（规则引擎检查药物交互/过敏/剂量等，辅助药师审核）</summary>
    [RelayCommand(CanExecute = nameof(CanAiPreReview))]
    private async Task AiPreReviewAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            if (!long.TryParse(ReviewPrescriptionIdInput.Trim(), out var prescriptionId) || prescriptionId <= 0)
            {
                ErrorMessage = "请输入有效的处方 ID";
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var prescriptionService = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();

            var suggestions = await prescriptionService.GetAiReviewSuggestionsAsync(prescriptionId);

            AiReviewResult = suggestions;
            HasAiReviewResult = true;
            StatusMessage = "智能预审完成，请参考建议进行人工审核";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"智能预审失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAiPreReview()
        => !IsBusy && !string.IsNullOrWhiteSpace(ReviewPrescriptionIdInput);

    /// <summary>确认审核处方（药师审核，《处方管理办法》要求）</summary>
    [RelayCommand(CanExecute = nameof(CanReviewPrescription))]
    private async Task ReviewPrescriptionAsync()
    {
        if (!long.TryParse(ReviewPrescriptionIdInput.Trim(), out var prescriptionId) || prescriptionId <= 0)
        {
            ErrorMessage = "请输入有效的处方 ID";
            return;
        }

        // 弹出确认对话框
        var confirm = System.Windows.MessageBox.Show(
            $"确定要审核通过该处方吗？\n" +
            $"处方 ID：{prescriptionId}\n\n" +
            "审核通过后处方方可收费。",
            "确认药师审核",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (confirm != System.Windows.MessageBoxResult.Yes)
            return;

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var prescriptionService = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();

            var success = await prescriptionService.ReviewPrescriptionAsync(
                prescriptionId,
                ReviewNote?.Trim());

            if (success)
            {
                // 审核通过后自动填充收费区，医生无需重新输入处方ID
                PrescriptionIdInput = prescriptionId.ToString();
                try
                {
                    using var scope2 = _scopeFactory.CreateScope();
                    var rxSvc = scope2.ServiceProvider.GetRequiredService<IPrescriptionService>();
                    var rx = await rxSvc.GetPrescriptionByIdAsync(prescriptionId);
                    if (rx is not null)
                        AmountInput = rx.TotalAmount.ToString("F2");
                }
                catch { /* 查询金额失败不影响手动输入 */ }

                StatusMessage = $"处方 {prescriptionId} 审核通过，请在下方收费区确认收费";

                // 清空审核表单
                ReviewPrescriptionIdInput = string.Empty;
                ReviewNote = null;
                AiReviewResult = null;
                HasAiReviewResult = false;
            }
            else
            {
                ErrorMessage = "审核失败：处方不存在或当前状态不允许审核";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"审核失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanReviewPrescription()
        => CanReview && !IsBusy && !string.IsNullOrWhiteSpace(ReviewPrescriptionIdInput);
}
