namespace Clinic.Application.Interfaces;

/// <summary>
/// llama.cpp 服务器进程生命周期管理接口。
/// 负责启动/停止 llama-server.exe 进程，监控运行状态，提供健康检查。
/// </summary>
public interface ILlamaServerManager : IDisposable
{
    /// <summary>llama-server 是否正在运行</summary>
    bool IsRunning { get; }

    /// <summary>llama-server 的 API 端点地址（如 http://127.0.0.1:8080）</summary>
    string? Endpoint { get; }

    /// <summary>
    /// 启动 llama-server 进程并等待服务就绪。
    /// 如果 AutoStart 为 false，需手动调用此方法。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>启动成功返回 true，失败返回 false</returns>
    Task<bool> StartAsync(CancellationToken ct = default);

    /// <summary>优雅停止 llama-server 进程</summary>
    Task StopAsync();

    /// <summary>
    /// 检查 llama-server 是否已就绪可接受请求。
    /// </summary>
    Task<bool> HealthCheckAsync(CancellationToken ct = default);
}