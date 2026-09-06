using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Clinic.Presentation.ViewModels;

namespace Clinic.Presentation.Views;

/// <summary>
/// 处方开具视图。DataTemplate 在 MainWindow.xaml 中将其与 PrescriptionViewModel 关联。
/// 加载时自动触发药品目录加载。
/// </summary>
public partial class PrescriptionView : UserControl
{
    public PrescriptionView()
    {
        InitializeComponent();
        IsVisibleChanged += PrescriptionView_IsVisibleChanged;
    }

    private void PrescriptionView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 页面每次变为可见时，重新检查AI状态（用户可能在其他页面启动了AI）
        if ((bool)e.NewValue && DataContext is PrescriptionViewModel vm)
        {
            _ = vm.CheckLlmStatusAsync();
        }
    }

    /// <summary>关闭循证医学辅助面板</summary>
    private void CloseEvidencePanel_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PrescriptionViewModel vm)
        {
            vm.ShowEvidenceBasedPanel = false;
        }
    }

    private void PrescriptionView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PrescriptionViewModel vm)
        {
            _ = vm.LoadDrugsCommand.ExecuteAsync(null);
            _ = vm.CheckLlmStatusAsync();
        }
    }

    /// <summary>双击药品列表行，快速添加到处方明细</summary>
    private void DrugDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is DrugDto drug)
        {
            if (DataContext is PrescriptionViewModel vm)
            {
                if (vm.QuickAddDrugCommand.CanExecute(drug))
                {
                    vm.QuickAddDrugCommand.Execute(drug);
                }
                else if (vm.ErrorMessage is null)
                {
                    vm.ErrorMessage = vm.IsBusy
                        ? "正在处理中，请稍候..."
                        : "请先选择患者并填写诊断后，再双击药品添加明细";
                }
            }
        }
    }

    /// <summary>处方明细 DataGrid 单元格编辑结束：重算数量/小计并持久化到数据库</summary>
    private async void ItemsDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not PrescriptionItemDto item) return;
        if (DataContext is not PrescriptionViewModel vm) return;

        // 判断是否是数量列被修改（数量列只重算金额，不重算数量）
        var isQtyColumn = e.Column?.Header?.ToString() == "数量";

        // 等待绑定更新完成后再处理
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);

        await vm.UpdateItemInlineAsync(item, isQtyColumn);

        // 刷新 DataGrid 以显示更新后的 Qty 和 Subtotal
        ItemsDataGrid.Items.Refresh();
    }

    /// <summary>
    /// 体征输入框按键过滤：仅允许数字和小数点。
    /// 阻止字母、符号（+、-、逗号等）和中文输入。
    /// IME 已通过 InputMethod.IsInputMethodEnabled=False 禁用，
    /// 小数点键始终产生 "." 无需拦截按键。
    /// </summary>
    private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // 仅允许数字和小数点
        foreach (char c in e.Text)
        {
            if (!char.IsDigit(c) && c != '.')
            {
                e.Handled = true;
                return;
            }
        }

        // 限制只能有一个小数点
        if (sender is TextBox tb && e.Text.Contains('.'))
        {
            if (tb.Text.Contains('.'))
            {
                e.Handled = true;
            }
        }
    }

    // ── 悬浮辅助工具栏（主诉/诊断） ──

    private void ChiefComplaintTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        ChiefComplaintPopup.IsOpen = true;
    }

    private void DiagnosisTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        DiagnosisPopup.IsOpen = true;
    }

    private void ChiefComplaintTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // 延迟检查：如果焦点不在 Popup 内，则关闭
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ChiefComplaintPopup.IsKeyboardFocusWithin)
                ChiefComplaintPopup.IsOpen = false;
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void DiagnosisTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!DiagnosisPopup.IsKeyboardFocusWithin)
                DiagnosisPopup.IsOpen = false;
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ChiefComplaintTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Content is string tag)
        {
            if (DataContext is PrescriptionViewModel vm)
            {
                if (vm.AddChiefComplaintCommand.CanExecute(tag))
                    vm.AddChiefComplaintCommand.Execute(tag);
            }
            // 保持 Popup 打开，不转移焦点，用户可继续选择多个标签
            // 点击输入框外部时由 LostFocus 关闭
        }
    }

    private void DiagnosisTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Content is string tag)
        {
            if (DataContext is PrescriptionViewModel vm)
            {
                if (vm.AddDiagnosisCommand.CanExecute(tag))
                    vm.AddDiagnosisCommand.Execute(tag);
            }
            // 保持 Popup 打开，不转移焦点，用户可继续选择多个标签
        }
    }
}
