using System.Windows;

namespace Clinic.Presentation.Services;

/// <summary>
/// 对话框服务接口，抽象MessageBox和窗口显示，便于单元测试和统一管理。
/// </summary>
public interface IDialogService
{
    /// <summary>显示信息消息框</summary>
    void ShowMessage(string message, string title = "提示");

    /// <summary>显示警告消息框</summary>
    void ShowWarning(string message, string title = "警告");

    /// <summary>显示错误消息框</summary>
    void ShowError(string message, string title = "错误");

    /// <summary>显示确认对话框，返回true表示确认，false表示取消</summary>
    bool ShowConfirm(string message, string title = "确认");

    /// <summary>显示带自定义按钮的确认对话框</summary>
    MessageBoxResult ShowDialog(string message, string title, MessageBoxButton buttons, MessageBoxImage icon);
}
