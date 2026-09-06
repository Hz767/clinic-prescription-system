namespace Clinic.AiScribe.Models;

/// <summary>转写片段，包含说话人标签和时间戳</summary>
public class TranscriptSegment
{
    public string Speaker { get; set; } = "unknown"; // doctor / patient / unknown
    public double Start { get; set; } // 开始时间（秒）
    public double End { get; set; }   // 结束时间（秒）
    public string Text { get; set; } = string.Empty;
    public double? AvgVolumeDb { get; set; } // 平均音量（分贝），用于说话人分离
}

/// <summary>生命体征</summary>
public class Vitals
{
    public decimal? Temperature { get; set; }    // 体温（摄氏度）
    public int? SystolicBP { get; set; }        // 收缩压
    public int? DiastolicBP { get; set; }       // 舒张压
    public int? HeartRate { get; set; }         // 心率
    public decimal? Weight { get; set; }        // 体重（kg）
}

/// <summary>AI生成的病历草稿</summary>
public class MedicalRecordDraft
{
    public string ChiefComplaint { get; set; } = string.Empty;    // 主诉
    public string PresentIllness { get; set; } = string.Empty;   // 现病史
    public string PastHistory { get; set; } = string.Empty;      // 既往史
    public string Allergies { get; set; } = string.Empty;        // 过敏史
    public string PersonalHistory { get; set; } = string.Empty;  // 个人史
    public string FamilyHistory { get; set; } = string.Empty;    // 家族史
    public Vitals? Vitals { get; set; }                          // 生命体征
    public string PhysicalExam { get; set; } = string.Empty;     // 体格检查
    public string Diagnosis { get; set; } = string.Empty;        // 诊断
    public string TreatmentPlan { get; set; } = string.Empty;    // 治疗计划
    public List<string> MedicationSuggestions { get; set; } = new(); // 用药建议
    public double Confidence { get; set; }                       // 置信度 0-1
    public List<string> Warnings { get; set; } = new();          // 需要医生确认的警告
}

/// <summary>问诊会话状态</summary>
public class ScribeSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public long PatientId { get; set; }
    public long DoctorId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Idle;
    public string? AudioFilePath { get; set; }
    public List<TranscriptSegment>? Transcript { get; set; }
    public MedicalRecordDraft? Draft { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum SessionStatus
{
    Idle,
    Recording,
    Transcribing,
    Generating,
    Completed,
    Failed,
    Cancelled
}

/// <summary>开始问诊请求</summary>
public class StartScribeRequest
{
    public long PatientId { get; set; }
    public long DoctorId { get; set; }
    public int? ExpectedDuration { get; set; } // 预期时长（秒）
}

/// <summary>停止问诊响应</summary>
public class StopScribeResponse
{
    public string SessionId { get; set; } = string.Empty;
    public double Duration { get; set; }
    public List<TranscriptSegment> Transcript { get; set; } = new();
    public string FullText { get; set; } = string.Empty;
}

/// <summary>生成病历请求</summary>
public class GenerateRecordRequest
{
    public string SessionId { get; set; } = string.Empty;
}

/// <summary>会话状态响应</summary>
public class SessionStatusResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double? Duration { get; set; }
    public string? ErrorMessage { get; set; }
}
