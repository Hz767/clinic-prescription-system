using System.Collections.ObjectModel;
using System.Windows;

namespace Clinic.Presentation.Services;

// 别名：避免与 Clinic.Application 命名空间冲突（Application.Current 需完全限定）
using App = System.Windows.Application;

/// <summary>Toast 消息类型</summary>
public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>单条 Toast 消息</summary>
public sealed record ToastMessage(Guid Id, string Message, ToastType Type);

/// <summary>
/// 全局 Toast 反馈服务（右下角悬浮提示，3 秒自动消失）。
/// 使用静态单例（与 InventoryLock.Instance 同模式），任何线程都可调用，
/// 内部通过 UI 派发器保证线程安全，避免为所有 ViewModel 改动构造函数。
/// </summary>
public sealed class ToastService
{
    /// <summary>全局唯一实例</summary>
    public static ToastService Instance { get; } = new();

    private ToastService() { }

    /// <summary>当前待展示的 Toast 集合（宿主控件绑定此集合）</summary>
    public ObservableCollection<ToastMessage> Toasts { get; } = new();

    /// <summary>展示一条 Toast，默认 3 秒后自动消失</summary>
    public void Show(string message, ToastType type = ToastType.Success, int durationMs = 3000)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var toast = new ToastMessage(Guid.NewGuid(), message, type);
        var dispatcher = App.Current?.Dispatcher;
        if (dispatcher is null) return;

        dispatcher.BeginInvoke(new Action(() =>
        {
            if (!Toasts.Contains(toast))
                Toasts.Add(toast);
            _ = RemoveAfterAsync(toast, durationMs);
        }));
    }

    private async Task RemoveAfterAsync(ToastMessage toast, int delayMs)
    {
        await Task.Delay(delayMs);
        var dispatcher = App.Current?.Dispatcher;
        dispatcher?.BeginInvoke(new Action(() => Toasts.Remove(toast)));
    }

    public void Info(string message) => Show(message, ToastType.Info);
    public void Success(string message) => Show(message, ToastType.Success);
    public void Warning(string message) => Show(message, ToastType.Warning);
    public void Error(string message) => Show(message, ToastType.Error);
}