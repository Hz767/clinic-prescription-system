using Clinic.AiScribe.Models;

namespace Clinic.AiScribe.Services;

/// <summary>录音服务接口</summary>
public interface IAudioRecorder
{
    /// <summary>开始录音</summary>
    Task StartAsync(string sessionId, CancellationToken ct = default);

    /// <summary>停止录音，返回WAV文件路径</summary>
    Task<string> StopAsync(string sessionId, CancellationToken ct = default);

    /// <summary>取消录音</summary>
    Task CancelAsync(string sessionId);

    /// <summary>获取当前录音时长（秒）</summary>
    double GetDuration(string sessionId);

    /// <summary>是否正在录音</summary>
    bool IsRecording(string sessionId);
}
