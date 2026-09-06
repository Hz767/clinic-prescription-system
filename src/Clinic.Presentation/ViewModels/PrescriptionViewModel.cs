using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Clinic.Presentation.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation.ViewModels;

/// <summary>
/// 处方开具 ViewModel。流程：搜索患者 → 选择药品 → 添加明细 → 创建处方 → 保存。
/// 处方创建后处于"草稿"状态，可继续添加明细，最终保存时计算总金额。
/// </summary>
public partial class PrescriptionViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IUserSession _session;
    private readonly IConfiguration _configuration;
    private readonly ILlmService _llmService;

    private long? _draftPrescriptionId;
    private long? _savedPrescriptionId;

    /// <summary>当前是否处于草稿状态（处方已创建但未保存，可编辑明细）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveItemCommand))]
    [NotifyCanExecuteChangedFor(nameof(SavePrescriptionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAndReviewCommand))]
    private bool _isDraft;

    /// <summary>保存并审核后显示"前往收费"按钮</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoToBillingCommand))]
    private bool _canGoToBilling;

    /// <summary>导航到收费页面的事件（由 MainViewModel 订阅）</summary>
    public event Action<long>? NavigateToBillingRequested;

    // ── 患者区 ──

    [ObservableProperty]
    private string _patientSearchKeyword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchPatientCommand))]
    [NotifyCanExecuteChangedFor(nameof(SavePrescriptionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAndReviewCommand))]
    private bool _isBusy;

    public ObservableCollection<PatientDto> MatchedPatients { get; } = new();

    /// <summary>实时过滤后的患者列表（用于下拉联想）</summary>
    public ObservableCollection<PatientDto> FilteredPatients { get; } = new();

    [ObservableProperty]
    private PatientDto? _selectedPatient;

    /// <summary>选中患者的年龄显示文本（根据出生日期计算，如"35岁"或"未知"）</summary>
    public string SelectedPatientAgeText =>
        SelectedPatient?.Dob is { } dob
            ? $"{DateTime.Today.Year - dob.Year - (DateTime.Today.DayOfYear < dob.DayOfYear ? 1 : 0)}岁"
            : "未知";

    /// <summary>选中患者的过敏史显示文本</summary>
    public string SelectedPatientAllergyText =>
        string.IsNullOrWhiteSpace(SelectedPatient?.Allergies) ? "无" : SelectedPatient.Allergies;

    /// <summary>选中患者的基础疾病显示文本</summary>
    public string SelectedPatientChronicText =>
        string.IsNullOrWhiteSpace(SelectedPatient?.ChronicTags) ? "无" : SelectedPatient.ChronicTags;

    /// <summary>是否有过敏史（用于顶部信息条显示过敏标签）</summary>
    public bool HasAllergy => !string.IsNullOrWhiteSpace(SelectedPatient?.Allergies);

    /// <summary>是否有基础疾病（用于顶部信息条显示慢病标签）</summary>
    public bool HasChronic => !string.IsNullOrWhiteSpace(SelectedPatient?.ChronicTags);

    /// <summary>是否显示下拉联想框</summary>
    [ObservableProperty]
    private bool _showPatientDropdown;

    /// <summary>搜索无匹配结果时显示"新建患者"入口</summary>
    [ObservableProperty]
    private bool _hasNoResults;

    /// <summary>是否处于快速建档模式</summary>
    [ObservableProperty]
    private bool _isQuickRegistration;

    [ObservableProperty]
    private string _newPatientName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateNewPatientCommand))]
    private string _newPatientPhone = string.Empty;

    [ObservableProperty]
    private string _newPatientGender = "男";

    /// <summary>快速建档手机号验证错误提示（null 表示无错误）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateNewPatientCommand))]
    private string? _newPatientPhoneError;

    /// <summary>快速建档 - 出生日期（可选，用于计算年龄和儿科用药；DatePicker 使用 DateTime）</summary>
    [ObservableProperty]
    private DateTime? _newPatientDob;

    /// <summary>快速建档 - 过敏史（可选，处方保存时用于药物过敏检查）</summary>
    [ObservableProperty]
    private string? _newPatientAllergies;

    /// <summary>快速建档 - 慢病标签（可选，如高血压、糖尿病等，用于药物禁忌检查）</summary>
    [ObservableProperty]
    private string? _newPatientChronicTags;

    private CancellationTokenSource? _searchCts;

    // ── 本次就诊体征（实时变量，非患者长期数据，每次新建处方时清空） ──

    [ObservableProperty] private decimal? _patientWeight;
    [ObservableProperty] private decimal? _patientTemperature;
    [ObservableProperty] private int? _patientSystolicBP;
    [ObservableProperty] private int? _patientDiastolicBP;
    [ObservableProperty] private int? _patientHeartRate;

    // ── 体征异常值实时检查 ──

    /// <summary>体征异常警告文本（null 表示无异常）</summary>
    [ObservableProperty] private string? _vitalSignsWarning;

    /// <summary>体征警告级别：0=正常，1=异常（黄色警告），2=超范围（红色错误）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SavePrescriptionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAndReviewCommand))]
    private int _vitalSignsWarningLevel;

    /// <summary>是否有体征警告（控制警告区域显示）</summary>
    public bool HasVitalSignsWarning => VitalSignsWarningLevel > 0;

    // ── 历史病历（选中患者后自动加载，帮助医生快速了解病史） ──

    /// <summary>选中患者的处方/病历历史记录</summary>
    public ObservableCollection<PrescriptionHistoryDto> PatientHistory { get; } = new();

    /// <summary>是否有历史记录（控制历史区域显示）</summary>
    [ObservableProperty] private bool _hasPatientHistory;

    // ── 药品区 ──

    /// <summary>全部药品目录（未过滤）</summary>
    private List<DrugDto> _allDrugs = new();

    /// <summary>当前显示的药品列表（经过过滤）</summary>
    public ObservableCollection<DrugDto> Drugs { get; } = new();

    [ObservableProperty]
    private DrugDto? _selectedDrug;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(QuickAddDrugCommand))]
    private string _drugFilter = string.Empty;

    /// <summary>药品分类筛选：0=全部, 1=抗菌药, 2=特殊管理(限制/特殊级抗菌药+毒性药品), 3=低库存</summary>
    [ObservableProperty]
    private int _drugCategoryFilter;

    // ── 处方明细添加表单 ──

    [ObservableProperty]
    private decimal _dose = 1m;

    [ObservableProperty]
    private string _doseUnit = "片";

    [ObservableProperty]
    private string _frequency = "每日三次";

    [ObservableProperty]
    private string _route = "口服";

    [ObservableProperty]
    private int _durationDays = 3;

    [ObservableProperty]
    private decimal _qty = 9m;

    // ── 处方信息 ──

    public ObservableCollection<PrescriptionItemDto> PrescriptionItems { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreatePrescriptionCommand))]
    private string _diagnosisText = string.Empty;

    /// <summary>主诉（可选，在诊断前填写）</summary>
    [ObservableProperty]
    private string? _chiefComplaint;

    /// <summary>常见主诉列表（点击添加，可多选累积）</summary>
    public ObservableCollection<string> CommonChiefComplaints { get; } = new()
    {
        "发热1天", "发热3天", "咳嗽伴发热", "咽痛2天", "鼻塞流涕",
        "头痛3天", "头晕1周", "胸闷气短", "心悸", "胸痛",
        "腹痛1天", "腹泻2天", "恶心呕吐", "腹胀", "便秘1周",
        "腰背疼痛", "关节肿痛", "颈肩疼痛", "肢体麻木",
        "皮疹3天", "皮肤瘙痒", "失眠1月", "乏力2周",
        "尿频尿急", "眼红分泌物增多", "耳鸣听力下降",
        "月经不调", "痛经", "外伤后疼痛",
    };

    /// <summary>常见诊断下拉列表（点击添加，可多选累积）</summary>
    public ObservableCollection<string> CommonDiagnoses { get; } = new()
    {
        // 呼吸系统
        "上呼吸道感染", "急性支气管炎", "慢性支气管炎急性发作", "社区获得性肺炎",
        "急性咽炎", "急性扁桃体炎", "急性鼻窦炎", "过敏性鼻炎", "哮喘急性发作",
        // 心血管系统
        "高血压病", "冠心病", "心绞痛", "心律失常", "心力衰竭",
        // 内分泌代谢
        "2型糖尿病", "甲状腺功能亢进", "高脂血症", "痛风急性发作",
        // 消化系统
        "急性胃肠炎", "慢性胃炎", "功能性消化不良", "消化性溃疡", "便秘",
        "急性阑尾炎", "胆结石", "脂肪肝",
        // 泌尿系统
        "尿路感染", "肾结石", "前列腺增生",
        // 皮肤科
        "湿疹", "过敏性皮炎", "荨麻疹", "带状疱疹", "真菌性皮肤病", "痤疮",
        // 神经系统
        "偏头痛", "紧张性头痛", "脑供血不足", "面神经炎",
        // 骨科
        "颈椎病", "腰椎间盘突出", "骨关节炎", "类风湿关节炎", "软组织挫伤",
        "肩周炎", "腱鞘炎", "踝关节扭伤",
        // 五官科
        "急性结膜炎", "口腔溃疡", "急性中耳炎", "牙龈炎", "慢性鼻炎",
        // 妇科
        "痛经", "阴道炎", "月经不调",
        // 其他
        "缺铁性贫血", "焦虑状态", "失眠", "维生素D缺乏", "低钾血症",
    };

    [ObservableProperty]
    private int _prescriptionType;

    [ObservableProperty]
    private string? _extendedReason;

    // ── 状态 ──
