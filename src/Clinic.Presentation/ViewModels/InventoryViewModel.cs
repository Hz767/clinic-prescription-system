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
/// 库存管理 ViewModel。功能：库存概览、入库操作、效期预警。
/// 权限：Doctor/Nurse 可入库（CanModify），所有角色可查看。
/// </summary>
public partial class InventoryViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IUserSession _session;

    // ── 库存概览 ──

    public ObservableCollection<DrugStockSummaryDto> StockSummaryList { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ViewBatchesCommand))]
    private DrugStockSummaryDto? _selectedStockItem;

    public ObservableCollection<StockBatchDto> StockBatches { get; } = new();

    // ── 入库操作 ──

    public ObservableCollection<DrugDto> Drugs { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StockInCommand))]
    private DrugDto? _selectedDrug;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StockInCommand))]
    private string? _batchNo;

    [ObservableProperty]
    private DateTime? _expiryDate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StockInCommand))]
    private string? _qty;

    [ObservableProperty]
    private string? _costPrice;

    [ObservableProperty]
    private string? _supplier;

    // ── 效期预警 ──

    public ObservableCollection<ExpiryAlertDto> ExpiryAlerts { get; } = new();

    // ── 手动出库 ──

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StockOutCommand))]
    private DrugDto? _stockOutSelectedDrug;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StockOutCommand))]
    private string? _stockOutQty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StockOutCommand))]
    private string? _stockOutReason;

    // ── 入库历史 ──

    public ObservableCollection<StockInRecordDto> StockInHistory { get; } = new();

    [ObservableProperty]
    private DateTime? _historyFromDate;

    [ObservableProperty]
    private DateTime? _historyToDate;

    // ── 通用状态 ──

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>是否正在执行异步操作（防重复提交）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StockInCommand))]
    [NotifyCanExecuteChangedFor(nameof(StockOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadDataCommand))]
    private bool _isBusy;

    /// <summary>当前功能页：0=库存概览, 1=入库操作, 2=效期预警</summary>
    [ObservableProperty]
    private int _currentTab;

    /// <summary>当前用户是否有入库权限</summary>
    public bool CanModify => _session.Role == UserRole.Doctor || _session.Role == UserRole.Nurse;

    public InventoryViewModel(IServiceScopeFactory scopeFactory, IUserSession session)
    {
        _scopeFactory = scopeFactory;
        _session = session;

        // 默认日期：历史查询范围为最近 30 天
        HistoryFromDate = DateTime.Today.AddDays(-30);
        HistoryToDate = DateTime.Today;
    }

    /// <summary>加载药品目录 + 库存汇总 + 效期预警 + 入库历史</summary>
    [RelayCommand(CanExecute = nameof(CanLoadData))]
    private async Task LoadDataAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            var drugs = await inventoryService.GetAllDrugsAsync();
            var summary = await inventoryService.GetAllStockSummaryAsync();
            var alerts = await inventoryService.GetExpiryAlertsAsync();
            var history = await inventoryService.GetStockInHistoryAsync(HistoryFromDate, HistoryToDate);

            Drugs.Clear();
            foreach (var d in drugs)
                Drugs.Add(d);

            StockSummaryList.Clear();
            foreach (var s in summary)
                StockSummaryList.Add(s);

            ExpiryAlerts.Clear();
            foreach (var a in alerts)
                ExpiryAlerts.Add(a);

            StockInHistory.Clear();
            foreach (var h in history)
                StockInHistory.Add(h);

            StatusMessage = $"已加载 {StockSummaryList.Count} 种药品库存";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"加载数据失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLoadData() => !IsBusy;

    /// <summary>刷新库存汇总</summary>
    [RelayCommand]
    private async Task RefreshStockSummaryAsync()
    {
        ErrorMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            var summary = await inventoryService.GetAllStockSummaryAsync();

            StockSummaryList.Clear();
            foreach (var s in summary)
                StockSummaryList.Add(s);

            StatusMessage = $"已刷新库存汇总（{StockSummaryList.Count} 条）";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"刷新库存汇总失败：{ExceptionFormatter.GetMessage(ex)}";
        }
    }

    /// <summary>查看选中药品的批次明细</summary>
    [RelayCommand(CanExecute = nameof(CanViewBatches))]
    private async Task ViewBatchesAsync()
    {
        if (SelectedStockItem is null)
            return;

        ErrorMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            var batches = await inventoryService.GetStockBatchesAsync(SelectedStockItem.DrugId);

            StockBatches.Clear();
            foreach (var b in batches)
                StockBatches.Add(b);

            StatusMessage = $"已加载 {SelectedStockItem.DrugName} 的 {StockBatches.Count} 个批次";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"加载批次明细失败：{ExceptionFormatter.GetMessage(ex)}";
        }
    }

    private bool CanViewBatches() => SelectedStockItem is not null;

    /// <summary>执行入库</summary>
    [RelayCommand(CanExecute = nameof(CanStockIn))]
    private async Task StockInAsync()
    {
        IsBusy = true;
        try
        {
            if (SelectedDrug is null)
            {
                ErrorMessage = "请选择药品";
                return;
            }

            if (ExpiryDate is null)
            {
                ErrorMessage = "请选择效期日期";
                return;
            }

            if (!decimal.TryParse(Qty, out var qty) || qty <= 0)
            {
                ErrorMessage = "请输入有效的数量";
                return;
            }

            var costPrice = 0m;
            if (!string.IsNullOrWhiteSpace(CostPrice) && !decimal.TryParse(CostPrice, out costPrice))
            {
                ErrorMessage = "请输入有效的成本价";
                return;
            }

            ErrorMessage = null;
            StatusMessage = null;

            using var scope = _scopeFactory.CreateScope();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            var operatorId = _session.UserId ?? 0;
            var expiryDateOnly = DateOnly.FromDateTime(ExpiryDate.Value);
            var drugName = SelectedDrug.GenericNameCn;

            var recordId = await inventoryService.StockInAsync(
                SelectedDrug.Id,
                BatchNo!.Trim(),
                expiryDateOnly,
                qty,
                costPrice,
                Supplier?.Trim(),
                operatorId);

            // 清空表单
            SelectedDrug = null;
            BatchNo = null;
            ExpiryDate = null;
            Qty = null;
            CostPrice = null;
            Supplier = null;

            // 刷新库存汇总和入库历史
            var summary = await inventoryService.GetAllStockSummaryAsync();
            StockSummaryList.Clear();
            foreach (var s in summary)
                StockSummaryList.Add(s);

            var history = await inventoryService.GetStockInHistoryAsync(HistoryFromDate, HistoryToDate);
            StockInHistory.Clear();
            foreach (var h in history)
                StockInHistory.Add(h);

            StatusMessage = $"入库成功：{drugName} × {qty}（记录号 {recordId}）";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"入库失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanStockIn()
        => !IsBusy
           && CanModify
           && SelectedDrug is not null
           && !string.IsNullOrWhiteSpace(BatchNo)
           && decimal.TryParse(Qty, out _);

    /// <summary>刷新效期预警</summary>
    [RelayCommand]
    private async Task LoadExpiryAlertsAsync()
    {
        ErrorMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            var alerts = await inventoryService.GetExpiryAlertsAsync();

            ExpiryAlerts.Clear();
            foreach (var a in alerts)
                ExpiryAlerts.Add(a);

            StatusMessage = $"已加载 {ExpiryAlerts.Count} 条效期预警";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"加载效期预警失败：{ExceptionFormatter.GetMessage(ex)}";
        }
    }

    /// <summary>按日期范围查询入库历史</summary>
    [RelayCommand]
    private async Task LoadStockInHistoryAsync()
    {
        ErrorMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            var history = await inventoryService.GetStockInHistoryAsync(HistoryFromDate, HistoryToDate);

            StockInHistory.Clear();
            foreach (var h in history)
                StockInHistory.Add(h);

            StatusMessage = $"已加载 {StockInHistory.Count} 条入库记录";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"查询入库历史失败：{ExceptionFormatter.GetMessage(ex)}";
        }
    }

    /// <summary>手动出库（报损、调拨等非处方出库）</summary>
    [RelayCommand(CanExecute = nameof(CanStockOut))]
    private async Task StockOutAsync()
    {
        if (StockOutSelectedDrug is null)
        {
            ErrorMessage = "请选择药品";
            return;
        }

        if (!int.TryParse(StockOutQty, out var qty) || qty <= 0)
        {
            ErrorMessage = "请输入有效的出库数量（正整数）";
            return;
        }

        if (string.IsNullOrWhiteSpace(StockOutReason))
        {
            ErrorMessage = "请填写出库原因";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            var operatorId = _session.UserId ?? 0;
            var drugName = StockOutSelectedDrug.GenericNameCn;

            await inventoryService.StockOutAsync(
                StockOutSelectedDrug.Id,
                qty,
                StockOutReason.Trim(),
                operatorId);

            // 清空表单
            StockOutSelectedDrug = null;
            StockOutQty = null;
            StockOutReason = null;

            // 刷新库存汇总
            var summary = await inventoryService.GetAllStockSummaryAsync();
            StockSummaryList.Clear();
            foreach (var s in summary)
                StockSummaryList.Add(s);

            StatusMessage = $"出库成功：{drugName} × {qty}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"出库失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanStockOut()
        => !IsBusy
           && CanModify
           && StockOutSelectedDrug is not null
           && int.TryParse(StockOutQty, out _)
           && !string.IsNullOrWhiteSpace(StockOutReason);
}
