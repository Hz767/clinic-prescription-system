using System.Windows.Controls;
using System.Windows.Input;
using Clinic.Presentation.ViewModels;

namespace Clinic.Presentation.Views;

public partial class PrescriptionHistoryView : UserControl
{
    public PrescriptionHistoryView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 双击处方行跳转到收费页面（仅对已保存/已审核处方生效）。
    /// 复用 ViewModel 的 GoToBillingCommand 进行权限和状态检查。
    /// </summary>
    private void PrescriptionDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PrescriptionHistoryViewModel vm
            && vm.GoToBillingCommand.CanExecute(null))
        {
            vm.GoToBillingCommand.Execute(null);
        }
    }
}
