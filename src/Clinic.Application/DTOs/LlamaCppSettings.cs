namespace Clinic.Application.DTOs;

/// <summary>
/// llama.cpp 本地部署配置。
/// 在 appsettings.json 的 LlamaCpp 节配置，由 App.xaml.cs 读取后传入 DI。
/// </summary>
public sealed record LlamaCppSettings
{
    /// <summary>是否启用 llama.cpp 本地推理</summary>
    public bool Enabled { get; init; }

    /// <summary>llama-server.exe 可执行文件路径</summary>
    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>GGUF 模型文件路径</summary>
    public string ModelPath { get; init; } = string.Empty;

    /// <summary>监听地址（默认 127.0.0.1）</summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>监听端口（默认 8080）</summary>
    public int Port { get; init; } = 8080;

    /// <summary>上下文窗口大小</summary>
    public int CtxSize { get; init; } = 4096;

    /// <summary>卸载到 GPU 的层数（0 = 纯 CPU）</summary>
    public int NGpuLayers { get; init; }

    /// <summary>CPU 推理线程数</summary>
    public int Threads { get; init; } = 4;

    /// <summary>是否在应用启动时自动启动 llama-server</summary>
    public bool AutoStart { get; init; }

    /// <summary>启动后等待就绪的最大秒数</summary>
    public int StartupTimeoutSeconds { get; init; } = 30;

    /// <summary>健康检查轮询间隔（毫秒）</summary>
    public int HealthCheckIntervalMs { get; init; } = 500;
}