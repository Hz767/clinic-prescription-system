using System.Windows;
using Clinic.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clinic.Presentation.ViewModels;

/// <summary>
/// 处方ViewModel - AI智能问诊部分
/// 功能：开始/停止录音问诊，AI自动生成病历草稿
/// </summary>
public partial class PrescriptionViewModel
{
    private IAiScribeClient? _aiScribeClient;
    private string? _currentScribeSessionId;

    /// <summary>AI问诊Agent是否可用</summary>
    [ObservableProperty]
    private bool _aiScribeAvailable;

    /// <summary>是否正在录音问诊</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopScribeCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartScribeCommand))]
    private bool _isScribeRecording;

    /// <summary>是否正在处理（转写/生成病历）</summary>
    [ObservableProperty]
    private bool _isScribeProcessing;

    /// <summary>问诊转写文本（医患对话）</summary>
    [ObservableProperty]
    private string _scribeTranscript = string.Empty;

    /// <summary>AI生成的病历草稿-主诉</summary>
    [ObservableProperty]
    private string _aiChiefComplaint = string.Empty;

    /// <summary>AI生成的病历草稿-现病史</summary>
    [ObservableProperty]
    private string _aiPresentIllness = string.Empty;

    /// <summary>AI生成的病历草稿-诊断</summary>
    [ObservableProperty]
    private string _aiDiagnosis = string.Empty;

    /// <summary>AI生成的病历草稿-治疗建议</summary>
    [ObservableProperty]
    private string _aiTreatmentPlan = string.Empty;

    /// <summary>AI病历置信度</summary>
    [ObservableProperty]
    private double _aiConfidence;

    /// <summary>AI警告信息</summary>
    [ObservableProperty]
    private string _aiWarnings = string.Empty;

    /// <summary>是否显示AI病历预览</summary>
    [ObservableProperty]
    private bool _showAiDraft;

    /// <summary>问诊录音时长（秒）</summary>
    [ObservableProperty]
    private double _scribeDuration;

    private IAiScribeClient AiScribeClient =>
        _aiScribeClient ??= new AiScribeClient(
            System.Windows.Application.Current?.TryFindResource("AiScribeBaseUrl") as string
            ?? "http://127.0.0.1:5080");

    /// <summary>检查AI问诊服务可用性</summary>
    public async Task CheckAiScribeAvailabilityAsync()
    {
        try
        {
            AiScribeAvailable = await AiScribeClient.IsAvailableAsync();
        }
        catch
        {
            AiScribeAvailable = false;
        }
    }

    /// <summary>开始AI问诊录音</summary>
    [RelayCommand(CanExecute = nameof(CanStartScribe))]
    private async Task StartScribeAsync()
    {
        if (SelectedPatient == null)
        {
            MessageBox.Show("请先选择患者", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            IsScribeRecording = true;
            IsBusy = true;
            ScribeTranscript = string.Empty;
            ShowAiDraft = false;

            _currentScribeSessionId = await AiScribeClient.StartAsync(
                SelectedPatient.Id, _session.UserId ?? 0);

            // 启动计时器更新时长
            _ = UpdateScribeDurationAsync();
        }
        catch (Exception ex)
        {
            IsScribeRecording = false;
            IsBusy = false;
            MessageBox.Show($"开始问诊失败：{ex.Message}\n\n请确认AI问诊Agent服务已启动。",
                "AI问诊", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool CanStartScribe() => !IsScribeRecording && !IsScribeProcessing && SelectedPatient != null;

    /// <summary>停止问诊并生成病历</summary>
    [RelayCommand(CanExecute = nameof(CanStopScribe))]
    private async Task StopScribeAsync()
    {
        if (string.IsNullOrEmpty(_currentScribeSessionId)) return;

        try
        {
            IsScribeRecording = false;
            IsScribeProcessing = true;
            IsBusy = true;

            // 停止录音并获取转写
            var stopResult = await AiScribeClient.StopAsync(_currentScribeSessionId);
            ScribeTranscript = stopResult.FullText;
            ScribeDuration = stopResult.Duration;

            if (string.IsNullOrWhiteSpace(ScribeTranscript))
            {
                MessageBox.Show("未识别到语音内容，请检查麦克风。", "AI问诊",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                IsScribeProcessing = false;
                IsBusy = false;
                return;
            }

            // 生成结构化病历
            var draft = await AiScribeClient.GenerateAsync(_currentScribeSessionId);

            AiChiefComplaint = draft.ChiefComplaint;
            AiPresentIllness = draft.PresentIllness;
            AiDiagnosis = draft.Diagnosis;
            AiTreatmentPlan = draft.TreatmentPlan;
            AiConfidence = draft.Confidence;
            AiWarnings = draft.Warnings.Count > 0
                ? string.Join("\n", draft.Warnings)
                : string.Empty;
            ShowAiDraft = true;

            // 自动填充到处方表单
            if (!string.IsNullOrEmpty(AiDiagnosis))
                DiagnosisText = AiDiagnosis;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"AI问诊处理失败：{ex.Message}", "AI问诊",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsScribeProcessing = false;
            IsBusy = false;
            _currentScribeSessionId = null;
        }
    }

    private bool CanStopScribe() => IsScribeRecording;

    /// <summary>将AI生成的病历应用到处方</summary>
    [RelayCommand]
    private void ApplyAiDraft()
    {
        if (!string.IsNullOrEmpty(AiChiefComplaint))
            ChiefComplaint = AiChiefComplaint;
        if (!string.IsNullOrEmpty(AiDiagnosis))
            DiagnosisText = AiDiagnosis;

        MessageBox.Show("AI病历内容已应用到处方表单（主诉、诊断）。\n现病史和治疗建议请在门诊病历中填写。",
            "AI问诊", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>更新录音时长</summary>
    private async Task UpdateScribeDurationAsync()
    {
        while (IsScribeRecording)
        {
            try
            {
                if (!string.IsNullOrEmpty(_currentScribeSessionId))
                {
                    var status = await AiScribeClient.GetStatusAsync(_currentScribeSessionId);
                    if (status.Duration.HasValue)
                        ScribeDuration = status.Duration.Value;
                }
            }
            catch { }
            await Task.Delay(1000);
        }
    }
}
