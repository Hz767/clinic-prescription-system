using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Clinic.Infrastructure.Llm;

/// <summary>
/// llama-server.exe 进程生命周期管理器。
/// 负责启动、监控、健康检查和优雅停止 llama.cpp 推理服务。
/// 
/// 启动流程：
///   1. 验证可执行文件和模型文件存在
///   2. 以独立进程启动 llama-server.exe
///   3. 轮询 GET /health 直到返回 200 或超时
///   4. 注册进程退出事件，异常退出时写日志
/// 
/// 停止流程：
///   1. 发送 SIGTERM / CloseMainWindow
///   2. 等待 5 秒优雅退出
///   3. 超时则 Kill
/// </summary>
public sealed class LlamaServerProcessManager : ILlamaServerManager
{
    private readonly LlamaCppSettings _settings;
    private readonly ILogger<LlamaServerProcessManager>? _logger;
    private Process? _process;
    private readonly object _lock = new();

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public string? Endpoint
    {
        get
        {
            if (!IsRunning) return null;
            return $"http://{_settings.Host}:{_settings.Port}";
        }
    }

    public LlamaServerProcessManager(LlamaCppSettings settings, ILogger<LlamaServerProcessManager>? logger = null)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<bool> StartAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_process is { HasExited: false })
            {
                _logger?.LogInformation("llama-server 已在运行中 (PID: {Pid})", _process.Id);
                return true;
            }
        }

        // 验证可执行文件
        if (!File.Exists(_settings.ExecutablePath))
        {
            _logger?.LogError("llama-server 可执行文件不存在: {Path}", _settings.ExecutablePath);
            return false;
        }

        // 验证模型文件
        if (!File.Exists(_settings.ModelPath))
        {
            _logger?.LogError("GGUF 模型文件不存在: {Path}", _settings.ModelPath);
            return false;
        }

        var args = BuildArgs();
        _logger?.LogInformation("启动 llama-server: {Exe} {Args}", _settings.ExecutablePath, args);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _settings.ExecutablePath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };

        // 捕获进程退出
        process.Exited += (_, _) =>
        {
            _logger?.LogWarning("llama-server 进程已退出 (退出码: {ExitCode})", process.ExitCode);
            lock (_lock)
            {
                if (_process == process)
                    _process = null;
            }
        };

        // 异步读取 stdout/stderr 避免缓冲区满导致阻塞
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _logger?.LogDebug("[llama-server] {Line}", e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _logger?.LogWarning("[llama-server:err] {Line}", e.Data);
        };

        try
        {
            if (!process.Start())
            {
                _logger?.LogError("llama-server 启动失败");
                process.Dispose();
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            lock (_lock)
            {
                _process?.Dispose();
                _process = process;
            }

            _logger?.LogInformation("llama-server 已启动 (PID: {Pid})，等待就绪...", process.Id);

            // 轮询等待服务就绪
            var endpoint = $"http://{_settings.Host}:{_settings.Port}";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

            var deadline = DateTime.UtcNow.AddSeconds(_settings.StartupTimeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                if (process.HasExited)
                {
                    _logger?.LogError("llama-server 在启动过程中异常退出 (退出码: {ExitCode})", process.ExitCode);
                    return false;
                }

                try
                {
                    var response = await http.GetAsync($"{endpoint}/health", ct);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger?.LogInformation("llama-server 就绪 ({Endpoint})", endpoint);
                        return true;
                    }
                }
                catch
                {
                    // 服务尚未就绪，继续等待
                }

                await Task.Delay(_settings.HealthCheckIntervalMs, ct);
            }

            _logger?.LogError("llama-server 启动超时（{Seconds}秒）", _settings.StartupTimeoutSeconds);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "llama-server 启动异常");
            lock (_lock)
            {
                if (_process == process)
                    _process = null;
            }
            process.Dispose();
            return false;
        }
    }

    public async Task StopAsync()
    {
        Process? process;
        lock (_lock)
        {
            process = _process;
            _process = null;
        }

        if (process is not { HasExited: false })
            return;

        _logger?.LogInformation("正在停止 llama-server (PID: {Pid})...", process.Id);

        try
        {
            // 先尝试优雅关闭
            if (!process.CloseMainWindow())
            {
                // CloseMainWindow 对控制台进程无效，直接 Kill
                process.Kill(entireProcessTree: true);
            }

            // 等待进程退出
            var exited = await Task.Run(() => process.WaitForExit(5000));
            if (!exited)
            {
                _logger?.LogWarning("llama-server 未在 5 秒内退出，强制终止");
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // 进程已退出
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "停止 llama-server 时发生异常");
        }
        finally
        {
            process.Dispose();
            _logger?.LogInformation("llama-server 已停止");
        }
    }

    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        if (!IsRunning) return false;

        var endpoint = Endpoint;
        if (endpoint is null) return false;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = await http.GetAsync($"{endpoint}/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        Process? process;
        lock (_lock)
        {
            process = _process;
            _process = null;
        }

        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { /* already exited */ }
            process.Dispose();
        }
    }

    /// <summary>构建 llama-server 命令行参数</summary>
    private string BuildArgs()
    {
        var parts = new List<string>
        {
            $"-m \"{_settings.ModelPath}\"",
            $"--host {_settings.Host}",
            $"--port {_settings.Port}",
            $"--ctx-size {_settings.CtxSize}",
            $"--n-gpu-layers {_settings.NGpuLayers}",
            $"--threads {_settings.Threads}"
        };

        return string.Join(" ", parts);
    }
}