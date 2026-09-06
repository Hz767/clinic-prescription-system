using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;

namespace Clinic.Infrastructure.Llm;

/// <summary>
/// LLM 服务的空操作回退实现。
/// 当 Ollama 不可用时使用此实现，所有方法返回空/默认结果，不阻断业务流程。
/// 
/// 使用场景：
/// - 未安装 Ollama 的环境
/// - 配置文件未启用 LLM 功能
/// - 首次部署无需 LLM 的场景
/// </summary>
public sealed class NoOpLlmService : ILlmService
{
    public Task<StructuredDiagnosis> ParseDiagnosisAsync(string freeText, CancellationToken ct = default)
    {
        return Task.FromResult(new StructuredDiagnosis(
            ChiefComplaint: freeText ?? string.Empty,
            Signs: null,
            SuspectedDiagnosis: null,
            SuggestedExams: null,
            RawOutput: string.Empty));
    }

    public Task<StructuredAllergy> ParseAllergyAsync(string freeText, CancellationToken ct = default)
    {
        return Task.FromResult(new StructuredAllergy(
            DrugAllergyTags: Array.Empty<string>(),
            OtherAllergyTags: Array.Empty<string>(),
            ReactionDescription: null,
            RawOutput: string.Empty));
    }

    public Task<PrescriptionSuggestion> ParsePrescriptionAsync(string freeText, CancellationToken ct = default)
    {
        return Task.FromResult(new PrescriptionSuggestion(
            DrugName: string.Empty,
            Dose: null,
            DoseUnit: null,
            Frequency: null,
            Route: null,
            DurationDays: null,
            TotalQty: null,
            RawOutput: string.Empty));
    }

    public Task<IReadOnlyList<PrescriptionSuggestion>> ParsePrescriptionListAsync(
        string freeText, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<PrescriptionSuggestion>>(Array.Empty<PrescriptionSuggestion>());
    }

    public Task<LlmStatus> GetStatusAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new LlmStatus(
            IsAvailable: false,
            ModelName: null,
            Endpoint: null,
            Message: "LLM 功能未启用。如需使用，请安装 Ollama 并在 appsettings.json 中配置。"));
    }

    public Task<EvidenceBasedAdvice> GenerateEvidenceBasedAdviceAsync(
        string patientInfo, string chiefComplaint, string diagnosis, string vitalSigns,
        CancellationToken ct = default)
    {
        return Task.FromResult(new EvidenceBasedAdvice(
            DifferentialDiagnoses: string.Empty,
            SuggestedExams: string.Empty,
            TreatmentOptions: string.Empty,
            MedicationReference: string.Empty,
            RiskWarnings: string.Empty,
            EvidenceLevel: string.Empty,
            RawOutput: null));
    }
}