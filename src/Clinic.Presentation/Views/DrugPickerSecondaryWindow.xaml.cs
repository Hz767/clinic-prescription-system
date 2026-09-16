using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Clinic.Application.DTOs;
using Clinic.Presentation.ViewModels;

namespace Clinic.Presentation.Views;

/// <summary>
/// 药品选择副屏窗口（双屏布局，阶段三）。
/// DataContext 与主处方窗共享同一个 <see cref="PrescriptionViewModel"/> 实例，
/// 因此在副屏选药/搜索/查看明细时，主屏录入区实时同步，实现"主屏录入、副屏选药"。
/// </summary>
public partial class DrugPickerSecondaryWindow : Window
{
    private readonly PrescriptionViewModel _viewModel;

    public DrugPickerSecondaryWindow(PrescriptionViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>双击药品行：快速添加到处方明细（与主屏双击行为一致）</summary>
    private void DrugsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox box && box.SelectedItem is DrugDto drug)
        {
            if (_viewModel.QuickAddDrugCommand.CanExecute(drug))
            {
                _viewModel.QuickAddDrugCommand.Execute(drug);
            }
            else if (_viewModel.ErrorMessage is null)
            {
                _viewModel.ErrorMessage = _viewModel.IsBusy
                    ? "正在处理中，请稍候..."
                    : "请先选择患者并填写诊断后，再在副屏选择药品";
            }
        }
    }
}