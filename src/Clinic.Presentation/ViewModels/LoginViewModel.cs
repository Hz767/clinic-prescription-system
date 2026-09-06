using System.Windows;
using System.Windows.Media;
using Clinic.Application.Interfaces;
using Clinic.Presentation.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAiAssistantManager _aiManager;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _username = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _password = string.Empty;

    /// <summary>是否启用 AI 辅助（用户在登录时勾选）</summary>
    [ObservableProperty]
    private bool _enableAiAssistant;

    /// <summary>系统自检消息</summary>
    [ObservableProperty]
    private string? _healthCheckMessage;

    /// <summary>系统自检消息颜色</summary>
    [ObservableProperty]
    private Brush _healthCheckColor = Brushes.Gray;

    /// <summary>登录成功时触发，由 App.xaml.cs 订阅以切换窗口</summary>
    public event Action? LoginSucceeded;

    public LoginViewModel(IServiceScopeFactory scopeFactory, IAiAssistantManager aiManager)
    {
        _scopeFactory = scopeFactory;
        _aiManager = aiManager;
    }

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        HealthCheckMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
            var success = await authService.LoginAsync(Username, Password);

            if (!success)
            {
                ErrorMessage = "用户名或密码错误，连续失败 5 次将锁定 10 分钟";
                return;
            }

            // 如果用户勾选了启用 AI 辅助，则执行系统自检并加载大模型
            if (EnableAiAssistant)
            {
                HealthCheckMessage = "正在执行系统自检...";
                HealthCheckColor = Brushes.DodgerBlue;

                var aiEnabled = await _aiManager.EnableAsync();
                if (aiEnabled)
                {
                    HealthCheckMessage = _aiManager.StatusMessage;
                    HealthCheckColor = Brushes.SeaGreen;
                }
                else
                {
                    // AI 辅助启用失败（如内存不足），但不阻止登录
                    HealthCheckMessage = _aiManager.StatusMessage;
                    HealthCheckColor = Brushes.DarkOrange;
                    // 给用户一点时间看到提示
                    await Task.Delay(1500);
                }
            }

            LoginSucceeded?.Invoke();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("锁定"))
        {
            // 账户被锁定，AuthService 抛出包含锁定信息的异常
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"登录失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLogin()
        => !IsBusy && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}
