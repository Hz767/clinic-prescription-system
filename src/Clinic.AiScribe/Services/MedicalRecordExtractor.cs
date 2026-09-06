using System.Text.Json;
using System.Text.RegularExpressions;
using Clinic.AiScribe.Models;

namespace Clinic.AiScribe.Services;

/// <summary>病历结构化提取服务</summary>
public class MedicalRecordExtractor
{
    private readonly ILogger<MedicalRecordExtractor> _logger;
    private readonly ILlmService _llmService;

    public MedicalRecordExtractor(ILogger<MedicalRecordExtractor> logger, ILlmService llmService)
    {
        _logger = logger;
        _llmService = llmService;
    }

    /// <summary>从转写文本提取结构化病历</summary>
    public async Task<MedicalRecordDraft> ExtractAsync(List<TranscriptSegment> transcript, CancellationToken ct = default)
    {
        var fullText = FormatTranscript(transcript);
        _logger.LogInformation("开始病历提取，对话长度：{Length}", fullText.Length);

        try
        {
            var systemPrompt = BuildSystemPrompt();
            var userMessage = $"【医患对话】\n{fullText}\n\n请提取结构化病历，输出严格的JSON格式。";

            var response = await _llmService.ChatAsync(systemPrompt, userMessage, ct);
            var draft = ParseJsonResponse(response);

            // 后处理：体征数值提取和验证
            PostProcess(draft, fullText);

            draft.Confidence = CalculateConfidence(draft);
            _logger.LogInformation("病历提取完成，诊断：{Diagnosis}，置信度：{Confidence:F2}",
                draft.Diagnosis, draft.Confidence);

            return draft;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "病历提取失败");
            return new MedicalRecordDraft
            {
                ChiefComplaint = "",
                PresentIllness = fullText,
                Warnings = new List<string> { $"AI提取失败：{ex.Message}，请手动填写" },
                Confidence = 0
            };
        }
    }

    private string FormatTranscript(List<TranscriptSegment> segments)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var seg in segments)
        {
            var speaker = seg.Speaker switch
            {
                "doctor" => "医生",
                "patient" => "患者",
                _ => "未知"
            };
            sb.AppendLine($"{speaker}：{seg.Text}");
        }
        return sb.ToString();
    }

    private string BuildSystemPrompt()
    {
        return @"你是一位专业的门诊病历整理助手。请根据医患对话，提取结构化的门诊病历信息。

【规则】
1. 只提取对话中明确提到的信息，绝对不要编造
2. 对话中未提及的字段必须为null或空字符串
3. 体征数值（体温、血压、心率）必须是对话中明确说出的数字
4. 诊断应使用规范的医学术语
5. 输出严格的JSON格式，不要有任何其他文字、解释或markdown标记
6. 体温单位是摄氏度，血压单位是mmHg，心率单位是次/分

【输出格式】
{
  ""chiefComplaint"": ""主诉（主要症状+持续时间）"",
  ""presentIllness"": ""现病史（起病情况、症状特点、诊疗经过）"",
  ""pastHistory"": ""既往史"",
  ""allergies"": ""过敏史"",
  ""personalHistory"": ""个人史"",
  ""familyHistory"": ""家族史"",
  ""vitals"": {
    ""temperature"": null,
    ""systolicBP"": null,
    ""diastolicBP"": null,
    ""heartRate"": null,
    ""weight"": null
  },
  ""physicalExam"": ""体格检查"",
  ""diagnosis"": ""初步诊断"",
  ""treatmentPlan"": ""治疗建议和医嘱"",
  ""medicationSuggestions"": [""药品1"", ""药品2""]
}";
    }

    private MedicalRecordDraft ParseJsonResponse(string response)
    {
        // 尝试提取JSON（LLM可能输出markdown代码块）
        var json = ExtractJson(response);
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
            };
            var draft = JsonSerializer.Deserialize<MedicalRecordDraft>(json, options);
            return draft ?? new MedicalRecordDraft();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON解析失败，原始响应：{Response}", response.Substring(0, Math.Min(200, response.Length)));
            return new MedicalRecordDraft
            {
                PresentIllness = response,
                Warnings = new List<string> { "JSON解析失败，原始内容已放入现病史，请手动整理" }
            };
        }
    }

    private string ExtractJson(string response)
    {
        // 尝试从markdown代码块中提取
        var match = Regex.Match(response, @"```(?:json)?\s*(\{.*?\})\s*```", RegexOptions.Singleline);
        if (match.Success)
            return match.Groups[1].Value;

        // 尝试找到第一个{和最后一个}
        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        if (start >= 0 && end > start)
            return response.Substring(start, end - start + 1);

        return response;
    }

    private void PostProcess(MedicalRecordDraft draft, string fullText)
    {
        // 体征数值正则提取（双重验证）
        if (draft.Vitals == null)
            draft.Vitals = new Vitals();

        // 体温：36.5度 / 36.5摄氏度 / 体温36.5
        var tempMatch = Regex.Match(fullText, @"体温?\s*[:：]?\s*(\d{2}\.?\d?)\s*(?:度|摄氏度|℃)?");
        if (tempMatch.Success && decimal.TryParse(tempMatch.Groups[1].Value, out var temp))
        {
            if (temp >= 35 && temp <= 42)
                draft.Vitals.Temperature = temp;
        }

        // 血压：120/80 / 血压120 80
        var bpMatch = Regex.Match(fullText, @"血压?\s*[:：]?\s*(\d{2,3})\s*[/\s]\s*(\d{2,3})");
        if (bpMatch.Success &&
            int.TryParse(bpMatch.Groups[1].Value, out var sys) &&
            int.TryParse(bpMatch.Groups[2].Value, out var dia))
        {
            if (sys >= 60 && sys <= 250 && dia >= 40 && dia <= 150)
            {
                draft.Vitals.SystolicBP = sys;
                draft.Vitals.DiastolicBP = dia;
            }
        }

        // 心率：心率78 / 脉搏78
        var hrMatch = Regex.Match(fullText, @"(?:心率|脉搏)\s*[:：]?\s*(\d{2,3})");
        if (hrMatch.Success && int.TryParse(hrMatch.Groups[1].Value, out var hr))
        {
            if (hr >= 40 && hr <= 200)
                draft.Vitals.HeartRate = hr;
        }

        // 添加警告
        if (string.IsNullOrEmpty(draft.Diagnosis))
            draft.Warnings.Add("未提取到明确诊断，请医生确认");
        if (string.IsNullOrEmpty(draft.ChiefComplaint))
            draft.Warnings.Add("未提取到明确主诉，请医生确认");
    }

    private double CalculateConfidence(MedicalRecordDraft draft)
    {
        var score = 0.0;
        var total = 6.0;

        if (!string.IsNullOrEmpty(draft.ChiefComplaint)) score++;
        if (!string.IsNullOrEmpty(draft.PresentIllness)) score++;
        if (!string.IsNullOrEmpty(draft.Diagnosis)) score++;
        if (!string.IsNullOrEmpty(draft.TreatmentPlan)) score++;
        if (draft.Vitals != null && (draft.Vitals.Temperature.HasValue || draft.Vitals.SystolicBP.HasValue)) score++;
        if (draft.Warnings.Count == 0) score++;

        return score / total;
    }
}
