using Clinic.AiScribe.Models;

namespace Clinic.AiScribe.Services;

/// <summary>语音识别服务接口</summary>
public interface IAsrService
{
    /// <summary>是否可用（模型文件存在）</summary>
    bool IsAvailable { get; }

    /// <summary>将WAV文件转写为文本</summary>
    Task<string> TranscribeAsync(string wavFilePath, CancellationToken ct = default);

    /// <summary>将WAV文件转写为带时间戳的片段</summary>
    Task<List<TranscriptSegment>> TranscribeWithTimestampsAsync(string wavFilePath, CancellationToken ct = default);
}
