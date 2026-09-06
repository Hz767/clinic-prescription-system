using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Clinic.Presentation.Helpers;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation.ViewModels;

// ── 患者模块：患者搜索、选择、历史病历加载、快速建档 ──
// 字段定义见 PrescriptionViewModel.cs 主文件
public partial class PrescriptionViewModel
{
    /// <summary>点击搜索按钮时触发（保留兼容原有流程）</summary>
    [RelayCommand(CanExecute = nameof(CanSearchPatient))]
    private async Task SearchPatientAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();

            MatchedPatients.Clear();
            var keyword = PatientSearchKeyword.Trim();

            if (keyword.All(char.IsDigit) && keyword.Length >= 4)
            {
                var patient = await patientService.FindByPhoneAsync(keyword);
                if (patient is not null)
                    MatchedPatients.Add(patient);
            }
            else
            {
                var results = await patientService.SearchByNameAsync(keyword);
                foreach (var p in results)
                    MatchedPatients.Add(p);
            }

            if (MatchedPatients.Count == 0)
                StatusMessage = "未找到匹配的患者";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"搜索患者失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSearchPatient() => !IsBusy && !string.IsNullOrWhiteSpace(PatientSearchKeyword);

    /// <summary>从下拉列表中选择患者（传 null 时清除已选患者）</summary>
    [RelayCommand]
    private void SelectPatient(PatientDto? patient)
    {
        if (patient is null)
        {
            SelectedPatient = null;
            PatientSearchKeyword = string.Empty;
            ShowPatientDropdown = false;
            HasNoResults = false;
            IsQuickRegistration = false;
            return;
        }
        SelectedPatient = patient;
        ShowPatientDropdown = false;
        HasNoResults = false;
        IsQuickRegistration = false;
        PatientSearchKeyword = patient.Name;
    }

    /// <summary>选中患者后触发：更新状态提示并加载历史病历</summary>
    partial void OnSelectedPatientChanged(PatientDto? value)
    {
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
    }

    /// <summary>快速建档手机号实时验证（中国手机号：11位，1开头，第二位3-9）</summary>
    partial void OnNewPatientPhoneChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            NewPatientPhoneError = null;
            return;
        }

        // 只保留数字
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            NewPatientPhoneError = "请输入数字";
        }
        else if (digits.Length < 11)
        {
            NewPatientPhoneError = $"手机号不足11位（已输入{digits.Length}位）";
        }
        else if (digits.Length > 11)
        {
            NewPatientPhoneError = "手机号不能超过11位";
        }
        else if (!digits.StartsWith("1"))
        {
            NewPatientPhoneError = "手机号必须以1开头";
        }
        else if (digits[1] < '3' || digits[1] > '9')
        {
            NewPatientPhoneError = "手机号第二位必须是3-9";
        }
        else
        {
            NewPatientPhoneError = null;
        }
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

    /// <summary>切换到快速建档模式</summary>
    [RelayCommand]
    private void ToggleQuickRegistration()
    {
        IsQuickRegistration = true;
        ShowPatientDropdown = false;
        NewPatientName = PatientSearchKeyword.Trim();
        NewPatientPhone = string.Empty;
        NewPatientGender = "男";
        NewPatientDob = null;
        NewPatientAllergies = null;
        NewPatientChronicTags = null;
        NewPatientPhoneError = null;
    }

    /// <summary>快速建档并选择该患者</summary>
    [RelayCommand(CanExecute = nameof(CanCreateNewPatient))]
    private async Task CreateNewPatientAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPatientName) || string.IsNullOrWhiteSpace(NewPatientPhone))
        {
            ErrorMessage = "姓名和电话为必填项";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();

            var id = await patientService.CreatePatientAsync(
                NewPatientName.Trim(), NewPatientGender,
                NewPatientDob.HasValue ? DateOnly.FromDateTime(NewPatientDob.Value) : null,
                NewPatientPhone.Trim(), NewPatientAllergies?.Trim(), null,
                NewPatientChronicTags?.Trim());

            var patient = await patientService.GetPatientByIdAsync(id);
            if (patient is not null)
            {
                SelectedPatient = patient;
                IsQuickRegistration = false;
                ShowPatientDropdown = false;
                PatientSearchKeyword = patient.Name;
                StatusMessage = $"已建档并选择患者：{patient.Name}";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"建档失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCreateNewPatient()
        => !IsBusy && !string.IsNullOrWhiteSpace(NewPatientName)
        && !string.IsNullOrWhiteSpace(NewPatientPhone)
        && NewPatientPhoneError is null;
}
