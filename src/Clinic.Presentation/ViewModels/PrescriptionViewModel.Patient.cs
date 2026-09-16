using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Clinic.Presentation.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation.ViewModels;

// ── 患者模块：病历内患者联想输入、选择、历史病历加载、自动建档 ──
// 字段定义见 PrescriptionViewModel.cs 主文件
public partial class PrescriptionViewModel
{
    /// <summary>从下拉列表中选择患者（传 null 时清除已选患者）</summary>
    [RelayCommand]
    private void SelectPatient(PatientDto? patient)
    {
        if (patient is null)
        {
            SelectedPatient = null;
            PatientSearchKeyword = string.Empty;
            ShowPatientDropdown = false;
            return;
        }
        SelectedPatient = patient;
        ShowPatientDropdown = false;
        PatientSearchKeyword = patient.Name;
    }

    /// <summary>选中患者后触发：更新状态提示并加载历史病历</summary>
    partial void OnSelectedPatientChanged(PatientDto? value)
    {
        // 通知复制上次处方命令状态变更
        CopyLastPrescriptionCommand.NotifyCanExecuteChanged();
        if (value is not null)
        {
            StatusMessage = $"已选择患者：{value.Name}（{value.Gender}）";
            _ = LoadPatientHistoryAsync(value.Id);
            // 同步搜索框文本（直接设字段，避免触发重新搜索）
            _patientSearchKeyword = value.Name;
            OnPropertyChanged(nameof(PatientSearchKeyword));
            ShowPatientDropdown = false;
        }
        else
        {
            StatusMessage = null;
            PatientHistory.Clear();
            HasPatientHistory = false;
        }

        // 通知计算属性更新
        OnPropertyChanged(nameof(SelectedPatientAgeText));
        OnPropertyChanged(nameof(SelectedPatientAllergyText));
        OnPropertyChanged(nameof(SelectedPatientChronicText));
        OnPropertyChanged(nameof(HasAllergy));
        OnPropertyChanged(nameof(HasChronic));
        OnPropertyChanged(nameof(PrescriptionPatientName));
        OnPropertyChanged(nameof(PrescriptionPatientGender));
        // 新患者模式下病历上的性别/电话可编辑
        OnPropertyChanged(nameof(IsNewPatientMode));
        SavePrescriptionCommand.NotifyCanExecuteChanged();
        SaveAndReviewCommand.NotifyCanExecuteChanged();
    }

    /// <summary>病历上直接输入新患者：性别（默认男）</summary>
    [ObservableProperty]
    private string _patientGender = "男";

    /// <summary>病历上直接输入新患者：手机号</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PatientPhoneError))]
    private string _patientPhone = string.Empty;

    /// <summary>病历上直接输入新患者：出生日期（可选；DatePicker 使用 DateTime）</summary>
    [ObservableProperty]
    private DateTime? _patientDob;

    /// <summary>病历上直接输入新患者：过敏史（可选，处方保存时用于药物过敏检查）</summary>
    [ObservableProperty]
    private string? _patientAllergies;

    /// <summary>病历上直接输入新患者：基础疾病（可选，如高血压、糖尿病等，用于药物禁忌检查）</summary>
    [ObservableProperty]
    private string? _patientChronicTags;

    /// <summary>病历上未选择已有患者时，性别/电话等字段可编辑（新患者模式）</summary>
    public bool IsNewPatientMode => SelectedPatient is null;

    /// <summary>处方笺患者姓名：已选患者优先；新患者取病历输入的名字（保存前 SelectedPatient 可能为空）</summary>
    public string PrescriptionPatientName =>
        SelectedPatient?.Name
        ?? (!string.IsNullOrWhiteSpace(PatientSearchKeyword) ? PatientSearchKeyword : "—");

    /// <summary>处方笺患者性别：已选患者优先；新患者取病历选择的性别</summary>
    public string PrescriptionPatientGender =>
        SelectedPatient?.Gender ?? PatientGender;

    /// <summary>性别变化时同步处方笺性别显示</summary>
    partial void OnPatientGenderChanged(string value)
        => OnPropertyChanged(nameof(PrescriptionPatientGender));

    /// <summary>病历上输入的手机号验证错误（null 表示无错误）</summary>
    public string? PatientPhoneError
    {
        get
        {
            if (SelectedPatient is not null || string.IsNullOrWhiteSpace(PatientPhone))
                return null;
            var digits = new string(PatientPhone.Where(char.IsDigit).ToArray());
            if (digits.Length != 11 || !digits.StartsWith("1") || digits[1] < '3' || digits[1] > '9')
                return "手机号格式不正确";
            return null;
        }
    }

    partial void OnPatientPhoneChanged(string value)
    {
        OnPropertyChanged(nameof(PatientPhoneError));
        SavePrescriptionCommand.NotifyCanExecuteChanged();
        SaveAndReviewCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 确保已选择患者：若病历上直接输入了新患者信息（姓名+性别+手机号），
    /// 自动建档并选中，避免医生必须先去搜索/建档的额外操作。
    /// 返回 true 表示已具备患者（已选或建档成功）。
    /// </summary>
    private async Task<bool> EnsurePatientSelectedAsync()
    {
        if (SelectedPatient is not null)
            return true;

        var name = PatientSearchKeyword?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ErrorMessage = "请在病历表单输入患者姓名（输入时可选已有患者）";
            return false;
        }

        // 查找是否已有同名患者
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();
            var sameName = await patientService.SearchByNameAsync(name);
            var exact = sameName.FirstOrDefault(p => p.Name == name);
            if (exact is not null)
            {
                SelectedPatient = exact;
                return true;
            }
        }
        catch
        {
            // 查询失败继续走建档流程，由建档步骤统一报错
        }

        if (PatientPhoneError is not null)
        {
            ErrorMessage = $"新患者手机号无效：{PatientPhoneError}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(PatientPhone))
        {
            ErrorMessage = "新患者请在病历表单填写手机号";
            return false;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();
            var id = await patientService.CreatePatientAsync(
                name, PatientGender,
                PatientDob.HasValue ? DateOnly.FromDateTime(PatientDob.Value) : null,
                PatientPhone.Trim(), PatientAllergies?.Trim(), null,
                PatientChronicTags?.Trim());
            var patient = await patientService.GetPatientByIdAsync(id);
            if (patient is not null)
            {
                SelectedPatient = patient;
                StatusMessage = $"已自动建档患者：{patient.Name}（{patient.Phone}）";
                return true;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"自动建档失败：{ExceptionFormatter.GetMessage(ex)}";
        }

        return false;
    }

    /// <summary>加载患者历史处方/病历记录，帮助医生快速了解病史</summary>
    private async Task LoadPatientHistoryAsync(long patientId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var prescriptionService = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();
            var history = await prescriptionService.GetPrescriptionHistoryAsync();

            PatientHistory.Clear();
            foreach (var record in history.Where(h => h.PatientId == patientId).Take(10))
            {
                PatientHistory.Add(record);
            }
            HasPatientHistory = PatientHistory.Count > 0;
        }
        catch
        {
            // 历史加载失败不阻断处方流程
            PatientHistory.Clear();
            HasPatientHistory = false;
        }
    }
}
