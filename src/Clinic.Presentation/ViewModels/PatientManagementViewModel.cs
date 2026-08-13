using System.Collections.ObjectModel;
using System.Linq;
using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Clinic.Presentation.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation.ViewModels;

/// <summary>
/// 患者管理 ViewModel。提供患者搜索、建档、列表展示功能。
/// 搜索策略：输入纯数字按手机号查找，否则按姓名模糊搜索。
/// </summary>
public partial class PatientManagementViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILlmService _llmService;

    // ── 搜索区 ──

    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private bool _isBusy;

    // ── 患者列表 ──

    public ObservableCollection<PatientDto> Patients { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditPatientCommand))]
    private PatientDto? _selectedPatient;

    // ── 编辑模式 ──

    /// <summary>是否处于编辑模式（true=编辑已有患者，false=新建患者）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreatePatientCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdatePatientCommand))]
    private bool _isEditMode;

    /// <summary>正在编辑的患者 ID（编辑模式下使用）</summary>
    private long? _editingPatientId;

    // ── 新建患者表单 ──

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreatePatientCommand))]
    private string _newName = string.Empty;

    [ObservableProperty]
    private string _newGender = "男";

    [ObservableProperty]
    private DateTime? _newDob;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreatePatientCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditPatientCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdatePatientCommand))]
    private string _newPhone = string.Empty;

    /// <summary>手机号验证错误提示（null 表示无错误）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreatePatientCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditPatientCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdatePatientCommand))]
    private string? _newPhoneError;

    [ObservableProperty]
    private string _newAllergies = string.Empty;

    [ObservableProperty]
    private string _newHistory = string.Empty;

    [ObservableProperty]
    private string _newChronicTags = string.Empty;

    // ── LLM 辅助输入 ──

    /// <summary>LLM 服务是否可用</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ParseAllergyCommand))]
    private bool _llmIsAvailable;

    /// <summary>用于 LLM 解析的自由文本过敏史输入</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ParseAllergyCommand))]
    private string _llmAllergyInput = string.Empty;

    // ── 状态消息 ──

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    public PatientManagementViewModel(IServiceScopeFactory scopeFactory, ILlmService llmService)
    {
        _scopeFactory = scopeFactory;
        _llmService = llmService;
    }

    /// <summary>检查 LLM 服务状态，页面加载时调用</summary>
    public async Task CheckLlmStatusAsync()
    {
        try
        {
            var status = await _llmService.GetStatusAsync();
            LlmIsAvailable = status.IsAvailable;
        }
        catch
        {
            LlmIsAvailable = false;
        }
    }

    /// <summary>页面加载时自动加载全部患者列表</summary>
    public async Task LoadAllPatientsAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();

            Patients.Clear();
            var results = await patientService.GetAllPatientsAsync();
            foreach (var p in results)
                Patients.Add(p);
            StatusMessage = Patients.Count > 0
                ? $"共 {Patients.Count} 位患者"
                : "暂无患者档案，请在右侧表单中创建";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"加载失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>搜索患者：纯数字 → 按手机号查找，否则 → 按姓名模糊搜索</summary>
    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();

            Patients.Clear();

            var keyword = SearchKeyword.Trim();

            // 纯数字 → 手机号查找
            if (keyword.All(char.IsDigit) && keyword.Length >= 4)
            {
                var patient = await patientService.FindByPhoneAsync(keyword);
                if (patient is not null)
                    Patients.Add(patient);
                StatusMessage = Patients.Count > 0
                    ? $"找到 {Patients.Count} 条记录"
                    : "未找到匹配的患者";
            }
            else
            {
                var results = await patientService.SearchByNameAsync(keyword);
                foreach (var p in results)
                    Patients.Add(p);
                StatusMessage = $"找到 {Patients.Count} 条记录";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"搜索失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSearch() => !IsBusy && !string.IsNullOrWhiteSpace(SearchKeyword);

    /// <summary>创建新患者档案</summary>
    [RelayCommand(CanExecute = nameof(CanCreatePatient))]
    private async Task CreatePatientAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();

            DateOnly? dob = NewDob.HasValue
                ? DateOnly.FromDateTime(NewDob.Value)
                : null;

            var patientId = await patientService.CreatePatientAsync(
                NewName.Trim(),
                NewGender,
                dob,
                NewPhone.Trim(),
                string.IsNullOrWhiteSpace(NewAllergies) ? null : NewAllergies.Trim(),
                string.IsNullOrWhiteSpace(NewHistory) ? null : NewHistory.Trim(),
                string.IsNullOrWhiteSpace(NewChronicTags) ? null : NewChronicTags.Trim());

            StatusMessage = $"患者档案已创建（ID: {patientId}）";

            // 先保存手机号用于搜索，再清空表单
            var createdPhone = NewPhone.Trim();

            NewName = string.Empty;
            NewGender = "男";
            NewDob = null;
            NewPhone = string.Empty;
            NewAllergies = string.Empty;
            NewHistory = string.Empty;
            NewChronicTags = string.Empty;

            // 自动搜索新创建的患者并在列表中显示
            SearchKeyword = createdPhone;
            await SearchAsync();
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

    /// <summary>手机号实时验证（中国手机号：11位，1开头，第二位3-9）</summary>
    partial void OnNewPhoneChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            NewPhoneError = null;
            return;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            NewPhoneError = "请输入数字";
        }
        else if (digits.Length < 11)
        {
            NewPhoneError = $"手机号不足11位（已输入{digits.Length}位）";
        }
        else if (digits.Length > 11)
        {
            NewPhoneError = $"手机号超过11位（已输入{digits.Length}位）";
        }
        else if (!digits.StartsWith('1'))
        {
            NewPhoneError = "手机号须以1开头";
        }
        else if (digits[1] < '3' || digits[1] > '9')
        {
            NewPhoneError = "手机号第二位须为3-9";
        }
        else
        {
            NewPhoneError = null;
        }
    }

    private bool CanCreatePatient()
        => !IsBusy
           && !string.IsNullOrWhiteSpace(NewName)
           && !string.IsNullOrWhiteSpace(NewPhone)
           && NewPhoneError is null;

    /// <summary>使用 LLM 将自由文本过敏史整理为结构化标签</summary>
    [RelayCommand(CanExecute = nameof(CanParseAllergy))]
    private async Task ParseAllergyAsync()
    {
        if (string.IsNullOrWhiteSpace(LlmAllergyInput))
            return;

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            var result = await _llmService.ParseAllergyAsync(LlmAllergyInput.Trim());

            if (result.DrugAllergyTags.Count > 0 || result.OtherAllergyTags.Count > 0)
            {
                // 构建结构化过敏史文本
                var parts = new List<string>();
                if (result.DrugAllergyTags.Count > 0)
                    parts.Add($"药物过敏：{string.Join("、", result.DrugAllergyTags)}");
                if (result.OtherAllergyTags.Count > 0)
                    parts.Add($"其他过敏：{string.Join("、", result.OtherAllergyTags)}");
                if (!string.IsNullOrWhiteSpace(result.ReactionDescription))
                    parts.Add($"表现：{result.ReactionDescription}");

                // 将 LLM 结果填入过敏史输入框，同时保留原始输入供人工对比
                var existing = string.IsNullOrWhiteSpace(NewAllergies) ? "" : NewAllergies + "\n";
                NewAllergies = existing + $"[AI 整理] {string.Join("；", parts)}";
                StatusMessage = $"过敏史已结构化（原始输入已保留，请人工确认）";
            }
            else
            {
                StatusMessage = "LLM 未能识别过敏信息，请手动输入";
            }
        }
        catch (Exception ex)
        {
            // LLM 失败不阻断流程，仅提示
            StatusMessage = $"AI 整理失败：{ex.Message}，请手动输入过敏史";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanParseAllergy() => !IsBusy && LlmIsAvailable && !string.IsNullOrWhiteSpace(LlmAllergyInput);

    /// <summary>进入编辑模式：将选中患者信息加载到表单</summary>
    [RelayCommand(CanExecute = nameof(CanEditPatient))]
    private void EditPatient()
    {
        if (SelectedPatient is null)
            return;

        _editingPatientId = SelectedPatient.Id;
        IsEditMode = true;

        NewName = SelectedPatient.Name;
        NewGender = SelectedPatient.Gender;
        NewDob = SelectedPatient.Dob.HasValue
            ? new DateTime(SelectedPatient.Dob.Value.Year, SelectedPatient.Dob.Value.Month, SelectedPatient.Dob.Value.Day)
            : null;
        NewPhone = SelectedPatient.Phone;
        NewAllergies = SelectedPatient.Allergies ?? string.Empty;
        NewHistory = SelectedPatient.History ?? string.Empty;
        NewChronicTags = SelectedPatient.ChronicTags ?? string.Empty;

        StatusMessage = $"正在编辑患者：{SelectedPatient.Name}";
        ErrorMessage = null;
    }

    private bool CanEditPatient() => !IsBusy && SelectedPatient is not null;

    /// <summary>保存患者修改</summary>
    [RelayCommand(CanExecute = nameof(CanUpdatePatient))]
    private async Task UpdatePatientAsync()
    {
        if (_editingPatientId is null)
            return;

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();

            DateOnly? dob = NewDob.HasValue
                ? DateOnly.FromDateTime(NewDob.Value)
                : null;

            var success = await patientService.UpdatePatientAsync(
                _editingPatientId.Value,
                NewName.Trim(),
                NewGender,
                dob,
                NewPhone.Trim(),
                string.IsNullOrWhiteSpace(NewAllergies) ? null : NewAllergies.Trim(),
                string.IsNullOrWhiteSpace(NewHistory) ? null : NewHistory.Trim(),
                string.IsNullOrWhiteSpace(NewChronicTags) ? null : NewChronicTags.Trim());

            if (success)
            {
                StatusMessage = $"患者档案已更新（ID: {_editingPatientId}）";

                // 退出编辑模式并清空表单
                var editedPhone = NewPhone.Trim();
                CancelEdit();

                // 刷新列表
                SearchKeyword = editedPhone;
                await SearchAsync();
            }
            else
            {
                ErrorMessage = "更新失败：患者不存在";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"更新失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanUpdatePatient()
        => IsEditMode
           && !IsBusy
           && !string.IsNullOrWhiteSpace(NewName)
           && !string.IsNullOrWhiteSpace(NewPhone)
           && NewPhoneError is null;

    /// <summary>取消编辑模式，返回新建模式</summary>
    [RelayCommand]
    private void CancelEdit()
    {
        IsEditMode = false;
        _editingPatientId = null;

        NewName = string.Empty;
        NewGender = "男";
        NewDob = null;
        NewPhone = string.Empty;
        NewAllergies = string.Empty;
        NewHistory = string.Empty;
        NewChronicTags = string.Empty;

        StatusMessage = null;
        ErrorMessage = null;
    }
}
