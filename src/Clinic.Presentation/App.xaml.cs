using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Clinic.Application;
using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Clinic.Domain.Interfaces;
using Clinic.Infrastructure;
using Clinic.Infrastructure.Backup;
using Clinic.Infrastructure.Data;
using Clinic.Infrastructure.Llm;
using Microsoft.EntityFrameworkCore;
using Clinic.Presentation.ViewModels;
using Clinic.Presentation.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clinic.Presentation;

/// <summary>
/// 应用程序入口。通过 Generic Host 配置 DI 容器。
/// 启动流程：加载配置 → 注册服务 → 初始化数据库 → 显示主窗口
/// </summary>
public partial class App : System.Windows.Application
{
    private readonly IHost _host;
    private static ILogger<App>? _logger;

    /// <summary>懒加载的Logger（_host构建完成后可用）</summary>
    private static ILogger<App> Logger => _logger ??= ((App)Current)._host.Services.GetRequiredService<ILogger<App>>();

    /// <summary>全局服务提供者（供 View 层 code-behind 获取服务）</summary>
    public static IServiceProvider Services => ((App)Current)._host.Services;

    public App()
    {
        // 全局异常处理：捕获未处理异常并写入日志，防止程序静默崩溃
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                // 确保从 exe 所在目录加载 appsettings.json
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                // 读取配置
                var dbPath = context.Configuration["Database:Path"] ?? "clinic.db";
                var encryptionKey = context.Configuration["Encryption:EncryptionKey"]
                    ?? context.Configuration["Security:EncryptionKey"];
                var pepper = context.Configuration["Encryption:Pepper"];
                var llmEnabled = bool.TryParse(context.Configuration["Llm:Enabled"], out var le) && le;
                var llmEndpoint = context.Configuration["Llm:Endpoint"];
                var llmModel = context.Configuration["Llm:Model"];

                // 读取 llama.cpp 配置
                var llamaCppSection = context.Configuration.GetSection("LlamaCpp");
                var llamaCppEnabled = llamaCppSection.GetValue<bool>("Enabled");
                var llamaCppSettings = llamaCppEnabled
                    ? new LlamaCppSettings
                    {
                        Enabled = true,
                        ExecutablePath = llamaCppSection["ExecutablePath"] ?? "",
                        ModelPath = llamaCppSection["ModelPath"] ?? "",
                        Host = llamaCppSection["Host"] ?? "127.0.0.1",
                        Port = llamaCppSection.GetValue<int>("Port", 8080),
                        CtxSize = llamaCppSection.GetValue<int>("CtxSize", 4096),
                        NGpuLayers = llamaCppSection.GetValue<int>("NGpuLayers", 0),
                        Threads = llamaCppSection.GetValue<int>("Threads", 4),
                        AutoStart = llamaCppSection.GetValue<bool>("AutoStart"),
                        StartupTimeoutSeconds = llamaCppSection.GetValue<int>("StartupTimeoutSeconds", 30),
                        HealthCheckIntervalMs = llamaCppSection.GetValue<int>("HealthCheckIntervalMs", 500)
                    }
                    : null;

                // 注册各层服务
                services.AddInfrastructure(dbPath, encryptionKey, pepper, llmEnabled, llmEndpoint, llmModel, llamaCppSettings);
                services.AddApplication();

                // 注册 AI 辅助管理器（全局单例）
                services.AddSingleton<IAiAssistantManager, AiAssistantManager>();

                // 注册对话框服务
                services.AddSingleton<IDialogService, DialogService>();

                // 注册窗口服务
                services.AddSingleton<IWindowService, WindowService>();

                // 注册 ViewModel 和 Window
                services.AddSingleton<MainViewModel>();
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<PatientManagementViewModel>();
                services.AddTransient<PrescriptionViewModel>();
                services.AddTransient<BillingViewModel>();
                services.AddTransient<InventoryViewModel>();
                services.AddTransient<PrescriptionHistoryViewModel>();
                services.AddTransient<MedicalRecordManagementViewModel>();
                services.AddTransient<LoginViewModel>();
                services.AddSingleton<MainWindow>();
                services.AddTransient<LoginWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            await _host.StartAsync();

            // 初始化数据库 + 种子数据
            using (var scope = _host.Services.CreateScope())
            {
                var sp = scope.ServiceProvider;

                // 检查并执行待恢复操作（恢复备份后重启时触发）
                var config = sp.GetRequiredService<IConfiguration>();
                var dbPath = config["Database:Path"] ?? "clinic.db";
                if (BackupService.TryExecutePendingRestore(dbPath))
                {
                    // 恢复成功，记录系统日志
                    // （数据库已替换，后续 EnsureCreated 会使用恢复的数据库）
                }

                var db = sp.GetRequiredService<ClinicDbContext>();
                var passwordHasher = sp.GetRequiredService<IPasswordHasher>();
                var encryption = sp.GetRequiredService<IEncryptionService>();

                await db.Database.EnsureCreatedAsync();
                await MigratePrescriptionVitalsAsync(db);
                await MigrateP0P1ColumnsAsync(db);
                await DbSeeder.SeedAsync(db, passwordHasher, encryption);

                // 注意：不再自动启动 llama.cpp，改为用户在登录时选择是否启用 AI 辅助
                // （避免低配置电脑因强行加载大模型导致系统卡顿或进程中断）
                // 如果需要恢复自动启动，可在登录后通过主界面"加载大模型"按钮手动加载
            }

            ShowLoginWindow();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "应用启动失败");
            MessageBox.Show($"应用启动失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        base.OnStartup(e);
    }

