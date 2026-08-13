namespace Clinic.Application.DTOs;

/// <summary>
/// LLM 整理后的结构化诊断信息。
/// 用于将医生自由文本描述转换为系统可识别的结构化数据。
/// 所有字段均为 LLM 建议值，需人工确认后写入系统。
/// </summary>
public record StructuredDiagnosis(
    /// <summary>主诉（chief complaint）</summary>
    string ChiefComplaint,
    /// <summary>体征描述（如体温、血压等）</summary>
    string? Signs,
    /// <summary>疑似诊断</summary>
    string? SuspectedDiagnosis,
    /// <summary>建议检查项目</summary>
    string? SuggestedExams,
    /// <summary>LLM 原始输出（用于人工复核）</summary>
    string RawOutput);

/// <summary>
/// LLM 整理后的结构化过敏史信息。
/// 将自由文本过敏史拆分为结构化标签，解决当前过敏匹配粒度过粗的问题。
/// 例如："青霉素过敏，吃海鲜起疹子" → DrugAllergies: ["青霉素类"], OtherAllergies: ["海鲜"]
/// </summary>
public record StructuredAllergy(
    /// <summary>识别出的药物过敏标签列表（如 "青霉素类"、"磺胺类"、"头孢类"）</summary>
    IReadOnlyList<string> DrugAllergyTags,
    /// <summary>识别出的非药物过敏标签列表（如 "海鲜"、"花粉"）</summary>
    IReadOnlyList<string> OtherAllergyTags,
    /// <summary>过敏表现描述（如 "皮疹"、"呼吸困难"）</summary>
    string? ReactionDescription,
    /// <summary>LLM 原始输出（用于人工复核）</summary>
    string RawOutput);

/// <summary>
/// LLM 整理后的处方用药建议。
/// 将口语化用药描述转换为标准化的处方明细项。
/// 例如："阿莫西林一天三次一次两粒吃七天" → 标准化的药品、剂量、频次、疗程。
/// </summary>
public record PrescriptionSuggestion(
    /// <summary>识别出的药品名称（需与 drug_master 表匹配）</summary>
    string DrugName,
    /// <summary>单次剂量</summary>
    decimal? Dose,
    /// <summary>剂量单位</summary>
    string? DoseUnit,
    /// <summary>用药频次（如 "tid"、"bid"）</summary>
    string? Frequency,
    /// <summary>给药途径（如 "口服"、"静脉注射"）</summary>
    string? Route,
    /// <summary>疗程天数</summary>
    int? DurationDays,
    /// <summary>总量</summary>
    decimal? TotalQty,
    /// <summary>LLM 原始输出（用于人工复核）</summary>
    string RawOutput);

/// <summary>
/// LLM 服务的可用性状态。
/// </summary>
public record LlmStatus(
    /// <summary>LLM 服务是否可用</summary>
    bool IsAvailable,
    /// <summary>当前使用的模型名称</summary>
    string? ModelName,
    /// <summary>服务端点</summary>
    string? Endpoint,
    /// <summary>状态描述</summary>
    string? Message);