/// <summary>是否有已保存的处方可生成 PDF</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GeneratePdfCommand))]
    private bool _isPdfAvailable;

    // ── 模块折叠状态（医生完成病历后可折叠，聚焦处方开具） ──
    [ObservableProperty] private bool _isVitalSignsExpanded = true;
    [ObservableProperty] private bool _isHistoryExpanded = true;
    [ObservableProperty] private bool _isChiefComplaintExpanded = true;
    [ObservableProperty] private bool _isDiagnosisExpanded = true;

    /// <summary>诊疗费（默认10元，可修改）</summary>
    [ObservableProperty]
    private decimal _consultationFee = 10m;

    partial void OnConsultationFeeChanged(decimal value)
    {
        OnPropertyChanged(nameof(TotalAmount));
    }

    /// <summary>总金额 = 药品金额 + 诊疗费</summary>
    public decimal TotalAmount => PrescriptionItems.Sum(i => i.Subtotal) + ConsultationFee;

    /// <summary>当前登录医生姓名（用于病历/处方签名显示）</summary>
    public string DoctorName => _session.DisplayName ?? "—";

    public PrescriptionViewModel(IServiceScopeFactory scopeFactory, IUserSession session,
        IConfiguration configuration, ILlmService llmService)
    {
        _scopeFactory = scopeFactory;
        _session = session;
        _configuration = configuration;
        _llmService = llmService;

        PrescriptionItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TotalAmount));
            SavePrescriptionCommand.NotifyCanExecuteChanged();
            SaveAndReviewCommand.NotifyCanExecuteChanged();
        };
    }

    /// <summary>输入关键词变化时触发防抖自动搜索（300ms 延迟）</summary>
    partial void OnPatientSearchKeywordChanged(string value)
    {
        _searchCts?.Cancel();
        if (string.IsNullOrWhiteSpace(value))
        {
            FilteredPatients.Clear();
            ShowPatientDropdown = false;
            HasNoResults = false;
            IsQuickRegistration = false;
            return;
        }

        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        var keyword = value.Trim();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token);
                if (token.IsCancellationRequested) return;

                using var scope = _scopeFactory.CreateScope();
                var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();

                var results = new List<PatientDto>();

                // 同时按姓名和手机号搜索
                var nameResults = await patientService.SearchByNameAsync(keyword, token);
                results.AddRange(nameResults);

                // 如果输入是纯数字且长度>=4，也尝试精确手机号匹配
                if (keyword.All(char.IsDigit) && keyword.Length >= 4)
                {
                    var phoneResult = await patientService.FindByPhoneAsync(keyword, token);
                    if (phoneResult is not null && !results.Any(p => p.Id == phoneResult.Id))
                        results.Add(phoneResult);
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    FilteredPatients.Clear();
                    foreach (var p in results)
                        FilteredPatients.Add(p);

                    HasNoResults = FilteredPatients.Count == 0;
                    ShowPatientDropdown = true;
                    IsQuickRegistration = false;
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ErrorMessage = $"搜索患者失败：{ExceptionFormatter.GetMessage(ex)}";
                });
            }
        }, token);
    }

    // ── 体征变更时触发实时验证 ──

    partial void OnPatientWeightChanged(decimal? value) => ValidateVitalSigns();
    partial void OnPatientTemperatureChanged(decimal? value) => ValidateVitalSigns();
    partial void OnPatientSystolicBPChanged(int? value) => ValidateVitalSigns();
    partial void OnPatientDiastolicBPChanged(int? value) => ValidateVitalSigns();
    partial void OnPatientHeartRateChanged(int? value) => ValidateVitalSigns();

    /// <summary>
    /// 体征异常值实时检查。
    /// 级别 0=正常，1=异常（黄色警告），2=超范围（红色错误，阻止保存）。
    /// 医学参考范围基于成人标准，儿童/特殊人群需医生结合临床判断。
    /// </summary>
    private void ValidateVitalSigns()
    {
        var warnings = new List<string>();
        var maxLevel = 0;

        // 体重：硬限制 0.1-500kg，异常范围 <2 或 >200
        if (PatientWeight.HasValue)
        {
            if (PatientWeight.Value < 0.1m || PatientWeight.Value > 500m)
            {
                warnings.Add($"体重 {PatientWeight.Value}kg 超出有效范围（0.1-500kg）");
                maxLevel = Math.Max(maxLevel, 2);
            }
            else if (PatientWeight.Value < 2m || PatientWeight.Value > 200m)
            {
                warnings.Add($"体重 {PatientWeight.Value}kg 偏{'低' + (PatientWeight.Value < 2m ? "（正常2-200kg）" : "高（正常2-200kg）")}");
                maxLevel = Math.Max(maxLevel, 1);
            }
        }

        // 体温：硬限制 30-45°C，正常 36-37.3°C
        if (PatientTemperature.HasValue)
        {
            var t = PatientTemperature.Value;
            if (t < 30m || t > 45m)
            {
                warnings.Add($"体温 {t}°C 超出有效范围（30-45°C）");
                maxLevel = Math.Max(maxLevel, 2);
            }
            else if (t >= 39m)
            {
                warnings.Add($"体温 {t}°C 高热（≥39°C），建议紧急处理");
                maxLevel = Math.Max(maxLevel, 1);
            }
            else if (t >= 37.3m)
            {
                warnings.Add($"体温 {t}°C 发热（37.3-38.9°C）");
                maxLevel = Math.Max(maxLevel, 1);
            }
            else if (t < 35m)
            {
                warnings.Add($"体温 {t}°C 体温过低（<35°C）");
                maxLevel = Math.Max(maxLevel, 1);
            }
        }

        // 收缩压：硬限制 40-250mmHg，正常 90-140
        if (PatientSystolicBP.HasValue)
        {
            var s = PatientSystolicBP.Value;
            if (s < 40 || s > 250)
            {
                warnings.Add($"收缩压 {s}mmHg 超出有效范围（40-250mmHg）");
                maxLevel = Math.Max(maxLevel, 2);
            }
            else if (s > 180)
            {
                warnings.Add($"收缩压 {s}mmHg 重度高血压（>180mmHg）");
                maxLevel = Math.Max(maxLevel, 1);
            }
            else if (s > 140)
            {
                warnings.Add($"收缩压 {s}mmHg 偏高（正常90-140mmHg）");
                maxLevel = Math.Max(maxLevel, 1);
            }
            else if (s < 90)
            {
                warnings.Add($"收缩压 {s}mmHg 偏低（正常90-140mmHg）");
                maxLevel = Math.Max(maxLevel, 1);
            }
        }

        // 舒张压：硬限制 20-150mmHg，正常 60-90
        if (PatientDiastolicBP.HasValue)
        {
            var d = PatientDiastolicBP.Value;
            if (d < 20 || d > 150)
            {
                warnings.Add($"舒张压 {d}mmHg 超出有效范围（20-150mmHg）");
                maxLevel = Math.Max(maxLevel, 2);
            }
            else if (d > 110)
            {
                warnings.Add($"舒张压 {d}mmHg 重度高血压（>110mmHg）");
                maxLevel = Math.Max(maxLevel, 1);
            }
            else if (d > 90)
            {
                warnings.Add($"舒张压 {d}mmHg 偏高（正常60-90mmHg）");
                maxLevel = Math.Max(maxLevel, 1);
            }
            else if (d < 60)
            {
                warnings.Add($"舒张压 {d}mmHg 偏低（正常60-90mmHg）");
                maxLevel = Math.Max(maxLevel, 1);
            }
        }

        // 收缩压 < 舒张压 时提示逻辑错误
        if (PatientSystolicBP.HasValue && PatientDiastolicBP.HasValue
            && PatientSystolicBP.Value <= PatientDiastolicBP.Value)
        {
            warnings.Add($"收缩压应大于舒张压，请检查输入");
            maxLevel = Math.Max(maxLevel, 2);
        }

        // 心率：硬限制 20-250次/分，正常 60-100
        if (PatientHeartRate.HasValue)
        {
            var hr = PatientHeartRate.Value;
            if (hr < 20 || hr > 250)
            {
                warnings.Add($"心率 {hr}次/分 超出有效范围（20-250次/分）");
                maxLevel = Math.Max(maxLevel, 2);
            }
            else if (hr > 150)
            {
                warnings.Add($"心率 {hr}次/分 严重心动过速（>150次/分）");
                maxLevel = Math.Max(maxLevel, 1);
            }
            else if (hr > 100)
            {
                warnings.Add($"心率 {hr}次/分 偏快（正常60-100次/分）");
                maxLevel = Math.Max(maxLevel, 1);
            }
            else if (hr < 60)
            {
                warnings.Add($"心率 {hr}次/分 偏慢（正常60-100次/分）");
                maxLevel = Math.Max(maxLevel, 1);
            }
        }

        VitalSignsWarningLevel = maxLevel;
        VitalSignsWarning = warnings.Count > 0 ? string.Join("；", warnings) : null;
        OnPropertyChanged(nameof(HasVitalSignsWarning));
    }

    /// <summary>创建处方（草稿状态）</summary>
    [RelayCommand(CanExecute = nameof(CanCreatePrescription))]
    private async Task CreatePrescriptionAsync()
    {
        if (SelectedPatient is null || _session.UserId is null)
        {
            ErrorMessage = "请先选择患者并确认已登录";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var prescriptionService = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();

            _draftPrescriptionId = await prescriptionService.CreatePrescriptionAsync(
                SelectedPatient.Id,
                _session.UserId.Value,
                ChiefComplaint,
                DiagnosisText.Trim(),
                null,
                PrescriptionType,
                ExtendedReason,
                PatientWeight, PatientTemperature,
                PatientSystolicBP, PatientDiastolicBP, PatientHeartRate,
                ConsultationFee);

            IsDraft = true;
            StatusMessage = $"处方已创建（编号待生成），可继续添加药品明细";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"创建处方失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCreatePrescription()
        => !IsBusy && _draftPrescriptionId is null && SelectedPatient is not null && !string.IsNullOrWhiteSpace(DiagnosisText);

    /// <summary>添加处方明细</summary>
    [RelayCommand(CanExecute = nameof(CanAddItem))]
    private async Task AddItemAsync()
    {
        if (SelectedDrug is null || _draftPrescriptionId is null)
            return;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var prescriptionService = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();

            var itemId = await prescriptionService.AddPrescriptionItemAsync(
                _draftPrescriptionId.Value,
                SelectedDrug.Id,
                Dose,
                DoseUnit,
                Frequency,
                Route,
                DurationDays,
                Qty);

            if (itemId > 0)
            {
                // 本地添加到列表（使用服务返回的真实明细 ID）
                var unitPrice = SelectedDrug.RetailPriceRef ?? 0m;
                var packQty = PrescriptionItemDto.ParsePackQuantity(SelectedDrug.Spec);
                var item = new PrescriptionItemDto
                {
                    Id = itemId,
                    DrugId = SelectedDrug.Id,
                    DrugName = SelectedDrug.GenericNameCn,
                    Spec = SelectedDrug.Spec,
                    Dose = Dose,
                    DoseUnit = DoseUnit,
                    Frequency = Frequency,
                    Route = Route,
                    DurationDays = DurationDays,
                    Qty = Qty,
                    UnitPrice = unitPrice,
                    PackQuantity = packQty
                };
                item.RecalculateSubtotalOnly();
                PrescriptionItems.Add(item);

                StatusMessage = $"已添加：{SelectedDrug.GenericNameCn} × {Qty}{DoseUnit}（{packQty}{DoseUnit}/盒，¥{unitPrice}/盒）";
            }
            else
            {
                ErrorMessage = "添加明细失败，处方可能已保存或作废";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"添加明细失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAddItem()
        => !IsBusy && SelectedDrug is not null && _draftPrescriptionId is not null;

    /// <summary>确保处方草稿已创建（自动创建，无需医生手动操作）</summary>
    private async Task<bool> EnsureDraftPrescriptionAsync()
    {
        if (_draftPrescriptionId is not null)
            return true;

        if (SelectedPatient is null)
        {
            ErrorMessage = "请先选择患者";
            return false;
        }

        if (string.IsNullOrWhiteSpace(DiagnosisText))
        {
            ErrorMessage = "请先填写诊断信息";
            return false;
        }

        if (_session.UserId is null)
        {
            ErrorMessage = "登录状态异常，请重新登录";
            return false;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var prescriptionService = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();

            _draftPrescriptionId = await prescriptionService.CreatePrescriptionAsync(
                SelectedPatient.Id,
                _session.UserId.Value,
                ChiefComplaint,
                DiagnosisText.Trim(),
                null,
                PrescriptionType,
                ExtendedReason,
                PatientWeight, PatientTemperature,
                PatientSystolicBP, PatientDiastolicBP, PatientHeartRate,
                ConsultationFee);

            IsDraft = true;
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"创建处方失败：{ExceptionFormatter.GetMessage(ex)}";
            return false;
        }
    }

    /// <summary>AI药师审核（仅提供参考意见，不阻断流程，分级分色显示）</summary>
    private async Task RunAiPharmacistReviewAsync()
    {
        if (_draftPrescriptionId is null || PrescriptionItems.Count == 0)
            return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var prescriptionService = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();
            var suggestions = await prescriptionService.GetAiReviewSuggestionsAsync(_draftPrescriptionId.Value);

            // 解析建议级别
            AiPharmacistAdvice = suggestions;
            HasAiAdvice = true;

            if (suggestions.Contains("严重") || suggestions.Contains("禁忌") || suggestions.Contains("阻断"))
                AiAdviceLevel = 3;
            else if (suggestions.Contains("警告") || suggestions.Contains("注意") || suggestions.Contains("交互"))
                AiAdviceLevel = 2;
            else if (suggestions.Contains("提示") || suggestions.Contains("建议") || suggestions.Contains("参考"))
                AiAdviceLevel = 1;
            else
                AiAdviceLevel = 0;
        }
        catch
        {
            // AI审核失败不阻断流程
        }
    }

    /// <summary>删除处方明细（仅草稿状态可删除）</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveItem))]
    private async Task RemoveItemAsync(PrescriptionItemDto? item)
    {
        if (item is null || _draftPrescriptionId is null)
            return;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var prescriptionService = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();

            await prescriptionService.RemovePrescriptionItemAsync(
                _draftPrescriptionId.Value, item.Id);

            PrescriptionItems.Remove(item);
            StatusMessage = $"已删除：{item.DrugName}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"删除明细失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRemoveItem() => !IsBusy && IsDraft;

    /// <summary>内联编辑后持久化处方明细到数据库（由 DataGrid CellEditEnding 事件调用）</summary>
    public async Task UpdateItemInlineAsync(PrescriptionItemDto item, bool isQtyColumn = false)
    {
        if (item is null || _draftPrescriptionId is null)
            return;

        // 数量列被修改时只重算金额，不覆盖用户手动输入的数量
        if (isQtyColumn)
            item.RecalculateSubtotalOnly();
        else
            item.Recalculate();
        OnPropertyChanged(nameof(TotalAmount));

        if (item.Id == 0)
            return; // 新添加的明细尚未持久化 ID，跳过

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var prescriptionService = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();
            await prescriptionService.UpdatePrescriptionItemAsync(
                _draftPrescriptionId.Value, item.Id,
                item.Dose, item.DoseUnit, item.Frequency, item.Route,
                item.DurationDays, item.Qty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"更新明细失败：{ExceptionFormatter.GetMessage(ex)}";
        }
    }

    /// <summary>保存处方（计算总金额 + 事务提交 + 自动生成PDF）。
    /// 保存前先执行阻断校验（过敏/交互/禁忌症），存在阻断项时弹出临床覆盖确认对话框，
    /// 医生填写理由后放行保存，理由写入审计日志。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SavePrescriptionAsync()
    {
        if (_draftPrescriptionId is null)
            return;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var prescriptionService = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();

            // 步骤0：保存前阻断校验 → 存在阻断项时要求填写临床覆盖理由
            var blockers = await prescriptionService.GetPrescriptionBlockersAsync(_draftPrescriptionId.Value);
            string? overrideReason = null;
            if (blockers.Count > 0)
            {
                var reason = Views.OverrideReasonDialog.Show(blockers);
                if (reason is null)
                {
                    ErrorMessage = "处方存在阻断性问题，未填写覆盖理由，已取消保存。请调整处方或填写临床覆盖理由后重试。";
                    return;
                }
                overrideReason = reason;
            }

            var success = await prescriptionService.SavePrescriptionAsync(
                _draftPrescriptionId.Value, overrideReason);

            if (success)
            {
                _savedPrescriptionId = _draftPrescriptionId;
                _draftPrescriptionId = null;
                IsDraft = false;
                IsPdfAvailable = true;

                // 自动生成 PDF（《处方管理办法》要求处方保存后即生成可打印处方）
                await GeneratePdfInternalAsync(prescriptionService);

                StatusMessage = overrideReason is null
                    ? $"处方已保存，总金额：{TotalAmount:F2} 元。PDF已自动生成，可点击「前往收费」完成收费"
                    : $"处方已保存（含临床覆盖，理由已记入审计日志），总金额：{TotalAmount:F2} 元。可点击「前往收费」";
                CanGoToBilling = true;
            }
            else
            {
                ErrorMessage = "保存失败，处方可能已被作废";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存处方失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSave() => !IsBusy && _draftPrescriptionId is not null && PrescriptionItems.Count > 0
        && VitalSignsWarningLevel < 2;

    /// <summary>保存并审核（个人诊所医生兼任药师，一键完成保存+审核）</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAndReviewAsync()
    {
        if (_draftPrescriptionId is null)
            return;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var prescriptionService = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();

            // 步骤1：保存前阻断校验 → 存在阻断项时要求填写临床覆盖理由
            var blockers = await prescriptionService.GetPrescriptionBlockersAsync(_draftPrescriptionId.Value);
            string? overrideReason = null;
            if (blockers.Count > 0)
            {
                var reason = Views.OverrideReasonDialog.Show(blockers);
                if (reason is null)
                {
                    ErrorMessage = "处方存在阻断性问题，未填写覆盖理由，已取消保存。请调整处方或填写临床覆盖理由后重试。";
                    return;
                }
                overrideReason = reason;
            }

            // 步骤2：保存处方
            var saved = await prescriptionService.SavePrescriptionAsync(
                _draftPrescriptionId.Value, overrideReason);
            if (!saved)
            {
                ErrorMessage = "保存失败，处方可能已被作废";
                return;
            }

            _savedPrescriptionId = _draftPrescriptionId;
            _draftPrescriptionId = null;
            IsDraft = false;
            IsPdfAvailable = true;

            // 步骤3：自动生成 PDF
            await GeneratePdfInternalAsync(prescriptionService);

            // 步骤4：药师审核（个人诊所医生兼任药师）
            var reviewed = await prescriptionService.ReviewPrescriptionAsync(_savedPrescriptionId.Value, "医生自审");
            if (reviewed)
            {
                StatusMessage = $"处方已保存并审核通过，总金额：{TotalAmount:F2} 元。PDF已生成，请点击「前往收费」";
                CanGoToBilling = true;
            }
            else
            {
                StatusMessage = $"处方已保存，但审核失败。请前往收费管理页面手动审核。总金额：{TotalAmount:F2} 元";
                CanGoToBilling = true;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存并审核失败：{ExceptionFormatter.GetMessage(ex)}";
            // 保存可能已成功，允许前往收费
            if (_savedPrescriptionId is not null)
            {
                CanGoToBilling = true;
                StatusMessage = "处方已保存但审核失败，可点击「前往收费」手动处理";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>前往收费页面（携带处方ID自动填充）</summary>
    [RelayCommand(CanExecute = nameof(CanNavigateToBilling))]
    private void GoToBilling()
    {
        if (_savedPrescriptionId is not null)
            NavigateToBillingRequested?.Invoke(_savedPrescriptionId.Value);
    }

    private bool CanNavigateToBilling() => CanGoToBilling;

    /// <summary>内部生成PDF（不重设IsBusy）</summary>
    private async Task GeneratePdfInternalAsync(IPrescriptionService prescriptionService)
    {
        try
        {
            var outputDir = _configuration["Pdf:OutputDir"] ?? "Pdf";
            var fullDir = Path.Combine(AppContext.BaseDirectory, outputDir);
            Directory.CreateDirectory(fullDir);
            var pdfPath = await prescriptionService.GeneratePrescriptionPdfAsync(_savedPrescriptionId!.Value, fullDir);
            if (pdfPath is not null && File.Exists(pdfPath))
            {
                Process.Start(new ProcessStartInfo(pdfPath) { UseShellExecute = true });
            }
        }
        catch
        {
            // PDF生成失败不阻断主流程
        }
    }

    /// <summary>生成处方 PDF 并在默认阅读器中打开</summary>
    [RelayCommand(CanExecute = nameof(CanGeneratePdf))]
    private async Task GeneratePdfAsync()
    {
        if (_savedPrescriptionId is null)
            return;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var prescriptionService = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();

            var outputDir = _configuration["Pdf:OutputDir"] ?? "Pdf";
            var fullDir = Path.Combine(AppContext.BaseDirectory, outputDir);
            Directory.CreateDirectory(fullDir);

            var pdfPath = await prescriptionService.GeneratePrescriptionPdfAsync(
                _savedPrescriptionId.Value, fullDir);

            if (pdfPath is not null && File.Exists(pdfPath))
            {
                StatusMessage = $"PDF 已生成：{Path.GetFileName(pdfPath)}";
                // 在默认 PDF 阅读器中打开
                Process.Start(new ProcessStartInfo(pdfPath) { UseShellExecute = true });
            }
            else
            {
                ErrorMessage = "PDF 生成失败：处方不存在";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"生成 PDF 失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanGeneratePdf() => !IsBusy && IsPdfAvailable;

    /// <summary>新建处方：清空所有输入，开始新的处方开具</summary>
    [RelayCommand]
    private void NewPrescription()
    {
        _draftPrescriptionId = null;
        _savedPrescriptionId = null;
        IsDraft = false;
        IsPdfAvailable = false;
        CanGoToBilling = false;
        PrescriptionItems.Clear();
        DiagnosisText = string.Empty;
        ChiefComplaint = null;
        ExtendedReason = null;
        SelectedPatient = null;
        SelectedDrug = null;
        DrugFilter = string.Empty;
        MatchedPatients.Clear();
        FilteredPatients.Clear();
        ShowPatientDropdown = false;
        HasNoResults = false;
        IsQuickRegistration = false;
        NewPatientName = string.Empty;
        NewPatientPhone = string.Empty;
        NewPatientDob = null;
        NewPatientAllergies = null;
        NewPatientChronicTags = null;
        LlmPrescriptionInput = string.Empty;
        AiDiagnosisResult = null;
        HasAiDiagnosisResult = false;
        AiPharmacistAdvice = null;
        HasAiAdvice = false;
        AiAdviceLevel = 0;
        PatientWeight = null;
        PatientTemperature = null;
        PatientSystolicBP = null;
        PatientDiastolicBP = null;
        PatientHeartRate = null;
        PatientHistory.Clear();
        HasPatientHistory = false;
        StatusMessage = null;
        ErrorMessage = null;
    }

    /// <summary>点击常见主诉标签，添加到主诉文本（可多选累积，自动用分号分隔）</summary>
    [RelayCommand]
    private void AddChiefComplaint(string? complaint)
    {
        if (string.IsNullOrWhiteSpace(complaint))
            return;

        if (ChiefComplaint is not null && ChiefComplaint.Contains(complaint))
        {
            StatusMessage = $"「{complaint}」已在主诉中";
            return;
        }

        ChiefComplaint = string.IsNullOrWhiteSpace(ChiefComplaint)
            ? complaint
            : $"{ChiefComplaint}；{complaint}";
        StatusMessage = $"已添加主诉：{complaint}";
    }

    /// <summary>点击常见诊断标签，添加到诊断文本（可多选累积，自动用分号分隔）</summary>
    [RelayCommand]
    private void AddDiagnosis(string? diagnosis)
    {
        if (string.IsNullOrWhiteSpace(diagnosis))
            return;

        // 避免重复添加
        if (DiagnosisText.Contains(diagnosis))
        {
            StatusMessage = $"「{diagnosis}」已在诊断中";
            return;
        }

        DiagnosisText = string.IsNullOrWhiteSpace(DiagnosisText)
            ? diagnosis
            : $"{DiagnosisText}；{diagnosis}";
        StatusMessage = $"已添加：{diagnosis}（可继续选择或手动输入）";
    }

    /// <summary>AI生成规范格式病历（基于当前诊断文本，生成后需医生确认）</summary>
    [RelayCommand(CanExecute = nameof(CanGenerateMedicalRecord))]
    private async Task GenerateMedicalRecordAsync()
    {
        // 检查AI状态
        try
        {
            var status = await _llmService.GetStatusAsync();
            if (!status.IsAvailable)
            {
                ErrorMessage = "AI辅助未就绪，请先在顶部点击「启用AI辅助」加载大模型";
                return;
            }
        }
        catch
        {
            ErrorMessage = "AI辅助服务不可用，请检查 llama-server 是否正常运行";
            return;
        }

        // 至少需要主诉或诊断之一
        if (string.IsNullOrWhiteSpace(ChiefComplaint) && string.IsNullOrWhiteSpace(DiagnosisText))
        {
            ErrorMessage = "请先填写主诉或诊断，AI才能生成规范病历";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = "正在生成规范病历...";

        try
        {
            // 优先使用诊断，诊断为空时用主诉
            var input = !string.IsNullOrWhiteSpace(DiagnosisText)
                ? DiagnosisText.Trim()
                : ChiefComplaint.Trim();

            var result = await _llmService.ParseDiagnosisAsync(input);

            // 格式化为规范病历文本
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.ChiefComplaint))
                lines.Add($"【主诉】{result.ChiefComplaint}");
            if (!string.IsNullOrWhiteSpace(result.Signs))
                lines.Add($"【体征】{result.Signs}");
            if (!string.IsNullOrWhiteSpace(result.SuspectedDiagnosis))
                lines.Add($"【诊断】{result.SuspectedDiagnosis}");
            if (!string.IsNullOrWhiteSpace(result.SuggestedExams))
                lines.Add($"【建议检查】{result.SuggestedExams}");

            AiDiagnosisResult = lines.Count > 0
                ? string.Join("\n", lines)
                : $"【诊断】{DiagnosisText}\n（AI未能进一步结构化，请手动修改）";

            HasAiDiagnosisResult = true;
            StatusMessage = "AI病历已生成，请审核修改后点击「确认使用」";
        }
        catch (Exception ex)
        {
            StatusMessage = $"AI生成失败：{ex.Message}，请手动编辑诊断";
            // 即使AI失败也显示原始诊断供编辑
            AiDiagnosisResult = $"【诊断】{DiagnosisText}";
            HasAiDiagnosisResult = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanGenerateMedicalRecord() => !IsBusy;

    /// <summary>确认使用AI生成的病历（将编辑后的结果应用到诊断文本）</summary>
    [RelayCommand(CanExecute = nameof(CanConfirmAiDiagnosis))]
    private void ConfirmAiDiagnosis()
    {
        if (string.IsNullOrWhiteSpace(AiDiagnosisResult))
            return;

        // 提取诊断内容：取【诊断】行的内容，如果没有则取全文
        var diagnosisLine = AiDiagnosisResult
            .Split('\n')
            .FirstOrDefault(l => l.StartsWith("【诊断】"));

        DiagnosisText = diagnosisLine is not null
            ? diagnosisLine["【诊断】".Length..].Trim()
            : AiDiagnosisResult.Replace("【主诉】", "").Replace("【诊断】", "")
                .Replace("【ICD-10】", "").Replace("【备注】", "").Trim();

        AiDiagnosisResult = null;
        HasAiDiagnosisResult = false;
        StatusMessage = "已应用AI病历，请确认后创建处方";
    }

    /// <summary>放弃AI生成的病历</summary>
    [RelayCommand]
    private void ClearAiDiagnosis()
    {
        AiDiagnosisResult = null;
        HasAiDiagnosisResult = false;
        StatusMessage = null;
    }

    private bool CanConfirmAiDiagnosis() => HasAiDiagnosisResult;

    /// <summary>使用 LLM 将口语化用药描述整理为标准化处方明细</summary>
    [RelayCommand(CanExecute = nameof(CanParsePrescription))]
    private async Task ParsePrescriptionAsync()
    {
        if (string.IsNullOrWhiteSpace(LlmPrescriptionInput))
            return;

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            // 批量解析：支持多行/整段含多种药品的文本
            var results = await _llmService.ParsePrescriptionListAsync(LlmPrescriptionInput.Trim());

            if (results.Count == 0)
            {
                StatusMessage = "LLM 未能识别用药信息，请手动输入";
                return;
            }

            // 确保有草稿处方可写入
            if (!await EnsureDraftPrescriptionAsync())
            {
                StatusMessage = "AI 整理失败：请先选择患者并填写诊断";
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var prescriptionService = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();

            var addedCount = 0;
            var unmatched = new List<string>();

            foreach (var result in results)
            {
                if (string.IsNullOrWhiteSpace(result.DrugName))
                    continue;

                // 尝试在药品目录中匹配药品名
                var matchedDrug = Drugs.FirstOrDefault(d =>
                    d.GenericNameCn.Contains(result.DrugName, StringComparison.OrdinalIgnoreCase) ||
                    result.DrugName.Contains(d.GenericNameCn, StringComparison.OrdinalIgnoreCase));

                if (matchedDrug is null)
                {
                    unmatched.Add(result.DrugName);
                    continue;
                }

                // 计算数量：优先模型给出的 totalQty，缺失时按 单次剂量×频次×天数 估算
                var qty = result.TotalQty ?? EstimateQty(result.Dose, result.Frequency, result.DurationDays);
                if (qty <= 0)
                    qty = 1m;

                var dose = result.Dose ?? 1m;
                var doseUnit = string.IsNullOrWhiteSpace(result.DoseUnit) ? matchedDrug.Unit : result.DoseUnit!;
                var frequency = string.IsNullOrWhiteSpace(result.Frequency) ? "qd" : result.Frequency!;
                var route = string.IsNullOrWhiteSpace(result.Route) ? "口服" : result.Route!;
                var days = result.DurationDays ?? 3;

                try
                {
                    var itemId = await prescriptionService.AddPrescriptionItemAsync(
                        _draftPrescriptionId!.Value,
                        matchedDrug.Id,
                        dose,
                        doseUnit,
                        frequency,
                        route,
                        days,
                        qty);

                    if (itemId > 0)
                    {
                        var unitPrice = matchedDrug.RetailPriceRef ?? 0m;
                        var packQty = PrescriptionItemDto.ParsePackQuantity(matchedDrug.Spec);
                        var item = new PrescriptionItemDto
                        {
                            Id = itemId,
                            DrugId = matchedDrug.Id,
                            DrugName = matchedDrug.GenericNameCn,
                            Spec = matchedDrug.Spec,
                            Dose = dose,
                            DoseUnit = doseUnit,
                            Frequency = frequency,
                            Route = route,
                            DurationDays = days,
                            Qty = qty,
                            UnitPrice = unitPrice,
                            PackQuantity = packQty
                        };
                        item.RecalculateSubtotalOnly();
                        PrescriptionItems.Add(item);

                        addedCount++;
                    }
                }
                catch
                {
                    unmatched.Add($"{result.DrugName}（加入失败）");
                }
            }

            if (addedCount > 0 && unmatched.Count == 0)
            {
                StatusMessage = $"AI 批量整理完成：已添加 {addedCount} 种药品（请人工核对剂量与数量）";
            }
            else if (addedCount > 0)
            {
                StatusMessage = $"AI 批量整理完成：已添加 {addedCount} 种药品；未识别/失败：{string.Join("、", unmatched)}";
            }
            else
            {
                StatusMessage = $"未添加任何药品。未识别：{string.Join("、", unmatched)}，请手动选择药品";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"AI 整理失败：{ex.Message}，请手动输入用药信息";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>估算药品总量：单次剂量 × 频次次数 × 疗程天数</summary>
    private static decimal EstimateQty(decimal? dose, string? frequency, int? durationDays)
    {
        if (!dose.HasValue || dose.Value <= 0)
            return 0m;

        var freq = frequency?.ToLowerInvariant() ?? string.Empty;
        var timesPerDay = freq switch
        {
            _ when freq.Contains("tid") || freq.Contains("tds") || freq.Contains("三次") => 3,
            _ when freq.Contains("bid") || freq.Contains("两次") => 2,
            _ when freq.Contains("qid") || freq.Contains("四次") => 4,
            _ => 1
        };

        var days = durationDays ?? 0;
        return Math.Round(dose.Value * timesPerDay * days, 2);
    }

    private bool CanParsePrescription() => !IsBusy && LlmIsAvailable && !string.IsNullOrWhiteSpace(LlmPrescriptionInput);
}
