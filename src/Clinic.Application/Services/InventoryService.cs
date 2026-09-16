using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Clinic.Application.Validators;
using Clinic.Domain.Entities;
using Clinic.Domain.Interfaces;
using Clinic.Shared.Enums;
using FluentValidation;

namespace Clinic.Application.Services;

/// <summary>
/// 药品进销存服务实现。
/// 入库流程：记录 DrugIn（入库流水）+ 创建/更新 DrugStock（库存批次）
/// 效期预警：查询 30 天内即将过期的库存批次
/// </summary>
public class InventoryService : IInventoryService
{
    private const int ExpiryAlertDays = 30;

    private readonly IRepository<DrugIn> _drugInRepo;
    private readonly IRepository<DrugOut> _drugOutRepo;
    private readonly IRepository<DrugStock> _stockRepo;
    private readonly IRepository<DrugMaster> _drugRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IValidator<StockInRequest> _stockInValidator;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IAuditService _auditService;
    private readonly IUserSession _session;

    public InventoryService(
        IRepository<DrugIn> drugInRepo,
        IRepository<DrugOut> drugOutRepo,
        IRepository<DrugStock> stockRepo,
        IRepository<DrugMaster> drugRepo,
        IUnitOfWork unitOfWork,
        IClock clock,
        IValidator<StockInRequest> stockInValidator,
        IPermissionChecker permissionChecker,
        IAuditService auditService,
        IUserSession session)
    {
        _drugInRepo = drugInRepo;
        _drugOutRepo = drugOutRepo;
        _stockRepo = stockRepo;
        _drugRepo = drugRepo;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _stockInValidator = stockInValidator;
        _permissionChecker = permissionChecker;
        _auditService = auditService;
        _session = session;
    }

