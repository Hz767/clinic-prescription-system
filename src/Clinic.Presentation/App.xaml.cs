using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Clinic.Application;
using Clinic.Domain.Interfaces;
using Clinic.Infrastructure;
using Clinic.Infrastructure.Backup;
using Clinic.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Clinic.Presentation.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Clinic.Presentation;

/// <summary>
/// 应用程序入口。通过 Generic Host 配置 DI 容器。
/// 启动流程：加载配置 → 注册服务 → 初始化数据库 → 显示主窗口
/// </summary>
public partial class App : System.Windows.Application
{
    private readonly IHost _host;

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

                // 注册各层服务
                services.AddInfrastructure(dbPath, encryptionKey, pepper, llmEnabled, llmEndpoint, llmModel);
                services.AddApplication();

                // 注册 ViewModel 和 Window
                services.AddSingleton<MainViewModel>();
                services.AddTransient<PatientManagementViewModel>();
                services.AddTransient<PrescriptionViewModel>();
                services.AddTransient<BillingViewModel>();
                services.AddTransient<InventoryViewModel>();
                services.AddTransient<PrescriptionHistoryViewModel>();
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
                await DbSeeder.SeedAsync(db, passwordHasher, encryption);
            }

            ShowLoginWindow();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"应用启动失败: {ex}");
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
                System.Diagnostics.Debug.WriteLine($"[Migration] Added column Prescriptions.{name}");
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
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        var mainViewModel = (MainViewModel)mainWindow.DataContext;

        // 先取消旧订阅再添加新订阅，防止事件累积（H-13）
        // Lambda 无法取消订阅，改为命名方法
        mainViewModel.LogoutRequested -= OnLogoutRequested;
        mainViewModel.LogoutRequested += OnLogoutRequested;

        // 先显示窗口，再刷新权限（确保即使 RefreshPermissions 抛异常窗口仍可见）
        mainWindow.Show();
        mainWindow.Activate();

        try
        {
            // 刷新权限相关 UI 状态（MainViewModel 为 Singleton，跨登录会话复用）
            mainViewModel.RefreshPermissions();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RefreshPermissions 异常: {ex}");
            mainViewModel.ErrorMessage = $"初始化页面失败：{ex.Message}";
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
