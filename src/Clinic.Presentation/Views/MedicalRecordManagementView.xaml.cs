using System.Windows.Controls;
using System.Windows.Input;
using Clinic.Presentation.Services;
using Clinic.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation.Views;

public partial class MedicalRecordManagementView : UserControl
{
    public MedicalRecordManagementView()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            if (DataContext is MedicalRecordManagementViewModel vm)
            {
                _ = vm.LoadDataCommand.ExecuteAsync(null);
            }
        };
    }

    /// <summary>
    /// 双击病历行打开病历详情窗口，查看完整病历内容。
    /// </summary>
    private void RecordDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MedicalRecordManagementViewModel vm
            && vm.SelectedRecord is not null)
        {
            var windowService = App.Services.GetRequiredService<IWindowService>();
            windowService.ShowMedicalRecordDetailWindow(vm.SelectedRecord.Id);
        }
    }
}
