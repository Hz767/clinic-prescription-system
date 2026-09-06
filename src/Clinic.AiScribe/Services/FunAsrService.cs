using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Clinic.AiScribe.Models;

namespace Clinic.AiScribe.Services;

/// <summary>
/// 基于FunASR-llama.cpp的语音识别实现
/// 纯C++推理引擎，无需Python环境
/// 命令行：funasr-cli -m model.gguf -f audio.wav
/// </summary>
public class FunAsrService : IAsrService
{
    private readonly ILogger<FunAsrService> _logger;
    private readonly string _binaryPath;
    private readonly string _modelPath;
    private readonly string? _hotWordsPath;

    public bool IsAvailable => File.Exists(_binaryPath) && File.Exists(_modelPath);

    public FunAsrService(ILogger<FunAsrService> logger, IConfiguration config)
    {
        _logger = logger;
        _binaryPath = config["Asr:FunAsrBinaryPath"] ?? "tools/funasr-llama.cpp/funasr-cli.exe";
        _modelPath = config["Asr:ModelPath"] ?? "models/paraformer-zh.gguf";
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

        var args = BuildArgs(wavFilePath);
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

        var text = ParseOutput(output);
        _logger.LogInformation("ASR转写完成，长度：{Length}", text.Length);
        return text;
    }

    public async Task<List<TranscriptSegment>> TranscribeWithTimestampsAsync(string wavFilePath, CancellationToken ct = default)
    {
        // FunASR-llama.cpp当前版本可能不输出时间戳
        // MVP阶段：整段转写后按句号/问号/感叹号简单分段
        var fullText = await TranscribeAsync(wavFilePath, ct);
        return SimpleSegmentation(fullText);
    }

    private string BuildArgs(string wavFilePath)
    {
        var sb = new StringBuilder();
        sb.Append($"-m \"{_modelPath}\"");
        sb.Append($" -f \"{wavFilePath}\"");
        sb.Append(" -l zh"); // 中文
        if (!string.IsNullOrEmpty(_hotWordsPath) && File.Exists(_hotWordsPath))
            sb.Append($" --hot-words \"{_hotWordsPath}\"");
        return sb.ToString();
    }

    private string ParseOutput(string output)
    {
        // FunASR输出可能是纯文本或JSON，尝试解析JSON
        try
        {
            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.TryGetProperty("text", out var text))
                return text.GetString() ?? string.Empty;
        }
        catch
        {
            // 不是JSON，返回纯文本
        }
        return output.Trim();
    }

    private List<TranscriptSegment> SimpleSegmentation(string text)
    {
        var segments = new List<TranscriptSegment>();
        if (string.IsNullOrWhiteSpace(text))
            return segments;

        // 按句号、问号、感叹号分段
        var sentences = text.Split(new[] { '。', '？', '！', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries);
        double currentTime = 0;
        foreach (var sentence in sentences)
        {
            var trimmed = sentence.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            // 估算时长：中文约4字/秒
            var duration = trimmed.Length / 4.0;
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
