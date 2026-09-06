using System.Collections.ObjectModel;
using System.IO;
using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Clinic.Presentation.Helpers;
using Clinic.Shared.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation.ViewModels;

/// <summary>
/// 库存管理 ViewModel。功能：库存概览、入库操作、效期预警。
/// 权限：Doctor/Nurse 可入库（CanModify），所有角色可查看。
/// </summary>
public partial class InventoryViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IUserSession _session;

    // ── 库存概览 ──

    /// <summary>全部库存汇总（未过滤，用于客户端筛选）</summary>
    private List<DrugStockSummaryDto> _allStockSummary = new();

    public ObservableCollection<DrugStockSummaryDto> StockSummaryList { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ViewBatchesCommand))]
    private DrugStockSummaryDto? _selectedStockItem;

    /// <summary>库存概览搜索关键词（按药品名称/规格模糊匹配）</summary>
    [ObservableProperty]
    private string? _stockSearchKeyword;

    /// <summary>库存概览筛选：0=全部, 1=低库存, 2=近效期(30天内)</summary>
    [ObservableProperty]
    private int _stockFilter;

    partial void OnStockSearchKeywordChanged(string? value) => ApplyStockFilter();
    partial void OnStockFilterChanged(int value) => ApplyStockFilter();

    /// <summary>根据搜索关键词和筛选条件过滤库存概览</summary>
    private void ApplyStockFilter()
    {
        StockSummaryList.Clear();
        var keyword = StockSearchKeyword?.Trim() ?? string.Empty;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var nearExpiryThreshold = today.AddDays(30);

        var filtered = _allStockSummary.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filtered = filtered.Where(s =>
                s.DrugName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                s.Spec.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        filtered = StockFilter switch
        {
            1 => filtered.Where(s => s.IsLowStock),
            2 => filtered.Where(s => s.EarliestExpiry.HasValue && s.EarliestExpiry.Value <= nearExpiryThreshold),
            _ => filtered
        };

        foreach (var s in filtered)
            StockSummaryList.Add(s);
    }

    public ObservableCollection<StockBatchDto> StockBatches { get; } = new();

    // ── 入库操作 ──

    public ObservableCollection<DrugDto> Drugs { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StockInCommand))]
    private DrugDto? _selectedDrug;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StockInCommand))]
    private string? _batchNo;

    [ObservableProperty]
    private DateTime? _expiryDate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StockInCommand))]
    private string? _qty;

    [ObservableProperty]
    private string? _costPrice;

    [ObservableProperty]
    private string? _supplier;

    // ── 效期预警 ──

    public ObservableCollection<ExpiryAlertDto> ExpiryAlerts { get; } = new();

    // ── 手动出库 ──

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StockOutCommand))]
    private DrugDto? _stockOutSelectedDrug;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StockOutCommand))]
    private string? _stockOutQty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StockOutCommand))]
    private string? _stockOutReason;

    // ── 入库历史 ──

    public ObservableCollection<StockInRecordDto> StockInHistory { get; } = new();

    [ObservableProperty]
    private DateTime? _historyFromDate;

    [ObservableProperty]
    private DateTime? _historyToDate;

    // ── 通用状态 ──
