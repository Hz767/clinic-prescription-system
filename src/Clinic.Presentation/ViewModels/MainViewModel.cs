using System.IO;
using System.Windows;
using Clinic.Application.Interfaces;
using Clinic.Presentation.Helpers;
using Clinic.Presentation.Views;
using Clinic.Shared.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IUserSession _session;
    private readonly IServiceProvider _services;
    private readonly ILlmService _llmService;

    [ObservableProperty]
    private ObservableObject? _currentViewModel;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>是否正在执行异步操作（防重复提交）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyAuditChainCommand))]
    private bool _isBusy;

    public string CurrentUserDisplayName => _session.DisplayName ?? "未登录";

    /// <summary>当前用户角色显示文本</summary>
    public string CurrentUserRoleText => _session.Role switch
    {
        UserRole.Doctor => "医生",
        UserRole.Nurse => "护士",
        UserRole.Readonly => "只读用户",
        _ => "未知"
    };

    /// <summary>是否显示处方开具菜单（仅 Doctor）</summary>
    public bool CanPrescribe => _session.Role == UserRole.Doctor;

    /// <summary>LLM 服务是否可用（用于 UI 显示 AI 辅助功能入口）</summary>
    [ObservableProperty]
    private bool _llmIsAvailable;

    /// <summary>退出登录时触发，由 App.xaml.cs 订阅</summary>
    public event Action? LogoutRequested;

    public MainViewModel(IUserSession session, IServiceProvider services, ILlmService llmService)
    {
        _session = session;
        _services = services;
        _llmService = llmService;
    }

    [RelayCommand]
    private void NavigateToPatient()
    {
        CurrentViewModel = _services.GetRequiredService<PatientManagementViewModel>();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateToPrescription))]
    private void NavigateToPrescription()
    {
        try
        {
            var vm = _services.GetRequiredService<PrescriptionViewModel>();
            // 订阅导航到收费的事件（每次创建新 VM 时订阅）
            vm.NavigateToBillingRequested -= OnNavigateToBillingRequested;
            vm.NavigateToBillingRequested += OnNavigateToBillingRequested;
            CurrentViewModel = vm;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NavigateToPrescription 异常: {ex}");
            ErrorMessage = $"打开处方开具失败：{ex.Message}";
        }
    }

    private bool CanNavigateToPrescription() => CanPrescribe;

    /// <summary>从处方页面跳转到收费页面，并预填处方ID</summary>
    private async void OnNavigateToBillingRequested(long prescriptionId)
    {
        var billingVm = _services.GetRequiredService<BillingViewModel>();
        CurrentViewModel = billingVm;
        await billingVm.SetPendingPrescription(prescriptionId);
    }

    [RelayCommand]
    private void NavigateToBilling()
    {
        CurrentViewModel = _services.GetRequiredService<BillingViewModel>();
    }

    [RelayCommand]
    private void NavigateToInventory()
    {
        CurrentViewModel = _services.GetRequiredService<InventoryViewModel>();
    }

    [RelayCommand]
    private void NavigateToPrescriptionHistory()
    {
        var vm = _services.GetRequiredService<PrescriptionHistoryViewModel>();
        // 订阅导航到收费的事件（每次创建新 VM 时订阅）
        vm.NavigateToBillingRequested -= OnNavigateToBillingRequested;
        vm.NavigateToBillingRequested += OnNavigateToBillingRequested;
        CurrentViewModel = vm;
    }

    /// <summary>创建数据备份</summary>
    [RelayCommand(CanExecute = nameof(CanCreateBackup))]
    private async Task CreateBackupAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            using var scope = _services.CreateScope();
            var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            var backupDir = config["Backup:Dir"] ?? "Backups";
            var fullDir = Path.Combine(AppContext.BaseDirectory, backupDir);

            var backupPath = await backupService.CreateBackupAsync(fullDir);

            StatusMessage = $"备份成功：{Path.GetFileName(backupPath)}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"备份失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCreateBackup() => !IsBusy;

    /// <summary>从备份文件恢复数据（需重启应用）</summary>
    [RelayCommand(CanExecute = nameof(CanRestoreBackup))]
    private async Task RestoreBackupAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        // 选择备份文件
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择备份文件",
            Filter = "备份文件 (*.zip)|*.zip",
            InitialDirectory = Path.Combine(AppContext.BaseDirectory,
                _services.GetRequiredService<IConfiguration>()["Backup:Dir"] ?? "Backups")
        };

        if (dialog.ShowDialog() != true)
        {
            IsBusy = false;
            return;
        }

        var result = MessageBox.Show(
            "恢复操作将覆盖当前数据库，恢复后需要重启应用。\n确定要继续吗？",
            "确认恢复",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.OK)
        {
            IsBusy = false;
            return;
        }

        try
        {
            using var scope = _services.CreateScope();
            var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();

            await backupService.RestoreBackupAsync(dialog.FileName);

            MessageBox.Show(
                "数据恢复成功！应用将重新启动以加载恢复的数据。",
                "恢复完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // 重启应用
            System.Diagnostics.Process.Start(
                System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"恢复失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRestoreBackup() => !IsBusy;

    /// <summary>验证审计日志哈希链完整性</summary>
    [RelayCommand(CanExecute = nameof(CanVerifyAuditChain))]
    private async Task VerifyAuditChainAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            using var scope = _services.CreateScope();
            var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

            var result = await auditService.VerifyChainAsync();

            if (result.IsValid)
            {
                StatusMessage = $"审计日志验证通过（共 {result.TotalRecords} 条记录）";
            }
            else
            {
                ErrorMessage = $"审计日志验证失败：{result.ErrorMessage}（断裂点 ID={result.FirstBrokenId}）";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"验证失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanVerifyAuditChain() => !IsBusy;

    [RelayCommand]
    private void Logout()
    {
        _session.Clear();
        LogoutRequested?.Invoke();
    }

    /// <summary>
    /// 编辑当前登录用户的显示名称（用于处方签名）。
    /// 弹出输入对话框，确认后更新数据库和会话。
    /// </summary>
    [RelayCommand]
    private async Task EditDoctorNameAsync()
    {
        var currentName = _session.DisplayName ?? "";
        var newName = EditNameDialog.Show(currentName);

        if (string.IsNullOrWhiteSpace(newName) || newName == currentName)
            return;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            using var scope = _services.CreateScope();
            var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
            await authService.UpdateDisplayNameAsync(newName);

            // 刷新 UI 显示
            OnPropertyChanged(nameof(CurrentUserDisplayName));
            StatusMessage = $"医生姓名已更新为：{newName.Trim()}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"修改失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 登录成功后刷新权限相关的 UI 状态。
    /// MainViewModel 为 Singleton，跨登录会话复用，
    /// 需手动通知属性变更和命令 CanExecute 重新评估。
    /// </summary>
    public async void RefreshPermissions()
    {
        // 清除跨用户状态泄漏：登出后残留的上一用户 ViewModel（H-14）
        CurrentViewModel = null;

        OnPropertyChanged(nameof(CurrentUserDisplayName));
        OnPropertyChanged(nameof(CurrentUserRoleText));
        OnPropertyChanged(nameof(CanPrescribe));
        NavigateToPrescriptionCommand.NotifyCanExecuteChanged();

        // 如果当前显示的是处方页面但用户无处方权限，切换回患者管理
        if (CurrentViewModel is PrescriptionViewModel && !CanPrescribe)
        {
            CurrentViewModel = _services.GetRequiredService<PatientManagementViewModel>();
        }

        // 登录后默认导航到处方开具页面（核心业务），无处方权限时回退到患者管理
        if (CurrentViewModel is null)
        {
            if (CanPrescribe)
            {
                try
                {
                    var vm = _services.GetRequiredService<PrescriptionViewModel>();
                    vm.NavigateToBillingRequested -= OnNavigateToBillingRequested;
                    vm.NavigateToBillingRequested += OnNavigateToBillingRequested;
                    CurrentViewModel = vm;
                }
                catch
                {
                    // 处方页面初始化异常时回退到患者管理
                    CurrentViewModel = _services.GetRequiredService<PatientManagementViewModel>();
                }
            }
            else
            {
                CurrentViewModel = _services.GetRequiredService<PatientManagementViewModel>();
            }
        }

        // 异步检测 LLM 状态（不阻塞 UI）
        _ = Task.Run(async () =>
        {
            try
            {
                var status = await _llmService.GetStatusAsync();
                LlmIsAvailable = status.IsAvailable;
            }
            catch
            {
                LlmIsAvailable = false;
            }
        });
    }
}
