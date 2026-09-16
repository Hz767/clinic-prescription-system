using System.Diagnostics;
using Clinic.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Clinic.Infrastructure.Llm;

/// <summary>
/// AI 辅助功能全局管理服务实现。
/// </summary>
public sealed class AiAssistantManager : IAiAssistantManager, IDisposable
{
    private readonly ILlamaServerManager? _llamaManager;
    private readonly ILlmService? _llmService;
    private readonly ILogger<AiAssistantManager>? _logger;
    private readonly long _requiredMemoryMb;

    private bool _isEnabled;
    private bool _isModelLoaded;
    private bool _isBusy;
    private string _statusMessage = "AI 辅助未启用";
    private SystemHealthCheckResult? _lastHealthCheck;

    public bool IsEnabled => _isEnabled;
    public bool IsModelLoaded => _isModelLoaded;
    public bool IsBusy => _isBusy;
    public string StatusMessage => _statusMessage;
    public SystemHealthCheckResult? LastHealthCheck => _lastHealthCheck;

    public event EventHandler? StateChanged;

    public AiAssistantManager(
        ILlamaServerManager? llamaManager = null,
        ILlmService? llmService = null,
        ILogger<AiAssistantManager>? logger = null,
        long requiredMemoryMb = 6144)
    {
        _llamaManager = llamaManager;
        _llmService = llmService;
        _logger = logger;
        _requiredMemoryMb = requiredMemoryMb;
    }

    public async Task<SystemHealthCheckResult> RunHealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            var (totalMb, availableMb) = GetMemoryInfo();
            var sufficient = availableMb >= _requiredMemoryMb;
            var message = sufficient
                ? $"内存充足（可用 {availableMb}MB / 需 {_requiredMemoryMb}MB）"
                : $"内存不足（可用 {availableMb}MB / 需 {_requiredMemoryMb}MB），大模型可能无法加载或导致系统卡顿";

            _lastHealthCheck = new SystemHealthCheckResult(
                IsHealthy: sufficient,
                TotalMemoryMb: totalMb,
                AvailableMemoryMb: availableMb,
                RequiredMemoryMb: _requiredMemoryMb,
                Message: message);

