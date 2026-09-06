using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation.Services;

/// <summary>
/// 窗口服务默认实现，通过DI容器创建窗口实例。
/// </summary>
public class WindowService : IWindowService
{
    private readonly IServiceProvider _serviceProvider;

    public WindowService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void ShowLoginWindow()
    {
        var window = _serviceProvider.GetRequiredService<LoginWindow>();
        window.Show();
    }

    public void ShowMainWindow()
    {
        var window = _serviceProvider.GetRequiredService<MainWindow>();
        window.Show();
    }

    public bool? ShowDoctorRegistrationWindow()
    {
        var window = _serviceProvider.GetRequiredService<DoctorRegistrationWindow>();
        return window.ShowDialog();
    }

    public bool? ShowMedicalRecordDetailWindow(long recordId)
    {
        var window = new MedicalRecordDetailWindow(recordId);
        return window.ShowDialog();
    }

    public bool? ShowPrescriptionDetailWindow(long prescriptionId)
    {
        var window = new PrescriptionDetailWindow(prescriptionId);
        return window.ShowDialog();
    }

    public bool? ShowDialog<TWindow>() where TWindow : Window
    {
        var window = _serviceProvider.GetRequiredService<TWindow>();
        return window.ShowDialog();
    }

    public void CloseWindow(Window window)
    {
        window.Close();
    }
}
