using System.Diagnostics;
using System.IO;
using System.Windows;
using Clinic.Presentation.ViewModels;

namespace Clinic.Presentation;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closed += (_, _) =>
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "startup_debug.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] MainWindow Closed, StackTrace:\n{new StackTrace(true)}\n");
            System.Windows.Application.Current.Shutdown();
        };

        // 修复：Singleton 窗口在 Hide() 后再次 Show() 时可能出现 0x0 不可见的问题
        Loaded += async (_, _) =>
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "startup_debug.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] MainWindow Loaded\n");
            if (Width <= 0 || double.IsNaN(Width)) Width = 1100;
            if (Height <= 0 || double.IsNaN(Height)) Height = 700;
            Visibility = Visibility.Visible;
            WindowState = WindowState.Normal;

            // 初始化异步数据（AI状态等）
            if (DataContext is MainViewModel vm)
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] Calling InitializeAsync\n");
                await vm.InitializeAsync();
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] InitializeAsync done\n");
            }
        };
    }
}