/// <summary>是否正在执行异步操作（防重复提交）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StockInCommand))]
    [NotifyCanExecuteChangedFor(nameof(StockOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadDataCommand))]
    private bool _isBusy;

    /// <summary>当前功能页：0=库存概览, 1=入库操作, 2=效期预警</summary>
    [ObservableProperty]
    private int _currentTab;

    /// <summary>当前用户是否有入库权限</summary>
    public bool CanModify => _session.Role == UserRole.Doctor || _session.Role == UserRole.Nurse;

    // ── 添加国内药品清单 ──

    /// <summary>导入范围：0=国家目录（默认），1=全国（含各省增补）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportNationalDrugListCommand))]
    private int _importScope;

    /// <summary>导入地域筛选："全部"=不筛选，"国家"=仅国家目录，其他=具体省份</summary>
    [ObservableProperty]
    private string _importRegion = "全部";

    /// <summary>可选地域列表（从药品清单动态加载，含"全部"和"国家"及各省份）</summary>
    public ObservableCollection<string> AvailableRegions { get; } = new()
    {
        "全部", "国家", "北京", "天津", "河北", "山西", "内蒙古",
        "辽宁", "吉林", "黑龙江", "上海", "江苏", "浙江", "安徽",
        "福建", "江西", "山东", "河南", "湖北", "湖南", "广东",
        "广西", "海南", "重庆", "四川", "贵州", "云南", "西藏",
        "陕西", "甘肃", "青海", "宁夏", "新疆"
    };

    /// <summary>是否正在下载/导入药品清单（防重复提交）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportNationalDrugListCommand))]
    private bool _isImporting;

    /// <summary>国内药品清单数据源地址（可编辑，默认 GitHub）</summary>
    [ObservableProperty]
    private string _importSourceUrl = "https://raw.githubusercontent.com/lrpopeyou/MedicalInsuranceKG/master/药品信息.csv";

    /// <summary>国内药品清单本地文件路径（优先使用本地文件，不存在时回退网络下载）</summary>
    [ObservableProperty]
    private string _importLocalPath = @"testdata\药品清单\药品清单_完整.csv";

    public InventoryViewModel(IServiceScopeFactory scopeFactory, IUserSession session)
    {
        _scopeFactory = scopeFactory;
        _session = session;

        // 默认日期：历史查询范围为最近 30 天
        HistoryFromDate = DateTime.Today.AddDays(-30);
        HistoryToDate = DateTime.Today;
    }

    /// <summary>加载药品目录 + 库存汇总 + 效期预警 + 入库历史</summary>
    [RelayCommand(CanExecute = nameof(CanLoadData))]
    private async Task LoadDataAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            var drugs = await inventoryService.GetAllDrugsAsync();
            var summary = await inventoryService.GetAllStockSummaryAsync();
            var alerts = await inventoryService.GetExpiryAlertsAsync();
            var history = await inventoryService.GetStockInHistoryAsync(HistoryFromDate, HistoryToDate);

            Drugs.Clear();
            foreach (var d in drugs)
                Drugs.Add(d);

            _allStockSummary = summary.ToList();
            ApplyStockFilter();

            ExpiryAlerts.Clear();
            foreach (var a in alerts)
                ExpiryAlerts.Add(a);

            StockInHistory.Clear();
            foreach (var h in history)
                StockInHistory.Add(h);

            StatusMessage = $"已加载 {StockSummaryList.Count} 种药品库存";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"加载数据失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLoadData() => !IsBusy;

    /// <summary>刷新库存汇总</summary>
    [RelayCommand]
    private async Task RefreshStockSummaryAsync()
    {
        ErrorMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            var summary = await inventoryService.GetAllStockSummaryAsync();

            _allStockSummary = summary.ToList();
            ApplyStockFilter();

            StatusMessage = $"已刷新库存汇总（{StockSummaryList.Count} 条）";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"刷新库存汇总失败：{ExceptionFormatter.GetMessage(ex)}";
        }
    }

    /// <summary>查看选中药品的批次明细</summary>
    [RelayCommand(CanExecute = nameof(CanViewBatches))]
    private async Task ViewBatchesAsync()
    {
        if (SelectedStockItem is null)
            return;

        ErrorMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            var batches = await inventoryService.GetStockBatchesAsync(SelectedStockItem.DrugId);

            StockBatches.Clear();
            foreach (var b in batches)
                StockBatches.Add(b);

            StatusMessage = $"已加载 {SelectedStockItem.DrugName} 的 {StockBatches.Count} 个批次";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"加载批次明细失败：{ExceptionFormatter.GetMessage(ex)}";
        }
    }

    private bool CanViewBatches() => SelectedStockItem is not null;

    /// <summary>执行入库</summary>
    [RelayCommand(CanExecute = nameof(CanStockIn))]
    private async Task StockInAsync()
    {
        IsBusy = true;
        try
        {
            if (SelectedDrug is null)
            {
                ErrorMessage = "请选择药品";
                return;
            }

            if (ExpiryDate is null)
            {
                ErrorMessage = "请选择效期日期";
                return;
            }

            if (!decimal.TryParse(Qty, out var qty) || qty <= 0)
            {
                ErrorMessage = "请输入有效的数量";
                return;
            }

            var costPrice = 0m;
            if (!string.IsNullOrWhiteSpace(CostPrice) && !decimal.TryParse(CostPrice, out costPrice))
            {
                ErrorMessage = "请输入有效的成本价";
                return;
            }

            ErrorMessage = null;
            StatusMessage = null;

            using var scope = _scopeFactory.CreateScope();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            var operatorId = _session.UserId ?? 0;
            var expiryDateOnly = DateOnly.FromDateTime(ExpiryDate.Value);
            var drugName = SelectedDrug.GenericNameCn;

            var recordId = await inventoryService.StockInAsync(
                SelectedDrug.Id,
                BatchNo!.Trim(),
                expiryDateOnly,
                qty,
                costPrice,
                Supplier?.Trim(),
                operatorId);

            // 清空表单
            SelectedDrug = null;
            BatchNo = null;
            ExpiryDate = null;
            Qty = null;
            CostPrice = null;
            Supplier = null;

            // 刷新库存汇总和入库历史
            var summary = await inventoryService.GetAllStockSummaryAsync();
            StockSummaryList.Clear();
            foreach (var s in summary)
                StockSummaryList.Add(s);

            var history = await inventoryService.GetStockInHistoryAsync(HistoryFromDate, HistoryToDate);
            StockInHistory.Clear();
            foreach (var h in history)
                StockInHistory.Add(h);

            StatusMessage = $"入库成功：{drugName} × {qty}（记录号 {recordId}）";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"入库失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanStockIn()
        => !IsBusy
           && CanModify
           && SelectedDrug is not null
           && !string.IsNullOrWhiteSpace(BatchNo)
           && decimal.TryParse(Qty, out _);

    /// <summary>刷新效期预警</summary>
    [RelayCommand]
    private async Task LoadExpiryAlertsAsync()
    {
        ErrorMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            var alerts = await inventoryService.GetExpiryAlertsAsync();

            ExpiryAlerts.Clear();
            foreach (var a in alerts)
                ExpiryAlerts.Add(a);

            StatusMessage = $"已加载 {ExpiryAlerts.Count} 条效期预警";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"加载效期预警失败：{ExceptionFormatter.GetMessage(ex)}";
        }
    }

    /// <summary>按日期范围查询入库历史</summary>
    [RelayCommand]
    private async Task LoadStockInHistoryAsync()
    {
        ErrorMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            var history = await inventoryService.GetStockInHistoryAsync(HistoryFromDate, HistoryToDate);

            StockInHistory.Clear();
            foreach (var h in history)
                StockInHistory.Add(h);

            StatusMessage = $"已加载 {StockInHistory.Count} 条入库记录";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"查询入库历史失败：{ExceptionFormatter.GetMessage(ex)}";
        }
    }

    /// <summary>手动出库（报损、调拨等非处方出库）</summary>
    [RelayCommand(CanExecute = nameof(CanStockOut))]
    private async Task StockOutAsync()
    {
        if (StockOutSelectedDrug is null)
        {
            ErrorMessage = "请选择药品";
            return;
        }

        if (!int.TryParse(StockOutQty, out var qty) || qty <= 0)
        {
            ErrorMessage = "请输入有效的出库数量（正整数）";
            return;
        }

        if (string.IsNullOrWhiteSpace(StockOutReason))
        {
            ErrorMessage = "请填写出库原因";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            var operatorId = _session.UserId ?? 0;
            var drugName = StockOutSelectedDrug.GenericNameCn;

            await inventoryService.StockOutAsync(
                StockOutSelectedDrug.Id,
                qty,
                StockOutReason.Trim(),
                operatorId);

            // 清空表单
            StockOutSelectedDrug = null;
            StockOutQty = null;
            StockOutReason = null;

            // 刷新库存汇总
            var summary = await inventoryService.GetAllStockSummaryAsync();
            StockSummaryList.Clear();
            foreach (var s in summary)
                StockSummaryList.Add(s);

            StatusMessage = $"出库成功：{drugName} × {qty}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"出库失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanStockOut()
        => !IsBusy
           && CanModify
           && StockOutSelectedDrug is not null
           && int.TryParse(StockOutQty, out _)
           && !string.IsNullOrWhiteSpace(StockOutReason);

    /// <summary>
    /// 添加国内药品清单：优先从本地 CSV 文件读取，文件不存在时回退到网络下载。
    /// 走系统业务入口（与手工录入一致），按通用名去重，已存在药品自动跳过。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImportNationalDrugList))]
    private async Task ImportNationalDrugListAsync()
    {
        IsImporting = true;
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var source = scope.ServiceProvider.GetRequiredService<INationalDrugListSource>();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            // 1. 获取药品清单：优先本地文件，回退网络下载
            IReadOnlyList<NationalDrugEntryDto> all;
            string sourceDesc;

            // 解析本地文件路径（相对于程序运行目录或项目根目录）
            var localPath = ImportLocalPath;
            if (!Path.IsPathRooted(localPath))
            {
                // 尝试相对于程序运行目录
                var exeDir = AppContext.BaseDirectory;
                var candidate1 = Path.GetFullPath(Path.Combine(exeDir, localPath));
                // 尝试相对于项目根目录（向上找 testdata 目录）
                var candidate2 = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "..", localPath));
                if (File.Exists(candidate1))
                    localPath = candidate1;
                else if (File.Exists(candidate2))
                    localPath = candidate2;
            }

            if (File.Exists(localPath))
            {
                all = await source.ParseFromFileAsync(localPath);
                sourceDesc = $"本地文件 ({Path.GetFileName(localPath)})";
            }
            else
            {
                all = await source.DownloadAndParseAsync(ImportSourceUrl);
                sourceDesc = $"网络下载 ({ImportSourceUrl})";
            }

            // 2. 按地域筛选：全部 / 国家目录 / 具体省份
            var region = ImportRegion?.Trim() ?? "全部";
            var entries = region == "全部"
                ? all.ToList()
                : all.Where(e => e.MedPlc == region).ToList();

            if (entries.Count == 0)
            {
                ErrorMessage = $"从{sourceDesc}获取完成，但地域「{region}」下未解析到任何药品记录";
                return;
            }

            // 3. 导入药品主数据（走业务入口）
            var result = await inventoryService.ImportNationalDrugListAsync(entries);

            // 4. 刷新目录与库存
            await LoadDataAsync();

            StatusMessage =
                $"国内药品清单导入完成（{sourceDesc}）：共 {result.Total} 条，新增 {result.Imported} 种" +
                $"（其中抗菌药 {result.AntibioticCount} 种），跳过已存在 {result.SkippedExisting} 条，" +
                $"无效行 {result.SkippedInvalid} 条";
            if (result.SampleErrors.Count > 0)
                StatusMessage += $"；示例问题：{string.Join("；", result.SampleErrors.Take(3))}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"添加国内药品清单失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsImporting = false;
            IsBusy = false;
        }
    }

    private bool CanImportNationalDrugList() => !IsImporting && !IsBusy && CanModify;
}
