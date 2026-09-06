using System.Collections.ObjectModel;
using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Clinic.Presentation.Helpers;
using Clinic.Presentation.Services;
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
public partial class BillingViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IUserSession _session;
    private readonly IDialogService _dialogService;

    // ── 待办工作台 ──

    /// <summary>待审核处方列表（状态=已保存）</summary>
    public ObservableCollection<PrescriptionHistoryDto> PendingReviewList { get; } = new();

    /// <summary>待收费处方列表（状态=已审核）</summary>
    public ObservableCollection<PrescriptionHistoryDto> PendingBillingList { get; } = new();

    /// <summary>待发药处方列表（状态=已收费）</summary>
    public ObservableCollection<PrescriptionHistoryDto> PendingDispenseList { get; } = new();

    [ObservableProperty] private int _pendingReviewCount;
    [ObservableProperty] private int _pendingBillingCount;
    [ObservableProperty] private int _pendingDispenseCount;

    /// <summary>当前选中的待办处方（用于跳转到对应处理区域）</summary>
    [ObservableProperty] private PrescriptionHistoryDto? _selectedPendingPrescription;

    /// <summary>当前激活的Tab索引（0=待办工作台, 1=业务办理, 2=日结报表, 3=收费流水）</summary>
    [ObservableProperty] private int _activeTabIndex;

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
/// <summary>是否正在执行异步操作（防重复提交）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecordPaymentCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadDailyReportCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadPaymentHistoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefundCommand))]
    [NotifyCanExecuteChangedFor(nameof(AiPreReviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReviewPrescriptionCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadDispensePrescriptionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DispenseCommand))]
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

    // ── 发药（药房配药发药，发药时扣减库存） ──

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadDispensePrescriptionCommand))]
    private string _dispensePrescriptionIdInput = string.Empty;

    [ObservableProperty] private string? _dispensePrescriptionNo;
    [ObservableProperty] private string? _dispensePatientName;
    [ObservableProperty] private string? _dispenseAmount;
    [ObservableProperty] private string? _dispenseStatusText;
    [ObservableProperty] private bool _hasDispensePrescription;

    /// <summary>当前用户是否有发药权限（Doctor / Nurse / Pharmacist）</summary>
    public bool CanDispense =>
        _session.Role is UserRole.Doctor or UserRole.Nurse or UserRole.Pharmacist;

    // ── 处方搜索下拉（收费/审核/发药共用） ──

    /// <summary>处方搜索结果列表</summary>
    public ObservableCollection<PrescriptionHistoryDto> PrescriptionSearchResults { get; } = new();

    /// <summary>搜索下拉是否打开</summary>
    [ObservableProperty]
    private bool _isPrescriptionSearchOpen;

    /// <summary>当前激活的搜索字段：0=收费, 1=审核, 2=发药</summary>
    [ObservableProperty]
    private int _activePrescriptionSearchField;

    /// <summary>加载待办工作台数据（待审核/待收费/待发药）</summary>
    [RelayCommand]
    private async Task LoadPendingWorkbenchAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var rxSvc = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();

            // 取最近30天的处方，按状态分类
            var all = (await rxSvc.GetPrescriptionHistoryAsync(
                searchKeyword: null,
                fromDate: DateTime.Today.AddDays(-30),
                toDate: DateTime.Today.AddDays(1))).ToList();

            PendingReviewList.Clear();
            PendingBillingList.Clear();
            PendingDispenseList.Clear();

            foreach (var p in all)
            {
                switch (p.Status)
                {
                    case 1: PendingReviewList.Add(p); break;  // 已保存→待审核
                    case 4: PendingBillingList.Add(p); break; // 已审核→待收费
                    case 2: PendingDispenseList.Add(p); break; // 已收费→待发药
                }
            }

            PendingReviewCount = PendingReviewList.Count;
            PendingBillingCount = PendingBillingList.Count;
            PendingDispenseCount = PendingDispenseList.Count;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"加载待办失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>点击待办项：跳转到业务办理Tab并填充对应处方ID</summary>
    [RelayCommand]
    private void SelectPendingPrescription(PrescriptionHistoryDto? prescription)
    {
        if (prescription is null) return;
        SelectedPendingPrescription = prescription;
        ActiveTabIndex = 1; // 跳转到业务办理Tab
        var idStr = prescription.Id.ToString();

        // 根据状态跳转到对应处理区域
        switch (prescription.Status)
        {
            case 1: // 待审核→审核区域
                ReviewPrescriptionIdInput = idStr;
                break;
            case 4: // 待收费→收费区域
                PrescriptionIdInput = idStr;
                AmountInput = prescription.TotalAmount.ToString("F2");
                break;
            case 2: // 待发药→发药区域
                DispensePrescriptionIdInput = idStr;
                _ = LoadDispensePrescriptionAsync();
                break;
        }
    }

    /// <summary>搜索处方（按处方编号/患者姓名/诊断关键词）</summary>
    [RelayCommand]
    private async Task SearchPrescriptionsAsync()
    {
        ErrorMessage = null;
        var keyword = ActivePrescriptionSearchField switch
        {
            0 => PrescriptionIdInput?.Trim(),
            1 => ReviewPrescriptionIdInput?.Trim(),
            2 => DispensePrescriptionIdInput?.Trim(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(keyword))
        {
            IsPrescriptionSearchOpen = false;
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var rxSvc = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();
            var results = await rxSvc.GetPrescriptionHistoryAsync(
                searchKeyword: keyword,
                fromDate: null,
                toDate: null);

            PrescriptionSearchResults.Clear();
            foreach (var r in results.Take(10))
                PrescriptionSearchResults.Add(r);

            IsPrescriptionSearchOpen = PrescriptionSearchResults.Count > 0;
            if (PrescriptionSearchResults.Count == 0)
                StatusMessage = $"未找到匹配「{keyword}」的处方";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"搜索处方失败：{ExceptionFormatter.GetMessage(ex)}";
        }
    }

    /// <summary>从搜索结果中选择处方，填入对应输入框</summary>
    [RelayCommand]
    private void SelectPrescriptionFromSearch(PrescriptionHistoryDto? prescription)
    {
        if (prescription is null) return;
        var idStr = prescription.Id.ToString();

        switch (ActivePrescriptionSearchField)
        {
            case 0:
                PrescriptionIdInput = idStr;
                AmountInput = prescription.TotalAmount.ToString("F2");
                break;
            case 1:
                ReviewPrescriptionIdInput = idStr;
                break;
            case 2:
                DispensePrescriptionIdInput = idStr;
                break;
        }

        IsPrescriptionSearchOpen = false;
        StatusMessage = $"已选择处方 {prescription.NoYearSeq}（{prescription.PatientName}）";
    }

    /// <summary>关闭搜索下拉</summary>
    [RelayCommand]
    private void ClosePrescriptionSearch()
    {
        IsPrescriptionSearchOpen = false;
    }

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

    public BillingViewModel(IServiceScopeFactory scopeFactory, IUserSession session, IDialogService dialogService)
    {
        _scopeFactory = scopeFactory;
        _session = session;
        _dialogService = dialogService;
        _canReview = _session.Role == UserRole.Doctor || _session.Role == UserRole.Nurse;
    }

    /// <summary>从处方页面跳转过来时预填处方ID到审核区、收费区和发药区，并显示患者信息</summary>
    public async Task SetPendingPrescription(long prescriptionId)
    {
        var idStr = prescriptionId.ToString();
        ReviewPrescriptionIdInput = idStr;
        PrescriptionIdInput = idStr;
        DispensePrescriptionIdInput = idStr;

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
        0 => "草稿", 1 => "已保存", 2 => "已收费", 3 => "已作废", 4 => "已审核", 5 => "已发药", _ => "未知"
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
            StatusMessage = $"收费成功：{methodText} ¥{amount:F2}（流水号 {paymentId}）。已自动载入发药区，请完成配药发药";

            // 收费成功后自动填充发药区（发药时扣减库存）
            DispensePrescriptionIdInput = prescriptionId.ToString();
            HasDispensePrescription = false;
            await LoadDispensePrescriptionCoreAsync(prescriptionId);

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
        var confirm = _dialogService.ShowConfirm(
            $"确定要退费吗？\n" +
            $"处方编号：{target.PrescriptionNo}\n" +
            $"收费方式：{target.MethodText}\n" +
            $"金额：¥{target.Amount:F2}\n\n" +
            "退费后处方将作废，库存将回退，此操作不可撤销。",
            "确认退费");

        if (!confirm)
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
    // ── 发药工作台：加载待发药处方信息 ──

    /// <summary>加载发药处方信息（仅「已收费」状态可发药）</summary>
    [RelayCommand(CanExecute = nameof(CanLoadDispensePrescription))]
    private async Task LoadDispensePrescriptionAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            if (!long.TryParse(DispensePrescriptionIdInput.Trim(), out var prescriptionId) || prescriptionId <= 0)
            {
                ErrorMessage = "请输入有效的处方 ID";
                return;
            }

            await LoadDispensePrescriptionCoreAsync(prescriptionId);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"加载发药信息失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLoadDispensePrescription()
        => CanDispense && !IsBusy && !string.IsNullOrWhiteSpace(DispensePrescriptionIdInput);

    /// <summary>核心：查询处方并填充发药区信息</summary>
    private async Task LoadDispensePrescriptionCoreAsync(long prescriptionId)
    {
        using var scope = _scopeFactory.CreateScope();
        var rxSvc = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();
        var rx = await rxSvc.GetPrescriptionByIdAsync(prescriptionId);

        if (rx is null)
        {
            ErrorMessage = "处方不存在";
            HasDispensePrescription = false;
            return;
        }

        DispensePrescriptionNo = rx.NoYearSeq;
        DispensePatientName = rx.PatientName;
        DispenseAmount = $"¥{rx.TotalAmount:F2}";
        DispenseStatusText = GetStatusText(rx.Status);
        HasDispensePrescription = true;

        if (rx.Status != 2)
        {
            ErrorMessage = $"处方状态为「{GetStatusText(rx.Status)}」，仅「已收费」状态的处方可发药";
        }
    }

    // ── 发药工作台：确认发药（发药时按 FIFO 扣减库存） ──

    /// <summary>确认发药：仅「已收费」处方可发药，发药后状态变为「已发药」并扣减库存</summary>
    [RelayCommand(CanExecute = nameof(CanDispensePrescription))]
    private async Task DispenseAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            if (!long.TryParse(DispensePrescriptionIdInput.Trim(), out var prescriptionId) || prescriptionId <= 0)
            {
                ErrorMessage = "请输入有效的处方 ID";
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var rxSvc = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();

            var success = await rxSvc.DispensePrescriptionAsync(prescriptionId);
            if (success)
            {
                StatusMessage = $"发药成功：处方 {DispensePrescriptionNo ?? prescriptionId.ToString()} 已完成配药发药，库存已扣减";
                DispensePrescriptionIdInput = string.Empty;
                HasDispensePrescription = false;
                DispensePrescriptionNo = null;
                DispensePatientName = null;
                DispenseAmount = null;
                DispenseStatusText = null;
            }
            else
            {
                ErrorMessage = "发药失败，处方不存在";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"发药失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanDispensePrescription()
        => CanDispense && !IsBusy && !string.IsNullOrWhiteSpace(DispensePrescriptionIdInput);

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
        var confirm = _dialogService.ShowConfirm(
            $"确定要审核通过该处方吗？\n" +
            $"处方 ID：{prescriptionId}\n\n" +
            "审核通过后处方方可收费。",
            "确认药师审核");

        if (!confirm)
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
