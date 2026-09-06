using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clinic.Presentation.ViewModels;

// ── AI辅助模块：AI病历生成、AI药师审核、循证医学分析、LLM处方解析 ──
public partial class PrescriptionViewModel
{
    // ── AI药师审核意见 ──

    /// <summary>AI药师审核意见文本</summary>
    [ObservableProperty] private string? _aiPharmacistAdvice;

    /// <summary>AI审核意见级别: 0=通过 1=提示 2=警告 3=严重警告</summary>
    [ObservableProperty] private int _aiAdviceLevel;

    /// <summary>是否有AI审核意见</summary>
    [ObservableProperty] private bool _hasAiAdvice;

    // ── LLM 辅助输入 ──

    /// <summary>LLM 服务是否可用</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateMedicalRecordCommand))]
    [NotifyCanExecuteChangedFor(nameof(ParsePrescriptionCommand))]
    private bool _llmIsAvailable;

    /// <summary>AI生成的规范病历文本（可编辑，确认后使用）</summary>
    [ObservableProperty]
    private string? _aiDiagnosisResult;

    /// <summary>是否有AI病历结果待确认</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmAiDiagnosisCommand))]
    private bool _hasAiDiagnosisResult;

    /// <summary>用于 LLM 解析的自由文本用药描述</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ParsePrescriptionCommand))]
    private string _llmPrescriptionInput = string.Empty;

    // ── 循证医学辅助决策 ──

    [ObservableProperty]
    private EvidenceBasedAdvice? _evidenceBasedAdvice;

    [ObservableProperty]
    private bool _isEvidenceBasedLoading;

    [ObservableProperty]
    private bool _showEvidenceBasedPanel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunEvidenceBasedAnalysisCommand))]
    private bool _canRunEvidenceBased;

    [RelayCommand(CanExecute = nameof(CanRunEvidenceBasedAnalysis))]
    private async Task RunEvidenceBasedAnalysisAsync()
    {
        if (SelectedPatient is null)
        {
            ErrorMessage = "请先选择患者";
            return;
        }

        IsEvidenceBasedLoading = true;
        ShowEvidenceBasedPanel = true;
        ErrorMessage = null;

        try
        {
            // 组装患者信息
            var age = SelectedPatient.Dob.HasValue
                ? DateTime.Today.Year - SelectedPatient.Dob.Value.Year
                : (int?)null;
            var patientInfo = $"{age}岁{SelectedPatient.Gender}";
            if (!string.IsNullOrWhiteSpace(SelectedPatient.Allergies))
                patientInfo += $"，过敏史：{SelectedPatient.Allergies}";
            if (!string.IsNullOrWhiteSpace(SelectedPatient.ChronicTags))
                patientInfo += $"，基础疾病：{SelectedPatient.ChronicTags}";

            // 组装体征
            var vitalSigns = string.Empty;
            if (PatientTemperature.HasValue) vitalSigns += $"T{PatientTemperature}℃ ";
            if (PatientSystolicBP.HasValue && PatientDiastolicBP.HasValue)
                vitalSigns += $"BP{PatientSystolicBP}/{PatientDiastolicBP}mmHg ";
            if (PatientHeartRate.HasValue) vitalSigns += $"HR{PatientHeartRate}次/分 ";
            if (PatientWeight.HasValue) vitalSigns += $"Wt{PatientWeight}kg";

            var advice = await _llmService.GenerateEvidenceBasedAdviceAsync(
                patientInfo, ChiefComplaint ?? string.Empty, DiagnosisText, vitalSigns.Trim());

            EvidenceBasedAdvice = advice;

            if (string.IsNullOrWhiteSpace(advice.DifferentialDiagnoses))
            {
                StatusMessage = "循证分析完成，但结果为空（AI服务可能不可用）";
            }
            else
            {
                StatusMessage = "循证医学分析完成，仅供参考";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"循证分析失败：{ex.Message}";
        }
        finally
        {
            IsEvidenceBasedLoading = false;
        }
    }

    private bool CanRunEvidenceBasedAnalysis()
        => !IsEvidenceBasedLoading && SelectedPatient is not null
           && (!string.IsNullOrWhiteSpace(ChiefComplaint) || !string.IsNullOrWhiteSpace(DiagnosisText));

    /// <summary>检查 LLM 服务状态，页面加载时调用</summary>
    public async Task CheckLlmStatusAsync()
    {
        try
        {
            var status = await _llmService.GetStatusAsync();
            LlmIsAvailable = status.IsAvailable;
            CanRunEvidenceBased = status.IsAvailable;
        }
        catch
        {
            LlmIsAvailable = false;
            CanRunEvidenceBased = false;
        }
    }
}
