using Clinic.Application.DTOs;

namespace Clinic.Application.Interfaces;

/// <summary>
/// 本地 LLM 服务接口。
/// 将医生自由文本输入整理为结构化信息，供人工确认后写入系统。
/// 
/// 设计原则：
/// 1. LLM 仅作为"智能输入助手"，输出均为建议值，需人工确认
/// 2. 所有方法均为纯文本转换，不直接写入数据库
/// 3. 调用方负责将确认后的结果写入系统
/// 4. 服务不可用时返回空结果，不阻断业务流程
/// 
/// 使用场景：
/// - ParseDiagnosisAsync: 医生口述诊断 → 结构化诊断信息
/// - ParseAllergyAsync: 自由文本过敏史 → 结构化过敏标签
/// - ParsePrescriptionAsync: 口语化用药描述 → 标准化处方明细
/// </summary>
public interface ILlmService
{
    /// <summary>
    /// 将医生自由文本诊断描述整理为结构化诊断信息。
    /// </summary>
    /// <param name="freeText">医生输入的自由文本，如 "患者咳嗽三天，有黄痰，发烧38度"</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>结构化诊断建议；LLM 不可用时返回空的 ChiefComplaint</returns>
    Task<StructuredDiagnosis> ParseDiagnosisAsync(string freeText, CancellationToken ct = default);

    /// <summary>
    /// 将自由文本过敏史整理为结构化过敏标签。
    /// 解决当前过敏匹配粒度过粗的问题：
    /// "青霉素过敏" → 识别为 ["青霉素类"] → 可与 "阿莫西林胶囊" 正确匹配。
    /// </summary>
    /// <param name="freeText">患者口述过敏史，如 "青霉素过敏，吃海鲜起疹子，磺胺类也过敏"</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>结构化过敏标签建议；LLM 不可用时返回空列表</returns>
    Task<StructuredAllergy> ParseAllergyAsync(string freeText, CancellationToken ct = default);

    /// <summary>
    /// 将口语化用药描述整理为标准化处方明细。
    /// </summary>
    /// <param name="freeText">医生口述或患者转述的用药描述，如 "阿莫西林一天三次一次两粒吃七天"</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>标准化处方建议；LLM 不可用时返回空的 DrugName</returns>
    Task<PrescriptionSuggestion> ParsePrescriptionAsync(string freeText, CancellationToken ct = default);

    /// <summary>
    /// 获取 LLM 服务的当前状态。
    /// 用于 UI 层判断是否显示 LLM 辅助功能入口。
    /// </summary>
    Task<LlmStatus> GetStatusAsync(CancellationToken ct = default);
}