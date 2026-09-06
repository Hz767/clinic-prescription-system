using System.Windows;
using System.Windows.Controls;
using Clinic.Application.DTOs;

namespace Clinic.Presentation.Views;

/// <summary>
/// 收费管理视图代码后置。
/// </summary>
public partial class BillingView : UserControl
{
    public BillingView()
    {
        InitializeComponent();
    }

    /// <summary>搜索处方按钮点击：设置当前搜索字段并执行搜索</summary>
    private void SearchPrescription_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.BillingViewModel vm) return;
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out var field))
        {
            vm.ActivePrescriptionSearchField = field;
        }
        vm.SearchPrescriptionsCommand.Execute(null);
    }

    /// <summary>搜索结果列表选择：将选中处方填入对应输入框</summary>
    private void SearchResult_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ViewModels.BillingViewModel vm) return;
        if (sender is ListBox lb && lb.SelectedItem is PrescriptionHistoryDto rx)
        {
            vm.SelectPrescriptionFromSearchCommand.Execute(rx);
            lb.SelectedItem = null;
        }
    }

    /// <summary>待办工作台处方卡片点击：跳转到业务办理并填充处方</summary>
    private void PendingItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not ViewModels.BillingViewModel vm) return;
        if (sender is FrameworkElement fe && fe.DataContext is PrescriptionHistoryDto rx)
        {
            vm.SelectPendingPrescriptionCommand.Execute(rx);
        }
    }
}
