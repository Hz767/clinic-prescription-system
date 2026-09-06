using System.Collections.Concurrent;
using Clinic.AiScribe.Models;
using NAudio.Wave;

namespace Clinic.AiScribe.Services;

/// <summary>
/// 基于NAudio的录音服务实现
/// 输出WAV格式（16kHz, 16bit, mono），适合ASR识别
/// </summary>
public class AudioRecorder : IAudioRecorder, IDisposable
{
    private readonly ILogger<AudioRecorder> _logger;
    private readonly string _tempPath;
    private readonly ConcurrentDictionary<string, RecordingSession> _sessions = new();

    // 录音参数：16kHz, 16bit, mono（ASR标准格式）
    private const int SampleRate = 16000;
    private const int BitsPerSample = 16;
    private const int Channels = 1;

    public AudioRecorder(ILogger<AudioRecorder> logger, IConfiguration config)
    {
        _logger = logger;
        _tempPath = config["Scribe:AudioTempPath"] ?? "audio_temp";
        Directory.CreateDirectory(_tempPath);
    }

    public async Task StartAsync(string sessionId, CancellationToken ct = default)
    {
        if (_sessions.ContainsKey(sessionId))
            throw new InvalidOperationException($"会话 {sessionId} 已在录音中");

        var filePath = Path.Combine(_tempPath, $"{sessionId}.wav");
        var waveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels);
        var writer = new WaveFileWriter(filePath, waveFormat);
        var capture = new WaveInEvent
        {
            WaveFormat = waveFormat,
            BufferMilliseconds = 100
        };

        var session = new RecordingSession
        {
            SessionId = sessionId,
            FilePath = filePath,
            Writer = writer,
            Capture = capture,
            StartTime = DateTime.Now
        };

        capture.DataAvailable += (s, e) =>
        {
            try
            {
                writer.Write(e.Buffer, 0, e.BytesRecorded);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "写入录音数据失败");
            }
        };

        capture.RecordingStopped += (s, e) =>
        {
            try
            {
                writer.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关闭录音文件失败");
            }
        };

        capture.StartRecording();
        _sessions[sessionId] = session;
        _logger.LogInformation("开始录音，会话：{SessionId}，文件：{FilePath}", sessionId, filePath);

        await Task.CompletedTask;
    }

    public async Task<string> StopAsync(string sessionId, CancellationToken ct = default)
    {
        if (!_sessions.TryRemove(sessionId, out var session))
            throw new InvalidOperationException($"会话 {sessionId} 不存在或已停止");

        session.Capture.StopRecording();
        session.EndTime = DateTime.Now;

        // 等待录音停止事件完成
        await Task.Delay(200, ct);

        _logger.LogInformation("停止录音，会话：{SessionId}，时长：{Duration:F1}秒",
            sessionId, session.Duration);

        return session.FilePath;
    }

    public Task CancelAsync(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var session))
            return Task.CompletedTask;

        try
        {
            session.Capture.StopRecording();
            // 删除临时文件
            if (File.Exists(session.FilePath))
                File.Delete(session.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取消录音失败");
        }

        _logger.LogInformation("取消录音，会话：{SessionId}", sessionId);
        return Task.CompletedTask;
    }

    public double GetDuration(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
            return session.Duration;
        return 0;
    }

    public bool IsRecording(string sessionId) => _sessions.ContainsKey(sessionId);

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            try { session.Capture.StopRecording(); session.Writer.Dispose(); }
            catch { }
        }
        _sessions.Clear();
    }

    private class RecordingSession
    {
        public string SessionId { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public WaveFileWriter Writer { get; set; } = null!;
        public WaveInEvent Capture { get; set; } = null!;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double Duration => (EndTime ?? DateTime.Now).Subtract(StartTime).TotalSeconds;
    }
}