    /// <summary>
    /// 手动迁移：为 Prescriptions 表添加体征字段（Weight/Temperature/SystolicBP/DiastolicBP/HeartRate）。
    /// EF Core EnsureCreated 不会自动迁移已存在的数据库，需手动 ALTER TABLE。
    /// 体征数据从 Patient 实体迁移到 Prescription 实体（改为就诊实时变量）。
    /// </summary>
    private static async Task MigratePrescriptionVitalsAsync(ClinicDbContext db)
    {
        var columnsToAdd = new (string Name, string Type)[]
        {
            ("Weight", "TEXT"),
            ("Temperature", "TEXT"),
            ("SystolicBP", "INTEGER"),
            ("DiastolicBP", "INTEGER"),
            ("HeartRate", "INTEGER")
        };

        foreach (var (name, type) in columnsToAdd)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE Prescriptions ADD COLUMN {name} {type};");
                Logger.LogInformation("[Migration] Added column Prescriptions.{name}", name);
            }
            catch
            {
                // 列已存在或表不存在，忽略错误
            }
        }
    }

    /// <summary>
    /// 手动迁移：P0-P1 新增列。
    /// - DrugMasters.ReorderLevel：补货阈值（低库存预警）
    /// - Patients.Tags：自定义标签（列表筛选）
    /// - Prescriptions.OverrideReason：临床覆盖理由（阻断项放行时记录）
    /// - Prescriptions.DispensedAt / DispensedBy：发药时间与操作人
    /// </summary>
    private static async Task MigrateP0P1ColumnsAsync(ClinicDbContext db)
    {
        var migrations = new (string Table, string Column, string Type)[]
        {
            ("DrugMasters", "ReorderLevel", "REAL"),
            ("Patients", "Tags", "TEXT"),
            ("Prescriptions", "OverrideReason", "TEXT"),
            ("Prescriptions", "DispensedAt", "TEXT"),
            ("Prescriptions", "DispensedBy", "INTEGER")
        };

        foreach (var (table, column, type) in migrations)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE {table} ADD COLUMN {column} {type};");
                Logger.LogInformation("[Migration] Added column {table}.{column}", table, column);
            }
            catch
            {
                // 列已存在或表不存在，忽略错误
            }
        }
    }

    /// <summary>显示登录窗口，登录成功后切换到主窗口</summary>
    private void ShowLoginWindow()
    {
        var loginWindow = _host.Services.GetRequiredService<LoginWindow>();
        var loginViewModel = (LoginViewModel)loginWindow.DataContext;
        loginViewModel.LoginSucceeded += () =>
        {
            loginWindow.Close();
            ShowMainWindow();
        };
        loginWindow.Show();
    }

    /// <summary>显示主窗口，退出登录时返回登录窗口</summary>
    private void ShowMainWindow()
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "startup_debug.log");
        try
        {
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] ShowMainWindow start\n");
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] MainWindow created, IsLoaded={mainWindow.IsLoaded}\n");
            var mainViewModel = (MainViewModel)mainWindow.DataContext;
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] DataContext got\n");

            // 先取消旧订阅再添加新订阅，防止事件累积（H-13）
            mainViewModel.LogoutRequested -= OnLogoutRequested;
            mainViewModel.LogoutRequested += OnLogoutRequested;

            // 修复：Singleton 窗口可能被 Hide() 过，需强制重置可见性和大小
            mainWindow.Visibility = Visibility.Visible;
            mainWindow.WindowState = WindowState.Normal;
            if (mainWindow.Width <= 0) mainWindow.Width = 1100;
            if (mainWindow.Height <= 0) mainWindow.Height = 700;
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] Before Show, Vis={mainWindow.Visibility}, W={mainWindow.Width}, H={mainWindow.Height}\n");
            mainWindow.Show();
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] After Show, IsLoaded={mainWindow.IsLoaded}\n");
            mainWindow.Activate();
            mainWindow.Focus();

            try
            {
                // 刷新权限相关 UI 状态（MainViewModel 为 Singleton，跨登录会话复用）
                _ = mainViewModel.RefreshPermissions();
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] RefreshPermissions done\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] RefreshPermissions 异常: {ex}\n");
                mainViewModel.ErrorMessage = $"初始化页面失败：{ex.Message}";
            }
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] ShowMainWindow done\n");
        }
        catch (Exception ex)
        {
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] ShowMainWindow 异常: {ex}\n");
            MessageBox.Show($"打开主窗口失败：{ex.Message}\n\n{ex.StackTrace}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>退出登录回调：隐藏主窗口并显示登录窗口（H-13）</summary>
    private void OnLogoutRequested()
    {
        // MainWindow 为 Singleton，GetRequiredService 返回同一实例
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Hide();
        ShowLoginWindow();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        // 停止 llama.cpp 推理服务（仅在启用时注册了 ILlamaServerManager）
        try
        {
            var llamaManager = _host.Services.GetService<ILlamaServerManager>();
            if (llamaManager is not null)
            {
                await llamaManager.StopAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[llama.cpp] 停止异常");
        }

        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }

    /// <summary>UI线程未处理异常：写入日志文件并显示错误对话框</summary>
    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
        var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UI线程异常\n{e.Exception}\n\n";
        File.AppendAllText(logPath, msg);
        MessageBox.Show($"程序发生异常：\n\n{e.Exception.Message}\n\n详细信息已写入 crash.log", "异常",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    /// <summary>非UI线程未处理异常：写入日志文件</summary>
    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
        var ex = e.ExceptionObject as Exception;
        var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 非UI线程异常 (IsTerminating={e.IsTerminating})\n{ex}\n\n";
        File.AppendAllText(logPath, msg);
    }

    /// <summary>未观察的Task异常：写入日志文件</summary>
    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
        var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Task未观察异常\n{e.Exception}\n\n";
        File.AppendAllText(logPath, msg);
        e.SetObserved();
    }
}
