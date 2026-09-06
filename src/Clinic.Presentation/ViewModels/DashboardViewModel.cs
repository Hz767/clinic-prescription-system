using System.Collections.ObjectModel;
using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Clinic.Presentation.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation.ViewModels;

/// <summary>
/// 首页 Dashboard ViewModel。
/// 展示 KPI 概览、待办队列、最近活动和快捷操作。
/// </summary>
public partial class DashboardViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;

    // ── KPI 卡片 ──

    [ObservableProperty] private int _todayPrescriptionCount;
    [ObservableProperty] private decimal _todayRevenue;
    [ObservableProperty] private int _pendingReviewCount;
    [ObservableProperty] private int _pendingBillingCount;
    [ObservableProperty] private int _pendingDispenseCount;
    [ObservableProperty] private int _lowStockCount;
    [ObservableProperty] private int _expiryAlertCount;

    // ── 待办队列 ──

    public ObservableCollection<PrescriptionHistoryDto> PendingReviewList { get; } = new();
    public ObservableCollection<PrescriptionHistoryDto> PendingBillingList { get; } = new();
    public ObservableCollection<PrescriptionHistoryDto> PendingDispenseList { get; } = new();

    // ── 最近活动 ──

    public ObservableCollection<PrescriptionHistoryDto> RecentPrescriptions { get; } = new();

    // ── 近7日趋势 ──

    /// <summary>每日统计数据</summary>
    public record DailyStat(DateTime Date, int PrescriptionCount, decimal Revenue);

    /// <summary>近7日趋势数据</summary>
    public ObservableCollection<DailyStat> DailyStats { get; } = new();

    /// <summary>近7日处方总量</summary>
    [ObservableProperty] private int _weekTotalPrescriptions;

    /// <summary>近7日总收入</summary>
    [ObservableProperty] private decimal _weekTotalRevenue;

    // ── 状态 ──
/// <summary>导航到处方开具（由 MainWindow 订阅）</summary>
    public event Action? NavigateToPrescriptionRequested;

    /// <summary>导航到患者管理</summary>
    public event Action? NavigateToPatientRequested;

    /// <summary>导航到库存管理</summary>
    public event Action? NavigateToInventoryRequested;

    /// <summary>导航到收费管理</summary>
    public event Action? NavigateToBillingRequested;

    public DashboardViewModel(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>加载 Dashboard 全部数据</summary>
    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var billingSvc = scope.ServiceProvider.GetRequiredService<IBillingService>();
            var rxSvc = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();
            var invSvc = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            var today = DateTime.Today;

            // 1. 今日 KPI
            var report = await billingSvc.GetDailyReportAsync(today);
            TodayPrescriptionCount = report?.PrescriptionCount ?? 0;
            TodayRevenue = report?.TotalAmount ?? 0m;

            // 2. 全部处方（用于待办和最近活动）
            var allPrescriptions = (await rxSvc.GetPrescriptionHistoryAsync(
                searchKeyword: null,
                fromDate: today.AddDays(-7),
                toDate: today.AddDays(1))).ToList();

            // 状态：0=草稿, 1=已保存, 2=已收费, 3=已作废, 4=已审核, 5=已发药
            PendingReviewList.Clear();
            PendingBillingList.Clear();
            PendingDispenseList.Clear();

            foreach (var p in allPrescriptions)
            {
                switch (p.Status)
                {
                    case 1: // 已保存 → 待审核
                        PendingReviewList.Add(p);
                        break;
                    case 4: // 已审核 → 待收费
                        PendingBillingList.Add(p);
                        break;
                    case 2: // 已收费 → 待发药
                        PendingDispenseList.Add(p);
                        break;
                }
            }

            PendingReviewCount = PendingReviewList.Count;
            PendingBillingCount = PendingBillingList.Count;
            PendingDispenseCount = PendingDispenseList.Count;

            // 3. 最近处方（按创建时间倒序取前8条）
            RecentPrescriptions.Clear();
            foreach (var p in allPrescriptions
                         .Where(p => p.Status != 0 && p.Status != 3) // 排除草稿和作废
                         .OrderByDescending(p => p.CreatedAt)
                         .Take(8))
            {
                RecentPrescriptions.Add(p);
            }

            // 4. 库存预警
            var stockSummary = await invSvc.GetAllStockSummaryAsync();
            LowStockCount = stockSummary.Count(s => s.IsLowStock);

            var expiryAlerts = await invSvc.GetExpiryAlertsAsync();
            ExpiryAlertCount = expiryAlerts.Count;

            // 5. 近7日趋势
            DailyStats.Clear();
            int weekRx = 0;
            decimal weekRev = 0m;
            for (int i = 6; i >= 0; i--)
            {
                var date = today.AddDays(-i);
                var daily = await billingSvc.GetDailyReportAsync(date);
                var count = daily?.PrescriptionCount ?? 0;
                var rev = daily?.TotalAmount ?? 0m;
                DailyStats.Add(new DailyStat(date, count, rev));
                weekRx += count;
                weekRev += rev;
            }
            WeekTotalPrescriptions = weekRx;
            WeekTotalRevenue = weekRev;

            StatusMessage = "数据已更新";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"加载 Dashboard 失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── 快捷操作 ──

    [RelayCommand]
    private void NavigateToPrescription() => NavigateToPrescriptionRequested?.Invoke();

    [RelayCommand]
    private void NavigateToPatient() => NavigateToPatientRequested?.Invoke();

    [RelayCommand]
    private void NavigateToInventory() => NavigateToInventoryRequested?.Invoke();

    [RelayCommand]
    private void NavigateToBilling() => NavigateToBillingRequested?.Invoke();
}
