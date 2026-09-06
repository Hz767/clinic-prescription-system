using System.Windows;
using System.Windows.Controls;
using Clinic.Presentation.ViewModels;

namespace Clinic.Presentation.Views;

/// <summary>
/// Dashboard 首页视图代码后置。
/// </summary>
public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DashboardViewModel vm) return;

        try
        {
            // 处方量趋势图
            if (RxTrendChart is not null)
            {
                RxTrendChart.ValueSelector = item =>
                {
                    if (item is DashboardViewModel.DailyStat stat) return stat.PrescriptionCount;
                    return 0;
                };
                RxTrendChart.LabelSelector = item =>
                {
                    if (item is DashboardViewModel.DailyStat stat) return stat.Date.ToString("MM-dd");
                    return string.Empty;
                };
                RxTrendChart.ItemsSource = vm.DailyStats;
            }

            // 收入趋势图
            if (RevenueTrendChart is not null)
            {
                RevenueTrendChart.ValueSelector = item =>
                {
                    if (item is DashboardViewModel.DailyStat stat) return stat.Revenue;
                    return 0;
                };
                RevenueTrendChart.LabelSelector = item =>
                {
                    if (item is DashboardViewModel.DailyStat stat) return stat.Date.ToString("MM-dd");
                    return string.Empty;
                };
                RevenueTrendChart.ItemsSource = vm.DailyStats;
            }
        }
        catch
        {
            // 趋势图初始化失败不影响主界面
        }
    }
}
