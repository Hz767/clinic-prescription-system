using Clinic.AiScribe.Models;

namespace Clinic.AiScribe.Services;

/// <summary>说话人分离服务接口</summary>
public interface ISpeakerDiarizer
{
    /// <summary>对转写片段进行说话人标注</summary>
    List<TranscriptSegment> Diarize(List<TranscriptSegment> segments);
}

/// <summary>
/// 简化版说话人分离
/// MVP阶段：基于规则的简单分离
/// 策略：
/// 1. 问句通常是医生说的
/// 2. 症状描述通常是患者说的
/// 3. 连续短句交替出现
/// 后续可升级为声纹识别
/// </summary>
public class SimpleDiarizer : ISpeakerDiarizer
{
    private readonly ILogger<SimpleDiarizer> _logger;

    // 医生常用句式关键词
    private static readonly string[] DoctorPatterns = new[]
    {
        "怎么", "哪里", "什么", "多久", "有没有", "是否", "疼吗", "痒吗",
        "发烧", "咳嗽", "量过", "吃过", "用过", "以前", "家族", "过敏",
        "我给你", "建议", "需要", "应该", "可以", "先", "再", "复查"
    };

    // 患者常用句式关键词
    private static readonly string[] PatientPatterns = new[]
    {
        "我", "疼", "痒", "难受", "不舒服", "发烧", "咳嗽", "头晕", "恶心",
        "吃了", "用了", "去过", "检查", "结果", "医生说", "以前有", "没有",
        "好的", "谢谢", "知道了", "明白"
    };

    public SimpleDiarizer(ILogger<SimpleDiarizer> logger)
    {
        _logger = logger;
    }

    public List<TranscriptSegment> Diarize(List<TranscriptSegment> segments)
    {
        if (segments == null || segments.Count == 0)
            return segments ?? new List<TranscriptSegment>();

        var lastSpeaker = "unknown";
        foreach (var segment in segments)
        {
            if (segment.Speaker != "unknown")
            {
                lastSpeaker = segment.Speaker;
                continue;
            }

            var doctorScore = ScorePatterns(segment.Text, DoctorPatterns);
            var patientScore = ScorePatterns(segment.Text, PatientPatterns);

            // 问句倾向于医生
            if (segment.Text.EndsWith("?") || segment.Text.EndsWith("？"))
                doctorScore += 2;

            // 以"我"开头倾向于患者
            if (segment.Text.StartsWith("我"))
                patientScore += 2;

            if (doctorScore > patientScore)
            {
                segment.Speaker = "doctor";
                lastSpeaker = "doctor";
            }
            else if (patientScore > doctorScore)
            {
                segment.Speaker = "patient";
                lastSpeaker = "patient";
            }
            else
            {
                // 分数相同，交替假设
                segment.Speaker = lastSpeaker == "doctor" ? "patient" : "doctor";
                lastSpeaker = segment.Speaker;
            }
        }

        _logger.LogInformation("说话人分离完成，共 {Count} 段", segments.Count);
        return segments;
    }

    private int ScorePatterns(string text, string[] patterns)
    {
        var score = 0;
        foreach (var pattern in patterns)
        {
            if (text.Contains(pattern, StringComparison.Ordinal))
                score++;
        }
        return score;
    }
}