            _logger?.LogInformation("系统自检：{Message}", message);
            return _lastHealthCheck;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "系统自检失败");
            _lastHealthCheck = new SystemHealthCheckResult(
                IsHealthy: false,
                TotalMemoryMb: 0,
                AvailableMemoryMb: 0,
                RequiredMemoryMb: _requiredMemoryMb,
                Message: $"系统自检失败：{ex.Message}");
            return _lastHealthCheck;
        }
    }

    public async Task<bool> EnableAsync(CancellationToken ct = default)
    {
        if (_isBusy) return false;

        // 如果 llama-server 已经在运行（如手动启动或上次运行残留），
        // 直接复用，跳过内存检查（模型已加载，内存已占用）
        // 先检查管理器跟踪的进程，再通过 HTTP 健康检查检测外部启动的实例
        bool serverRunning = false;
        if (_llamaManager is not null)
        {
            if (_llamaManager.IsRunning)
            {
                serverRunning = true;
            }
            else
            {
                // 管理器没有跟踪到进程，但可能有外部启动的实例
                serverRunning = await _llamaManager.HealthCheckAsync(ct);
            }
        }

        if (serverRunning)
        {
            _isEnabled = true;
            _isModelLoaded = true;
            _statusMessage = "AI 辅助已就绪（检测到已加载的大模型）";
            _logger?.LogInformation("检测到 llama-server 已在运行，直接复用");
            OnStateChanged();
            return true;
        }

        _isBusy = true;
        _statusMessage = "正在执行系统自检...";
        OnStateChanged();

        try
        {
            var health = await RunHealthCheckAsync(ct);
            if (!health.MemorySufficient)
            {
                _statusMessage = $"AI 辅助未启用：{health.Message}";
                _isEnabled = false;
                OnStateChanged();
                return false;
            }

            _isEnabled = true;
            _statusMessage = "AI 辅助已启用，正在加载大模型...";
            OnStateChanged();

            var loaded = await LoadModelAsync(ct);
            if (loaded)
            {
                _statusMessage = "AI 辅助已就绪";
            }
            else
            {
                _statusMessage = "AI 辅助已启用，但大模型加载失败（可稍后手动加载）";
            }

            return true;
        }
        finally
        {
            _isBusy = false;
            OnStateChanged();
        }
    }

    public async Task DisableAsync()
    {
        if (_isBusy) return;

        _isBusy = true;
        _statusMessage = "正在卸载大模型...";
        OnStateChanged();

        try
        {
            if (_isModelLoaded && _llamaManager is not null)
            {
                await _llamaManager.StopAsync();
            }
            _isModelLoaded = false;
            _isEnabled = false;
            _statusMessage = "AI 辅助已禁用";
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "禁用 AI 辅助时出错");
            _statusMessage = $"禁用 AI 辅助时出错：{ex.Message}";
        }
        finally
        {
            _isBusy = false;
            OnStateChanged();
        }
    }

    public async Task<bool> LoadModelAsync(CancellationToken ct = default)
    {
        if (_isBusy) return false;
        if (_isModelLoaded) return true;

        _isBusy = true;
        _statusMessage = "正在执行系统自检...";
        OnStateChanged();

        try
        {
            var health = await RunHealthCheckAsync(ct);
            if (!health.MemorySufficient)
            {
                _statusMessage = $"大模型加载取消：{health.Message}";
                return false;
            }

            if (_llamaManager is null)
            {
                _statusMessage = "大模型加载失败：llama.cpp 未配置";
                return false;
            }

            _statusMessage = "正在启动 llama-server...";
            OnStateChanged();

            var started = await _llamaManager.StartAsync(ct);
            if (!started)
            {
                _statusMessage = "大模型加载失败：llama-server 启动失败";
                return false;
            }

            // 等待 LLM 服务就绪
            _statusMessage = "正在等待大模型就绪...";
            OnStateChanged();

            if (_llmService is not null)
            {
                var maxRetries = 10;
                for (int i = 0; i < maxRetries; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var status = await _llmService.GetStatusAsync(ct);
                    if (status.IsAvailable)
                    {
                        _isModelLoaded = true;
                        _statusMessage = "大模型已加载，AI 辅助就绪";
                        return true;
                    }
                    await Task.Delay(1000, ct);
                }
            }

            _isModelLoaded = true;
            _statusMessage = "大模型已加载";
            return true;
        }
        catch (OperationCanceledException)
        {
            _statusMessage = "大模型加载已取消";
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "加载大模型失败");
            _statusMessage = $"大模型加载失败：{ex.Message}";
            return false;
        }
        finally
        {
            _isBusy = false;
            OnStateChanged();
        }
    }

    public async Task UnloadModelAsync()
    {
        if (_isBusy) return;
        if (!_isModelLoaded) return;

        _isBusy = true;
        _statusMessage = "正在卸载大模型...";
        OnStateChanged();

        try
        {
            if (_llamaManager is not null)
            {
                await _llamaManager.StopAsync();
            }
            _isModelLoaded = false;
            _statusMessage = _isEnabled ? "大模型已卸载（AI 辅助仍启用，可重新加载）" : "大模型已卸载";
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "卸载大模型时出错");
            _statusMessage = $"卸载大模型时出错：{ex.Message}";
        }
        finally
        {
            _isBusy = false;
            OnStateChanged();
        }
    }

    public async Task RefreshStatusAsync()
    {
        try
        {
            if (_llamaManager is not null)
            {
                _isModelLoaded = _llamaManager.IsRunning;
                // 如果 llama-server 已经在运行，自动启用 AI 辅助（识别已加载的大模型）
                if (_isModelLoaded)
                {
                    _isEnabled = true;
                }
            }

            if (_llmService is not null && _isModelLoaded)
            {
                var status = await _llmService.GetStatusAsync();
                if (!status.IsAvailable)
                {
                    _statusMessage = "大模型已启动但未就绪";
                }
                else if (_isEnabled)
                {
                    _statusMessage = "AI 辅助已就绪";
                }
            }
            else if (!_isEnabled)
            {
                _statusMessage = "AI 辅助未启用";
            }
            // 注意：不在此触发 OnStateChanged，避免与订阅方形成递归调用
            // （订阅方收到事件后会主动调用 RefreshStatusAsync 刷新 UI）
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "刷新 LLM 状态失败");
        }
    }

    private void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static (long TotalMb, long AvailableMb) GetMemoryInfo()
    {
        try
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            if (GlobalMemoryStatusEx(ref mem))
            {
                return ((long)(mem.ullTotalPhys / 1024 / 1024), (long)(mem.ullAvailPhys / 1024 / 1024));
            }
        }
        catch { }
        // 回退：使用 GC 内存信息（近似值）
        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            return ((long)(gcInfo.TotalAvailableMemoryBytes / 1024 / 1024), (long)(gcInfo.TotalAvailableMemoryBytes / 1024 / 1024));
        }
        catch
        {
            return (0, 0);
        }
    }

    public void Dispose()
    {
        // 不停止 llama-server，由 App.xaml.cs 的 OnExit 处理
    }
}
