namespace Clinic.Presentation.Services;

/// <summary>AI问诊Agent客户端接口</summary>
public interface IAiScribeClient
{
    /// <summary>Agent服务是否可用</summary>
    Task<bool> IsAvailableAsync();

    /// <summary>开始问诊录音</summary>
    Task<string> StartAsync(long patientId, long doctorId);

    /// <summary>停止问诊，返回转写文本</summary>
    Task<StopScribeResult> StopAsync(string sessionId);

    /// <summary>生成结构化病历</summary>
    Task<MedicalRecordDraftResult> GenerateAsync(string sessionId);

    /// <summary>获取会话状态</summary>
    Task<ScribeStatusResult> GetStatusAsync(string sessionId);

    /// <summary>取消问诊</summary>
    Task CancelAsync(string sessionId);
}

/// <summary>停止问诊结果</summary>
public class StopScribeResult
{
    public string SessionId { get; set; } = string.Empty;
    public double Duration { get; set; }
    public List<TranscriptSegmentResult> Transcript { get; set; } = new();
    public string FullText { get; set; } = string.Empty;
}

public class TranscriptSegmentResult
{
    public string Speaker { get; set; } = "unknown";
    public double Start { get; set; }
    public double End { get; set; }
    public string Text { get; set; } = string.Empty;
}

/// <summary>病历草稿结果</summary>
public class MedicalRecordDraftResult
{
    public string ChiefComplaint { get; set; } = string.Empty;
    public string PresentIllness { get; set; } = string.Empty;
    public string PastHistory { get; set; } = string.Empty;
    public string Allergies { get; set; } = string.Empty;
    public string Diagnosis { get; set; } = string.Empty;
    public string TreatmentPlan { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public List<string> Warnings { get; set; } = new();
}

/// <summary>会话状态结果</summary>
public class ScribeStatusResult
{
    public string SessionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double? Duration { get; set; }
    public string? ErrorMessage { get; set; }
}
