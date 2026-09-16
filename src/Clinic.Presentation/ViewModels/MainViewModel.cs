using System.IO;
using System.Windows;
using System.Windows.Media;
using Clinic.Application.Interfaces;
using Clinic.Presentation.Helpers;
using Clinic.Presentation.Services;
using Clinic.Presentation.Views;
using Clinic.Shared.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clinic.Presentation.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IUserSession _session;
    private readonly IServiceProvider _services;
    private readonly ILlmService _llmService;
    private readonly IAiAssistantManager _aiManager;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private ObservableObject? _currentViewModel;

    /// <summary>当前导航页面标识（用于侧边栏高亮）</summary>
    [ObservableProperty]
    private string _currentNavKey = string.Empty;
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
        UserRole.Pharmacist => "药师",
        _ => "未知"
    };

    /// <summary>是否显示处方开具菜单（仅 Doctor）</summary>
    public bool CanPrescribe => _session.Role == UserRole.Doctor;

    /// <summary>LLM 服务是否可用（用于 UI 显示 AI 辅助功能入口）</summary>
    [ObservableProperty]
    private bool _llmIsAvailable;

    // ── AI 辅助状态栏 ──
    [ObservableProperty]
    private bool _aiStatusVisible;

    [ObservableProperty]
    private string _aiStatusText = "AI 辅助未启用";

    [ObservableProperty]
    private Brush _aiStatusColor = Brushes.Gray;

    [ObservableProperty]
    private bool _aiLoadButtonVisible;

    [ObservableProperty]
    private bool _aiUnloadButtonVisible;

    [ObservableProperty]
    private string _aiHealthCheckButtonText = "启用AI辅助";

    /// <summary>退出登录时触发，由 App.xaml.cs 订阅</summary>
    public event Action? LogoutRequested;

    public MainViewModel(IUserSession session, IServiceProvider services, ILlmService llmService, IAiAssistantManager aiManager, ILogger<MainViewModel> logger, IDialogService dialogService)
    {
        _session = session;
        _services = services;
        _llmService = llmService;
        _aiManager = aiManager;
        _logger = logger;
        _dialogService = dialogService;
        _aiManager.StateChanged += OnAiStateChanged;
        // 不在构造函数中调用异步方法，避免未观察到的异常终止进程
        // 由 MainWindow.Loaded 事件调用 InitializeAsync
    }

    /// <summary>初始化异步数据（由 MainWindow.Loaded 调用）</summary>
    public async Task InitializeAsync()
    {
        try
        {
            await RefreshAiStatusAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "InitializeAsync 异常");
        }
    }

    private void OnAiStateChanged(object? sender, EventArgs e)
    {
        _ = RefreshAiStatusAsync();
    }

    private async Task RefreshAiStatusAsync()
    {
        try
        {
            await _aiManager.RefreshStatusAsync();
            AiStatusVisible = true;
            AiStatusText = _aiManager.StatusMessage;
            AiStatusColor = _aiManager.IsModelLoaded
                ? Brushes.SeaGreen
                : _aiManager.IsEnabled
                    ? Brushes.DarkOrange
                    : Brushes.Gray;
            AiLoadButtonVisible = _aiManager.IsEnabled && !_aiManager.IsModelLoaded && !_aiManager.IsBusy;
            AiUnloadButtonVisible = _aiManager.IsModelLoaded && !_aiManager.IsBusy;
            AiHealthCheckButtonText = _aiManager.IsEnabled ? "系统自检" : "启用AI辅助";
        }
        catch { AiStatusVisible = false; }
    }

    [RelayCommand]
    private async Task LoadAiModelAsync()
    {
        await _aiManager.LoadModelAsync();
        await RefreshAiStatusAsync();
    }

    [RelayCommand]
    private async Task UnloadAiModelAsync()
    {
        await _aiManager.UnloadModelAsync();
        await RefreshAiStatusAsync();
    }

    [RelayCommand]
    private async Task RunAiHealthCheckAsync()
    {
        // 如果AI辅助未启用，先启用AI辅助（含系统自检和大模型加载）
        if (!_aiManager.IsEnabled)
        {
            StatusMessage = "正在启用AI辅助并执行系统自检...";
            var enabled = await _aiManager.EnableAsync();
            if (enabled)
            {
                StatusMessage = _aiManager.StatusMessage;
            }
            else
            {
                StatusMessage = $"AI辅助启用失败：{_aiManager.StatusMessage}";
            }
            await RefreshAiStatusAsync();
            return;
        }

        // AI已启用，仅运行健康检查
        var result = await _aiManager.RunHealthCheckAsync();
        StatusMessage = result.Message;
        await RefreshAiStatusAsync();
    }

    [RelayCommand]
    private void NavigateToDashboard()
    {
        var vm = _services.GetRequiredService<DashboardViewModel>();
        vm.NavigateToPrescriptionRequested -= OnDashboardNavigateToPrescription;
        vm.NavigateToPrescriptionRequested += OnDashboardNavigateToPrescription;
        vm.NavigateToPatientRequested -= OnDashboardNavigateToPatient;
        vm.NavigateToPatientRequested += OnDashboardNavigateToPatient;
        vm.NavigateToBillingRequested -= OnDashboardNavigateToBilling;
        vm.NavigateToBillingRequested += OnDashboardNavigateToBilling;
        vm.NavigateToInventoryRequested -= OnDashboardNavigateToInventory;
        vm.NavigateToInventoryRequested += OnDashboardNavigateToInventory;
        vm.NavigateToPendingHandleRequested -= OnDashboardNavigateToPendingHandle;
        vm.NavigateToPendingHandleRequested += OnDashboardNavigateToPendingHandle;
        CurrentViewModel = vm;
        CurrentNavKey = "Dashboard";
        _ = vm.LoadDataCommand.ExecuteAsync(null);
    }

    private void OnDashboardNavigateToPrescription() => NavigateToPrescription();
    private void OnDashboardNavigateToPatient() => NavigateToPatient();
    private void OnDashboardNavigateToBilling() => NavigateToBilling();
    private void OnDashboardNavigateToInventory() => NavigateToInventory();

    /// <summary>从首页待办队列跳到收费页办理指定处方（按状态预填审核/收费/发药区）</summary>
    private async void OnDashboardNavigateToPendingHandle(Clinic.Application.DTOs.PrescriptionHistoryDto prescription)
    {
        var billingVm = _services.GetRequiredService<BillingViewModel>();
        CurrentViewModel = billingVm;
        CurrentNavKey = "Billing";
        await billingVm.ProcessPendingPrescription(prescription);
    }

    [RelayCommand]
    private void NavigateToPatient()
    {
        CurrentViewModel = _services.GetRequiredService<PatientManagementViewModel>();
        CurrentNavKey = "Patient";
    }

    [RelayCommand]
    private void NavigateToMedicalRecord()
    {
        CurrentViewModel = _services.GetRequiredService<MedicalRecordManagementViewModel>();
        CurrentNavKey = "MedicalRecord";
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
            CurrentNavKey = "Prescription";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NavigateToPrescription 异常");
            ErrorMessage = $"打开处方开具失败：{ex.Message}";
        }
    }

    private bool CanNavigateToPrescription() => CanPrescribe;

    /// <summary>从处方页面跳转到收费页面，并预填处方ID</summary>
    private async void OnNavigateToBillingRequested(long prescriptionId)
    {
        var billingVm = _services.GetRequiredService<BillingViewModel>();
        CurrentViewModel = billingVm;
        CurrentNavKey = "Billing";
        await billingVm.SetPendingPrescription(prescriptionId);
    }

    [RelayCommand]
    private void NavigateToBilling()
    {
        CurrentViewModel = _services.GetRequiredService<BillingViewModel>();
        CurrentNavKey = "Billing";
    }

    [RelayCommand]
    private void NavigateToInventory()
    {
        CurrentViewModel = _services.GetRequiredService<InventoryViewModel>();
        CurrentNavKey = "Inventory";
    }

    [RelayCommand]
    private void NavigateToPrescriptionHistory()
    {
        var vm = _services.GetRequiredService<PrescriptionHistoryViewModel>();
        // 订阅导航到收费的事件（每次创建新 VM 时订阅）
        vm.NavigateToBillingRequested -= OnNavigateToBillingRequested;
        vm.NavigateToBillingRequested += OnNavigateToBillingRequested;
        CurrentViewModel = vm;
        CurrentNavKey = "PrescriptionHistory";
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
            ToastService.Instance.Success($"备份成功：{Path.GetFileName(backupPath)}");
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

        var result = _dialogService.ShowDialog(
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

            _dialogService.ShowMessage(
                "数据恢复成功！应用将重新启动以加载恢复的数据。",
                "恢复完成");

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
    private async Task LogoutAsync()
    {
        // 退出登录时卸载大模型，释放内存
        try
        {
            if (_aiManager.IsModelLoaded)
            {
                await _aiManager.UnloadModelAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "登出时卸载AI模型失败，不影响登出流程");
        }
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
    public async Task RefreshPermissions()
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
            CurrentNavKey = "Patient";
        }

        // 登录后默认导航到 Dashboard 首页
        if (CurrentViewModel is null)
        {
            try
            {
                var vm = _services.GetRequiredService<DashboardViewModel>();
                vm.NavigateToPrescriptionRequested -= OnDashboardNavigateToPrescription;
                vm.NavigateToPrescriptionRequested += OnDashboardNavigateToPrescription;
                vm.NavigateToPatientRequested -= OnDashboardNavigateToPatient;
                vm.NavigateToPatientRequested += OnDashboardNavigateToPatient;
                vm.NavigateToBillingRequested -= OnDashboardNavigateToBilling;
                vm.NavigateToBillingRequested += OnDashboardNavigateToBilling;
                vm.NavigateToInventoryRequested -= OnDashboardNavigateToInventory;
                vm.NavigateToInventoryRequested += OnDashboardNavigateToInventory;
                vm.NavigateToPendingHandleRequested -= OnDashboardNavigateToPendingHandle;
                vm.NavigateToPendingHandleRequested += OnDashboardNavigateToPendingHandle;
                CurrentViewModel = vm;
                CurrentNavKey = "Dashboard";
                _ = vm.LoadDataCommand.ExecuteAsync(null);
            }
            catch
            {
                // Dashboard 初始化异常时回退到处方开具或患者管理
                if (CanPrescribe)
                {
                    try
                    {
                        var vm = _services.GetRequiredService<PrescriptionViewModel>();
                        vm.NavigateToBillingRequested -= OnNavigateToBillingRequested;
                        vm.NavigateToBillingRequested += OnNavigateToBillingRequested;
                        CurrentViewModel = vm;
                        CurrentNavKey = "Prescription";
                    }
                    catch
                    {
                        CurrentViewModel = _services.GetRequiredService<PatientManagementViewModel>();
                        CurrentNavKey = "Patient";
                    }
                }
                else
                {
                    CurrentViewModel = _services.GetRequiredService<PatientManagementViewModel>();
                    CurrentNavKey = "Patient";
                }
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
