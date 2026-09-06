using CommunityToolkit.Mvvm.ComponentModel;

namespace Clinic.Presentation.ViewModels;

/// <summary>
/// ViewModel 基类，统一管理通用状态和错误处理。
/// 所有 ViewModel 应继承此类，避免重复定义 IsBusy/ErrorMessage/StatusMessage。
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    /// <summary>是否正在执行异步操作（用于显示加载状态和禁用按钮）</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>错误消息（null表示无错误）</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>状态/成功消息（null表示无状态消息）</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>
    /// 执行异步操作，统一管理 IsBusy 状态和异常处理。
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="action">要执行的异步操作</param>
    /// <param name="errorPrefix">错误消息前缀，如"加载失败"</param>
    /// <returns>操作结果；异常时返回 default</returns>
    protected async Task<T?> ExecuteAsync<T>(Func<Task<T>> action, string errorPrefix = "操作失败")
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            return await action();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"{errorPrefix}：{ex.Message}";
            return default;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 执行无返回值的异步操作，统一管理 IsBusy 状态和异常处理。
    /// </summary>
    /// <param name="action">要执行的异步操作</param>
    /// <param name="errorPrefix">错误消息前缀</param>
    /// <returns>true表示成功，false表示失败</returns>
    protected async Task<bool> ExecuteAsync(Func<Task> action, string errorPrefix = "操作失败")
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await action();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"{errorPrefix}：{ex.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>清除错误和状态消息</summary>
    protected void ClearMessages()
    {
        ErrorMessage = null;
        StatusMessage = null;
    }
}
