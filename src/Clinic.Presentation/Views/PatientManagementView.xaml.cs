using System.Windows;
using System.Windows.Controls;
using Clinic.Presentation.ViewModels;

namespace Clinic.Presentation.Views;

/// <summary>
/// 患者管理视图。DataTemplate 在 MainWindow.xaml 中将其与 PatientManagementViewModel 关联。
/// </summary>
public partial class PatientManagementView : UserControl
{
    public PatientManagementView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PatientManagementViewModel vm)
        {
            _ = vm.CheckLlmStatusAsync();
            _ = vm.LoadAllPatientsAsync();
        }
    }
}
