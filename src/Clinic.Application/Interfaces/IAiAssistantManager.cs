namespace Clinic.Application.Interfaces;

/// <summary>
/// 系统自检结果。
/// </summary>
public record SystemHealthCheckResult(
    bool IsHealthy,
    long TotalMemoryMb,
    long AvailableMemoryMb,
    long RequiredMemoryMb,
    string? Message)
{
    /// <summary>内存是否足够加载大模型</summary>
    public bool MemorySufficient => AvailableMemoryMb >= RequiredMemoryMb;
}

/// <summary>
/// AI 辅助功能全局管理服务。
/// 负责：
/// 1. 系统自检（内存/CPU/磁盘）
/// 2. AI 辅助功能启用/禁用状态管理
/// 3. 本地大模型加载/卸载（通过 ILlamaServerManager）
/// 4. 状态变化通知
/// </summary>
public interface IAiAssistantManager
{
    /// <summary>AI 辅助功能是否已启用（用户在登录时选择）</summary>
    bool IsEnabled { get; }

    /// <summary>大模型是否已加载并可用</summary>
    bool IsModelLoaded { get; }

    /// <summary>是否正在加载/卸载</summary>
    bool IsBusy { get; }

    /// <summary>当前状态描述</summary>
    string StatusMessage { get; }

    /// <summary>上次系统自检结果</summary>
    SystemHealthCheckResult? LastHealthCheck { get; }

    /// <summary>状态变化时触发</summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// 执行系统自检，检查内存是否足够加载大模型。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>自检结果</returns>
    Task<SystemHealthCheckResult> RunHealthCheckAsync(CancellationToken ct = default);

    /// <summary>
    /// 启用 AI 辅助功能（登录时调用）。
    /// 会先执行系统自检，如果内存不足则不启用。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否成功启用</returns>
    Task<bool> EnableAsync(CancellationToken ct = default);

    /// <summary>
    /// 禁用 AI 辅助功能并卸载大模型。
    /// </summary>
    Task DisableAsync();

    /// <summary>
    /// 加载本地大模型。
    /// 会先执行系统自检，如果内存不足则不加载。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否成功加载</returns>
    Task<bool> LoadModelAsync(CancellationToken ct = default);

    /// <summary>
    /// 卸载本地大模型（停止 llama-server 进程）。
    /// </summary>
    Task UnloadModelAsync();

    /// <summary>
    /// 刷新 LLM 服务状态（检查 llama-server 是否可达）。
    /// </summary>
    Task RefreshStatusAsync();
}
