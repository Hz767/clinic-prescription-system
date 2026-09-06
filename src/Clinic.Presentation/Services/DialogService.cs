using System.Windows;

namespace Clinic.Presentation.Services;

/// <summary>
/// 对话框服务默认实现，基于WPF MessageBox。
/// </summary>
public class DialogService : IDialogService
{
    public void ShowMessage(string message, string title = "提示")
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void ShowWarning(string message, string title = "警告")
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public void ShowError(string message, string title = "错误")
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public bool ShowConfirm(string message, string title = "确认")
    {
        var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    public MessageBoxResult ShowDialog(string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
    {
        return MessageBox.Show(message, title, buttons, icon);
    }
}
