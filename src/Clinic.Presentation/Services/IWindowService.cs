using System.Windows;

namespace Clinic.Presentation.Services;

/// <summary>
/// 窗口服务接口，统一管理窗口的创建和显示，便于单元测试和统一管理。
/// </summary>
public interface IWindowService
{
    /// <summary>显示登录窗口</summary>
    void ShowLoginWindow();

    /// <summary>显示主窗口</summary>
    void ShowMainWindow();

    /// <summary>显示医生注册窗口（模态）</summary>
    bool? ShowDoctorRegistrationWindow();

    /// <summary>显示病历详情窗口（模态）</summary>
    bool? ShowMedicalRecordDetailWindow(long recordId);

    /// <summary>显示处方详情窗口（模态）</summary>
    bool? ShowPrescriptionDetailWindow(long prescriptionId);

    /// <summary>显示通用对话框</summary>
    bool? ShowDialog<TWindow>() where TWindow : Window;

    /// <summary>关闭指定窗口</summary>
    void CloseWindow(Window window);
}
