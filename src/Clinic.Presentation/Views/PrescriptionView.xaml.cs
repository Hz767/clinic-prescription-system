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
    }

    private void PrescriptionView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PrescriptionViewModel vm)
        {
            _ = vm.LoadDrugsCommand.ExecuteAsync(null);
            _ = vm.CheckLlmStatusAsync();
        }
    }

    /// <summary>搜索框获得焦点时，若有已过滤的患者列表则重新显示下拉</summary>
    private void PatientSearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is PrescriptionViewModel vm && vm.FilteredPatients.Count > 0)
        {
            vm.ShowPatientDropdown = true;
        }
    }

    /// <summary>搜索框失去焦点时，延迟关闭下拉以允许点击列表项</summary>
    private async void PatientSearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        await Task.Delay(200);
        if (DataContext is PrescriptionViewModel vm)
        {
            // 如果焦点仍在列表或按钮上，保持下拉
            if (PatientListBox.IsKeyboardFocusWithin || PatientListBox.IsMouseOver)
                return;
            vm.ShowPatientDropdown = false;
        }
    }

    /// <summary>点击下拉列表中的患者行，触发选择</summary>
    private void PatientListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is PatientDto patient)
        {
            if (DataContext is PrescriptionViewModel vm)
            {
                vm.SelectPatientCommand.Execute(patient);
            }
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

        // 等待绑定更新完成后再处理
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);

        await vm.UpdateItemInlineAsync(item);

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
}