    public async Task<long> StockInAsync(
        long drugId, string batchNo, DateOnly expiryDate,
        decimal qty, decimal costPrice, string? supplier,
        long operatorId, CancellationToken ct = default)
    {
        // 权限检查：入库需要 Doctor 或 Nurse 权限
        _permissionChecker.RequireCanModify();

        // 输入验证：药品 ID、批号、效期、数量、成本价等由 FluentValidation 处理
        var request = new StockInRequest(drugId, batchNo, expiryDate, qty, costPrice, supplier, operatorId);
        var validationResult = await _stockInValidator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        var drug = await _drugRepo.GetByIdAsync(drugId, ct);
        if (drug is null)
            throw new InvalidOperationException("药品不存在");

        var now = _clock.UtcNow;

        // P0 修复：入库使用全应用共享库存锁，与出库/发药扣减/回退互斥，防止并发改写同一批次
        await InventoryLock.Instance.WaitAsync(ct);
        try
        {
            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                // 1. 记录入库流水
                var drugIn = new DrugIn
                {
                    DrugId = drugId,
                    BatchNo = batchNo.Trim(),
                    ExpiryDate = expiryDate,
                    Qty = qty,
                    CostPrice = costPrice,
                    Supplier = supplier?.Trim(),
                    ReceivedAt = now,
                    OperatorId = operatorId
                };
                await _drugInRepo.AddAsync(drugIn, ct);

                // 2. 创建或更新库存批次（同药品同批号合并）
                var existingStocks = await _stockRepo.FindAsync(
                    s => s.DrugId == drugId && s.BatchNo == batchNo.Trim(), ct);
                var stock = existingStocks.FirstOrDefault();

                if (stock is not null)
                {
                    stock.QtyRemaining += qty;
                    _stockRepo.Update(stock);
                }
                else
                {
                    stock = new DrugStock
                    {
                        DrugId = drugId,
                        BatchNo = batchNo.Trim(),
                        ExpiryDate = expiryDate,
                        QtyRemaining = qty,
                        CostPrice = costPrice,
                        Supplier = supplier?.Trim(),
                        ReceivedAt = now
                    };
                    await _stockRepo.AddAsync(stock, ct);
                }

                // 审计日志（事务内，与业务操作原子提交）
                await _auditService.LogAsync("STOCK_IN",
                    $"Drug:{drugId} Batch:{batchNo.Trim()}",
                    $"Qty:{qty} CostPrice:{costPrice} Expiry:{expiryDate:O}", ct);

                await _unitOfWork.CommitAsync(ct);

                return drugIn.Id;
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }
        }
        finally
        {
            InventoryLock.Instance.Release();
        }
    }

    /// <summary>
    /// 手动出库（P2）：报损、调拨等非处方出库。
    /// 按 FIFO（效期优先）策略扣减库存，跳过已过期批次，生成 DrugOut 出库流水。
    /// 库存不足或所有批次均已过期时抛出异常，事务回滚。
    /// </summary>
    public async Task StockOutAsync(
        long drugId, int qty, string reason, long operatorId,
        CancellationToken ct = default)
    {
        // 权限检查：出库需要 Doctor 或 Nurse 权限
        _permissionChecker.RequireCanModify();

        if (qty <= 0)
            throw new ArgumentException("出库数量必须大于 0", nameof(qty));

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("出库原因不能为空", nameof(reason));

        var drug = await _drugRepo.GetByIdAsync(drugId, ct);
        if (drug is null)
            throw new InvalidOperationException("药品不存在");

        var now = _clock.UtcNow;
        var today = DateOnly.FromDateTime(now.Date);

        // P0 修复：出库扣减使用全应用共享库存锁，防止并发导致超卖
        await InventoryLock.Instance.WaitAsync(ct);
        try
        {
            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                // 获取该药品所有可用库存批次（跳过已过期的），按效期升序（FIFO）
                var stocks = await _stockRepo.FindAsync(
                    s => s.DrugId == drugId && s.QtyRemaining > 0, ct);

                var validStocks = stocks
                    .Where(b => b.ExpiryDate > today)
                    .OrderBy(s => s.ExpiryDate)
                    .ToList();

                if (validStocks.Count == 0)
                    throw new InvalidOperationException(
                        stocks.Count > 0
                            ? $"药品「{drug.GenericNameCn}」所有库存批次均已过期，无法出库"
                            : $"药品「{drug.GenericNameCn}」无可用库存");

                var totalAvailable = validStocks.Sum(s => s.QtyRemaining);
                if (totalAvailable < qty)
                    throw new InvalidOperationException(
                        $"药品「{drug.GenericNameCn}」库存不足：需要 {qty}，可用 {totalAvailable}");

                var remainingQty = (decimal)qty;

                foreach (var stock in validStocks)
                {
                    if (remainingQty <= 0) break;

                    var deductQty = Math.Min(stock.QtyRemaining, remainingQty);

                    // 扣减库存批次
                    stock.QtyRemaining -= deductQty;
                    _stockRepo.Update(stock);

                    // 生成出库流水（PrescriptionId = null 表示非处方出库）
                    await _drugOutRepo.AddAsync(new DrugOut
                    {
                        DrugId = drugId,
                        BatchNo = stock.BatchNo,
                        Qty = deductQty,
                        PrescriptionId = null,
                        OccurredAt = now,
                        OperatorId = operatorId,
                        IsReversal = false
                    }, ct);

                    remainingQty -= deductQty;
                }

                // 审计日志（事务内，与业务操作原子提交）
                await _auditService.LogAsync("STOCK_OUT",
                    $"Drug:{drugId}",
                    $"Qty:{qty} Reason:{reason.Trim()} Operator:{operatorId}", ct);

                await _unitOfWork.CommitAsync(ct);
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }
        }
        finally
        {
            InventoryLock.Instance.Release();
        }
    }

    public async Task<decimal> GetStockQuantityAsync(long drugId, CancellationToken ct = default)
    {
        // P1：权限检查——读取库存需 Doctor/Nurse/Readonly 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly, UserRole.Pharmacist);

        var stocks = await _stockRepo.FindAsync(s => s.DrugId == drugId, ct);
        return stocks.Sum(s => s.QtyRemaining);
    }

    public async Task<IReadOnlyList<ExpiryAlertDto>> GetExpiryAlertsAsync(CancellationToken ct = default)
    {
        // P1：权限检查——读取效期预警需 Doctor/Nurse/Readonly 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly, UserRole.Pharmacist);

        var today = DateOnly.FromDateTime(_clock.Now);
        var alertThreshold = today.AddDays(ExpiryAlertDays);

        var stocks = await _stockRepo.FindAsync(
            s => s.QtyRemaining > 0 && s.ExpiryDate <= alertThreshold, ct);

        if (stocks.Count == 0)
            return [];

        // 获取药品名称
        var drugIds = stocks.Select(s => s.DrugId).Distinct().ToList();
        var alerts = new List<ExpiryAlertDto>();

        foreach (var stock in stocks.OrderBy(s => s.ExpiryDate))
        {
            var drug = await _drugRepo.GetByIdAsync(stock.DrugId, ct);
            var drugName = drug?.GenericNameCn ?? $"[药品ID:{stock.DrugId}]";
            var daysRemaining = stock.ExpiryDate.DayNumber - today.DayNumber;

            alerts.Add(new ExpiryAlertDto(
                stock.DrugId, drugName, stock.BatchNo,
                stock.ExpiryDate, daysRemaining, stock.QtyRemaining));
        }

        return alerts;
    }

    public async Task<IReadOnlyList<DrugDto>> GetAllDrugsAsync(CancellationToken ct = default)
    {
        // P1：权限检查——读取药品目录需 Doctor/Nurse/Readonly 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly, UserRole.Pharmacist);

        var drugs = await _drugRepo.GetAllAsync(ct);
        var allStocks = await _stockRepo.GetAllAsync(ct);

        return drugs.Select(d =>
        {
            var availableQty = allStocks
                .Where(s => s.DrugId == d.Id && s.QtyRemaining > 0)
                .Sum(s => s.QtyRemaining);
            var isLowStock = d.ReorderLevel.HasValue && availableQty < d.ReorderLevel.Value;

            return new DrugDto(
                d.Id,
                d.GenericNameCn,
                d.GenericNameEn,
                d.Spec,
                d.Unit,
                d.DefaultUsage,
                d.IsAntibiotic,
                (int)d.AntibioticLevel,
                d.IsToxicDrug,
                d.ContraindicationTags,
                d.RetailPriceRef,
                d.ReorderLevel,
                availableQty,
                isLowStock);
        }).ToList();
    }

    public async Task<IReadOnlyList<DrugStockSummaryDto>> GetAllStockSummaryAsync(CancellationToken ct = default)
    {
        // P1：权限检查——读取库存汇总需 Doctor/Nurse/Readonly 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly, UserRole.Pharmacist);

        var drugs = await _drugRepo.GetAllAsync(ct);
        var allStocks = await _stockRepo.GetAllAsync(ct);

        var result = new List<DrugStockSummaryDto>();
        foreach (var drug in drugs)
        {
            var stocks = allStocks.Where(s => s.DrugId == drug.Id && s.QtyRemaining > 0).ToList();
            var totalQty = stocks.Sum(s => s.QtyRemaining);
            var batchCount = stocks.Count;
            var earliestExpiry = stocks.Count > 0 ? stocks.Min(s => s.ExpiryDate) : (DateOnly?)null;
            var isLowStock = drug.ReorderLevel.HasValue && totalQty < drug.ReorderLevel.Value;

            result.Add(new DrugStockSummaryDto(
                drug.Id,
                drug.GenericNameCn,
                drug.Spec,
                drug.Unit,
                totalQty,
                batchCount,
                earliestExpiry,
                drug.RetailPriceRef,
                drug.ReorderLevel,
                isLowStock));
        }

        return result;
    }

    public async Task<IReadOnlyList<StockBatchDto>> GetStockBatchesAsync(long drugId, CancellationToken ct = default)
    {
        // P1：权限检查——读取库存批次需 Doctor/Nurse/Readonly 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly, UserRole.Pharmacist);

        var drug = await _drugRepo.GetByIdAsync(drugId, ct);
        var drugName = drug?.GenericNameCn ?? $"[药品ID:{drugId}]";

        var stocks = await _stockRepo.FindAsync(s => s.DrugId == drugId && s.QtyRemaining > 0, ct);

        return stocks
            .OrderBy(s => s.ExpiryDate)
            .Select(s => new StockBatchDto(
                s.Id,
                s.DrugId,
                drugName,
                s.BatchNo,
                s.ExpiryDate,
                s.QtyRemaining,
                s.CostPrice,
                s.Supplier,
                s.ReceivedAt))
            .ToList();
    }

    public async Task<IReadOnlyList<StockInRecordDto>> GetStockInHistoryAsync(
        DateTime? fromDate = null, DateTime? toDate = null, CancellationToken ct = default)
    {
        // P1：权限检查——读取入库流水需 Doctor/Nurse/Readonly 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly, UserRole.Pharmacist);

        var records = await _drugInRepo.GetAllAsync(ct);

        if (fromDate.HasValue)
            records = records.Where(r => r.ReceivedAt >= fromDate.Value).ToList();

        if (toDate.HasValue)
            records = records.Where(r => r.ReceivedAt < toDate.Value.AddDays(1)).ToList();

        // 批量获取药品名称
        var drugIds = records.Select(r => r.DrugId).Distinct().ToList();
        var drugNames = new Dictionary<long, string>();
        foreach (var drugId in drugIds)
        {
            var drug = await _drugRepo.GetByIdAsync(drugId, ct);
            drugNames[drugId] = drug?.GenericNameCn ?? $"[药品ID:{drugId}]";
        }

        return records
            .OrderByDescending(r => r.ReceivedAt)
            .Select(r => new StockInRecordDto(
                r.Id,
                r.DrugId,
                drugNames.GetValueOrDefault(r.DrugId, $"[药品ID:{r.DrugId}]"),
                r.BatchNo,
                r.ExpiryDate,
                r.Qty,
                r.CostPrice,
                r.Supplier,
                r.ReceivedAt,
                r.OperatorId,
                string.Empty))
            .ToList();
    }

    // ──────────────────────────── 国内药品清单导入 ────────────────────────────

    /// <summary>
    /// 抗菌药物名称特征词：命中即标记为抗菌药物（与类别判定互补）。
    /// </summary>
    private static readonly string[] AntibioticKeywords =
    {
        "西林", "头孢", "沙星", "霉素", "环素", "硝唑", "培南", "磺胺", "呋喃",
        "利福", "异烟肼", "乙胺丁醇", "吡嗪酰胺", "两性霉素", "制霉菌素", "氟康唑",
        "伊曲康唑", "伏立康唑", "卡泊芬净", "特比萘芬", "阿昔洛韦", "伐昔洛韦",
        "更昔洛韦", "奥司他韦", "利巴韦林", "磷霉素", "夫西地酸", "万古霉素",
        "替考拉宁", "利奈唑胺", "多黏菌素", "黏菌素"
    };

    /// <summary>特殊使用级 / 限制使用级抗菌药物特征词（按《抗菌药物临床应用管理办法》常见品种）</summary>
    private static readonly string[] RestrictedAntibioticKeywords =
    {
        "美罗培南", "亚胺培南", "帕尼培南", "比阿培南", "厄他培南", "万古霉素",
        "替考拉宁", "利奈唑胺", "替加环素", "两性霉素B", "伏立康唑", "卡泊芬净",
        "米卡芬净", "泊沙康唑", "多黏菌素B", "多黏菌素E", "头孢洛林", "头孢比罗",
        "头孢他啶阿维巴坦"
    };

    /// <summary>从医保目录的「类别剂型」文本中提取治疗类别（方括号前部分）</summary>
    internal static string ParseCategory(string medKind)
    {
        if (string.IsNullOrWhiteSpace(medKind)) return string.Empty;
        var idx = medKind.IndexOf('[');
        return idx >= 0 ? medKind[..idx].Trim() : medKind.Trim();
    }

    /// <summary>从医保目录的「类别剂型」文本中提取剂型（方括号内，多剂型用分号分隔保留）</summary>
    internal static string ParseDosageForm(string medKind)
    {
        if (string.IsNullOrWhiteSpace(medKind)) return string.Empty;
        var start = medKind.IndexOf('[');
        var end = medKind.LastIndexOf(']');
        if (start < 0 || end <= start) return string.Empty;
        return medKind[(start + 1)..end].Trim();
    }

    /// <summary>判断是否为抗菌药物：类别命中 或 名称命中特征词</summary>
    internal static bool IsAntibioticByRule(string name, string category) =>
        category.Contains("抗微生物", StringComparison.OrdinalIgnoreCase)
        || category.Contains("全身用抗感染", StringComparison.OrdinalIgnoreCase)
        || category.Contains("抗感染", StringComparison.OrdinalIgnoreCase)
        || AntibioticKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));

    /// <summary>判断抗菌药物管理级别：命中特殊/限制使用级特征词 → Restricted，否则 NonRestricted</summary>
    internal static AntibioticLevel LevelByRule(string name, bool isAntibiotic)
    {
        if (!isAntibiotic) return AntibioticLevel.None;
        return RestrictedAntibioticKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase))
            ? AntibioticLevel.Restricted
            : AntibioticLevel.NonRestricted;
    }

    public async Task<DrugImportResultDto> ImportNationalDrugListAsync(
        IReadOnlyList<NationalDrugEntryDto> entries, CancellationToken ct = default)
    {
        _permissionChecker.RequireCanModify();

        if (entries is null || entries.Count == 0)
            return new DrugImportResultDto(0, 0, 0, 0, 0, 0, ["清单为空，无数据可导入"]);

        var now = _clock.UtcNow;
        var existing = await _drugRepo.GetAllAsync(ct);
        // 库内去重键：通用名 + 规格（剂型）。同一成分不同剂型视为不同药品（如阿莫西林口服/注射）
        var existingByKey = existing
            .Where(d => !string.IsNullOrWhiteSpace(d.GenericNameCn))
            .ToDictionary(
                d => $"{d.GenericNameCn.Trim()}||{d.Spec?.Trim() ?? ""}",
                d => d,
                StringComparer.OrdinalIgnoreCase);

        var imported = 0;
        var skippedExisting = 0;
        var skippedInvalid = 0;
        var antibioticCount = 0;
        var restrictedCount = 0;
        var sampleErrors = new List<string>();
        var added = new List<DrugMaster>();

        // 同一批内按（通用名||剂型）去重，与库内口径一致
        var batchSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in entries)
        {
            var name = e.MedName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                skippedInvalid++;
                if (sampleErrors.Count < 10) sampleErrors.Add("空药品名称");
                continue;
            }
            if (name.Length > 100)
            {
                skippedInvalid++;
                if (sampleErrors.Count < 10) sampleErrors.Add($"药品名过长：{name[..Math.Min(20, name.Length)]}...");
                continue;
            }

            var category = ParseCategory(e.MedKind);
            var dosageForm = ParseDosageForm(e.MedKind);
            var dedupKey = $"{name}||{dosageForm}";

            // 批内 + 库内双重去重（通用名+剂型）
            if (!batchSeen.Add(dedupKey) || existingByKey.ContainsKey(dedupKey))
            {
                skippedExisting++;
                continue;
            }
            var isAntibiotic = IsAntibioticByRule(name, category);
            var level = LevelByRule(name, isAntibiotic);
            if (isAntibiotic) antibioticCount++;
            if (level == AntibioticLevel.Restricted) restrictedCount++;

            var drug = new DrugMaster
            {
                GenericNameCn = name,
                // 医保目录无英文名：置 null（数据库已迁移允许 NULL，唯一索引不约束 NULL）
                GenericNameEn = null!,
                // 规格：优先用剂型；无剂型时保留类别信息，便于库存模块展示
                Spec = dosageForm,
                Unit = string.Empty,
                DefaultUsage = null,
                IsAntibiotic = isAntibiotic,
                AntibioticLevel = level,
                IsToxicDrug = false,
                ContraindicationTags = null,
                CostPriceRef = null,
                RetailPriceRef = null,
                ReorderLevel = null,
                CreatedAt = now
            };
            await _drugRepo.AddAsync(drug, ct);
            added.Add(drug);
            imported++;
        }

        if (imported > 0)
        {
            // 事务保证原子性：目录写入 + 审计日志同生共死，失败整体回滚
            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                // 审计日志内部 SaveChanges（一次保存全部待写实体 + 审计记录）
                await _auditService.LogAsync("DRUG_CATALOG_IMPORT",
                    $"Source:NationalDrugList",
                    $"Total:{entries.Count} Imported:{imported} SkippedExisting:{skippedExisting} Invalid:{skippedInvalid} Antibiotic:{antibioticCount}",
                    ct);
                await _unitOfWork.CommitAsync(ct);
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }
        }

        return new DrugImportResultDto(
            entries.Count, imported, skippedExisting, skippedInvalid,
            antibioticCount, restrictedCount, sampleErrors);
    }
}
