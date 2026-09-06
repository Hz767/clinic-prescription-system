using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Clinic.AiScribe.Models;

namespace Clinic.AiScribe.Services;

/// <summary>
/// 基于FunASR-llama.cpp的语音识别实现
/// 纯C++推理引擎，无需Python环境
/// 命令行：llama-funasr-paraformer -m model.gguf -a audio.wav --vad fsmn-vad.gguf
/// </summary>
public class FunAsrService : IAsrService
{
    private readonly ILogger<FunAsrService> _logger;
    private readonly string _binaryPath;
    private readonly string _modelPath;
    private readonly string _vadPath;
    private readonly string? _hotWordsPath;

    public bool IsAvailable => File.Exists(_binaryPath) && File.Exists(_modelPath);

    public FunAsrService(ILogger<FunAsrService> logger, IConfiguration config)
    {
        _logger = logger;
        _binaryPath = config["Asr:FunAsrBinaryPath"] ?? "tools/funasr-llama.cpp/llama-funasr-paraformer.exe";
        _modelPath = config["Asr:ModelPath"] ?? "models/paraformer-q8.gguf";
        _vadPath = config["Asr:VadPath"] ?? "models/fsmn-vad.gguf";
        _hotWordsPath = config["Asr:HotWordsPath"];
    }

    public async Task<string> TranscribeAsync(string wavFilePath, CancellationToken ct = default)
    {
        if (!IsAvailable)
            throw new InvalidOperationException(
                $"ASR不可用：二进制={File.Exists(_binaryPath)}，模型={File.Exists(_modelPath)}。" +
                $"请下载FunASR-llama.cpp和Paraformer中文模型。");

        if (!File.Exists(wavFilePath))
            throw new FileNotFoundException($"音频文件不存在：{wavFilePath}");

        var args = BuildArgs(wavFilePath, withTimestamps: false);
        _logger.LogInformation("开始ASR转写：{File}", Path.GetFileName(wavFilePath));

        var psi = new ProcessStartInfo
        {
            FileName = _binaryPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动ASR进程");
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            _logger.LogError("ASR进程退出码 {Code}，错误：{Error}", process.ExitCode, error);
            throw new InvalidOperationException($"ASR转写失败：{error}");
        }

        var text = output.Trim();
        _logger.LogInformation("ASR转写完成，长度：{Length}", text.Length);
        return text;
    }

    public async Task<List<TranscriptSegment>> TranscribeWithTimestampsAsync(string wavFilePath, CancellationToken ct = default)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("ASR不可用");

        if (!File.Exists(wavFilePath))
            throw new FileNotFoundException($"音频文件不存在：{wavFilePath}");

        // 使用--srt参数获取带时间戳的输出
        var args = BuildArgs(wavFilePath, withTimestamps: true);
        _logger.LogInformation("开始ASR转写（带时间戳）：{File}", Path.GetFileName(wavFilePath));

        var psi = new ProcessStartInfo
        {
            FileName = _binaryPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动ASR进程");
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            _logger.LogError("ASR进程退出码 {Code}，错误：{Error}", process.ExitCode, error);
            // 降级为无时间戳转写
            _logger.LogWarning("降级为无时间戳转写");
            var text = await TranscribeAsync(wavFilePath, ct);
            return SimpleSegmentation(text);
        }

        var segments = ParseSrtOutput(output);
        if (segments.Count == 0)
        {
            // SRT解析失败，降级为简单分段
            var text = output.Trim();
            segments = SimpleSegmentation(text);
        }

        _logger.LogInformation("ASR转写完成，共 {Count} 段", segments.Count);
        return segments;
    }

    private string BuildArgs(string wavFilePath, bool withTimestamps)
    {
        var sb = new StringBuilder();
        sb.Append($"-m \"{_modelPath}\"");
        sb.Append($" -a \"{wavFilePath}\"");

        // VAD（语音活动检测），用于长音频分段
        if (File.Exists(_vadPath))
            sb.Append($" --vad \"{_vadPath}\"");

        // 时间戳输出
        if (withTimestamps)
            sb.Append(" --srt");

        return sb.ToString();
    }

    /// <summary>解析SRT格式输出</summary>
    private List<TranscriptSegment> ParseSrtOutput(string output)
    {
        var segments = new List<TranscriptSegment>();
        // SRT格式：序号 / 时间线 / 文本 / 空行
        var blocks = Regex.Split(output.Trim(), @"\n\s*\n");

        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3) continue;

            // 时间线格式：00:00:01,000 --> 00:00:03,500
            var timeMatch = Regex.Match(lines[1],
                @"(\d{2}):(\d{2}):(\d{2})[,.](\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2})[,.](\d{3})");
            if (!timeMatch.Success) continue;

            var start = int.Parse(timeMatch.Groups[1].Value) * 3600
                      + int.Parse(timeMatch.Groups[2].Value) * 60
                      + int.Parse(timeMatch.Groups[3].Value)
                      + int.Parse(timeMatch.Groups[4].Value) / 1000.0;
            var end = int.Parse(timeMatch.Groups[5].Value) * 3600
                    + int.Parse(timeMatch.Groups[6].Value) * 60
                    + int.Parse(timeMatch.Groups[7].Value)
                    + int.Parse(timeMatch.Groups[8].Value) / 1000.0;

            var text = string.Join(" ", lines.Skip(2)).Trim();
            if (string.IsNullOrEmpty(text)) continue;

            segments.Add(new TranscriptSegment
            {
                Speaker = "unknown",
                Start = start,
                End = end,
                Text = text
            });
        }

        return segments;
    }

    /// <summary>简单分段（无时间戳时的降级方案）</summary>
    private List<TranscriptSegment> SimpleSegmentation(string text)
    {
        var segments = new List<TranscriptSegment>();
        if (string.IsNullOrWhiteSpace(text))
            return segments;

        var sentences = text.Split(new[] { '。', '？', '！', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries);
        double currentTime = 0;
        foreach (var sentence in sentences)
        {
            var trimmed = sentence.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            var duration = trimmed.Length / 4.0; // 中文约4字/秒
            segments.Add(new TranscriptSegment
            {
                Speaker = "unknown",
                Start = currentTime,
                End = currentTime + duration,
                Text = trimmed
            });
            currentTime += duration;
        }
        return segments;
    }
}
