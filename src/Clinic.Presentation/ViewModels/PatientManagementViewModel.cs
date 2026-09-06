using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
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
public partial class PatientManagementViewModel : ViewModelBase
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

    /// <summary>选中患者的就诊历史（处方记录）</summary>
    public ObservableCollection<PrescriptionHistoryDto> PatientPrescriptionHistory { get; } = new();

    /// <summary>选中患者的就诊次数</summary>
    [ObservableProperty]
    private int _selectedPatientVisitCount;

    /// <summary>选中患者变化时加载就诊历史</summary>
    partial void OnSelectedPatientChanged(PatientDto? value)
    {
        _ = LoadPatientHistoryAsync(value);
    }

    private async Task LoadPatientHistoryAsync(PatientDto? patient)
    {
        PatientPrescriptionHistory.Clear();
        SelectedPatientVisitCount = 0;

        if (patient is null) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var rxSvc = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();
            var history = await rxSvc.GetPrescriptionHistoryAsync(
                searchKeyword: patient.Name,
                fromDate: null,
                toDate: null);

            // 只保留该患者的记录（按姓名匹配可能有同名，这里简单过滤）
            foreach (var p in history.Where(p => p.PatientId == patient.Id)
                         .OrderByDescending(p => p.CreatedAt))
            {
                PatientPrescriptionHistory.Add(p);
            }
            SelectedPatientVisitCount = PatientPrescriptionHistory.Count;
        }
        catch
        {
            // 就诊历史加载失败不影响主流程
        }
    }

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

    /// <summary>自定义标签（逗号分隔，如"孕妇、随访人群"）</summary>
    [ObservableProperty]
    private string _newTags = string.Empty;

    // ── 列表筛选与导出 ──

    /// <summary>标签/过敏史筛选关键词（本地过滤当前列表）</summary>
    [ObservableProperty]
    private string _tagFilter = string.Empty;

    /// <summary>同名患者提示：新建档案时若存在同名患者，提示核对手机号避免重复建档</summary>
    [ObservableProperty]
    private string? _sameNameWarning;

    /// <summary>同名检查防抖令牌</summary>
    private CancellationTokenSource? _sameNameCts;

    /// <summary>当前加载的全部患者（标签筛选的本地数据源）</summary>
    private List<PatientDto> _allLoadedPatients = new();

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

            _allLoadedPatients = (await patientService.GetAllPatientsAsync()).ToList();
            ApplyTagFilter();
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

            var keyword = SearchKeyword.Trim();

            // 纯数字 → 手机号查找；否则 → 姓名模糊搜索
            if (keyword.All(char.IsDigit) && keyword.Length >= 4)
            {
                var patient = await patientService.FindByPhoneAsync(keyword);
                _allLoadedPatients = patient is not null ? [patient] : [];
                ApplyTagFilter();
                StatusMessage = Patients.Count > 0
                    ? $"找到 {Patients.Count} 条记录"
                    : "未找到匹配的患者";
            }
            else
            {
                var results = await patientService.SearchByNameAsync(keyword);
                _allLoadedPatients = results.ToList();
                ApplyTagFilter();
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

    /// <summary>按标签/过敏史关键词本地过滤当前列表</summary>
    private void ApplyTagFilter()
    {
        var filter = TagFilter.Trim();
        Patients.Clear();

        IEnumerable<PatientDto> source = _allLoadedPatients;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            source = source.Where(p =>
                (p.Tags is not null && p.Tags.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                (p.ChronicTags is not null && p.ChronicTags.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                (p.Allergies is not null && p.Allergies.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var p in source)
            Patients.Add(p);
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
                string.IsNullOrWhiteSpace(NewChronicTags) ? null : NewChronicTags.Trim(),
                tags: string.IsNullOrWhiteSpace(NewTags) ? null : NewTags.Trim());

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
            NewTags = string.Empty;
            SameNameWarning = null;

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
    partial void OnNewNameChanged(string value)
    {
        // 同名检查（P1-3 重复建档拦截）：输入姓名时防抖查询同名患者，提示核对手机号
        _sameNameCts?.Cancel();

        if (IsEditMode || string.IsNullOrWhiteSpace(value))
        {
            SameNameWarning = null;
            return;
        }

        var cts = new CancellationTokenSource();
        _sameNameCts = cts;
        _ = CheckSameNameAsync(value.Trim(), cts.Token);
    }

    private async Task CheckSameNameAsync(string name, CancellationToken ct)
    {
        try
        {
            await Task.Delay(400, ct);

            using var scope = _scopeFactory.CreateScope();
            var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();
            var matches = await patientService.SearchByNameAsync(name, ct);

            if (ct.IsCancellationRequested)
                return;

            SameNameWarning = matches.Count > 0
                ? $"存在 {matches.Count} 位同名患者（{string.Join("、", matches.Take(3).Select(m => m.Phone))}），请核对手机号避免重复建档"
                : null;
        }
        catch (OperationCanceledException)
        {
            // 用户继续输入，取消旧查询
        }
        catch
        {
            // 同名查询失败不影响建档
        }
    }

    /// <summary>标签/过敏史筛选：本地过滤当前列表（不重新查库）</summary>
    partial void OnTagFilterChanged(string value) => ApplyTagFilter();

    /// <summary>手机号实时验证（中国手机号：11位，1开头，第二位3-9）</summary>
    partial void OnNewPhoneChanged(string value)
    {        if (string.IsNullOrWhiteSpace(value))
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
        NewTags = SelectedPatient.Tags ?? string.Empty;
        SameNameWarning = null;

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
                string.IsNullOrWhiteSpace(NewChronicTags) ? null : NewChronicTags.Trim(),
                tags: string.IsNullOrWhiteSpace(NewTags) ? null : NewTags.Trim());

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
        NewTags = string.Empty;
        SameNameWarning = null;

        StatusMessage = null;
        ErrorMessage = null;
    }

    /// <summary>导出当前列表为 CSV（Excel 可直接打开，UTF-8 BOM）</summary>
    [RelayCommand]
    private void ExportCsv()
    {
        if (Patients.Count == 0)
        {
            StatusMessage = "当前列表为空，无可导出数据";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV 文件 (*.csv)|*.csv",
            FileName = $"患者列表_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            Title = "导出患者列表"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("ID,姓名,性别,出生日期,联系电话,过敏史,病史,基础疾病,标签");

            foreach (var p in Patients)
            {
                sb.AppendLine(string.Join(",",
                    p.Id.ToString(),
                    CsvEscape(p.Name),
                    CsvEscape(p.Gender),
                    p.Dob?.ToString("yyyy-MM-dd") ?? string.Empty,
                    CsvEscape(p.Phone),
                    CsvEscape(p.Allergies ?? string.Empty),
                    CsvEscape(p.History ?? string.Empty),
                    CsvEscape(p.ChronicTags ?? string.Empty),
                    CsvEscape(p.Tags ?? string.Empty)));
            }

            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
            StatusMessage = $"已导出 {Patients.Count} 条记录：{dialog.FileName}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"导出失败：{ExceptionFormatter.GetMessage(ex)}";
        }
    }

    /// <summary>CSV 字段转义：含逗号/引号/换行时加引号包裹</summary>
    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
