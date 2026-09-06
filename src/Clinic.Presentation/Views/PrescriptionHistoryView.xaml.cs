using System.Windows.Controls;
using System.Windows.Input;
using Clinic.Infrastructure.Data;
using Clinic.Presentation.Services;
using Clinic.Presentation.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation.Views;

public partial class PrescriptionHistoryView : UserControl
{
    public PrescriptionHistoryView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 双击处方行打开处方详情窗口，查看原始处方内容。
    /// </summary>
    private void PrescriptionDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PrescriptionHistoryViewModel vm
            && vm.SelectedPrescription is not null)
        {
            var windowService = App.Services.GetRequiredService<IWindowService>();
            windowService.ShowPrescriptionDetailWindow(vm.SelectedPrescription.Id);
        }
    }
}
