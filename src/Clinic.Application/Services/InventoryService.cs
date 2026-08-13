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

    /// <summary>P1 H-09：入库批次合并互斥锁，防止并发导致批次重复创建</summary>
    private static readonly SemaphoreSlim _stockInLock = new(1, 1);

    /// <summary>P2：手动出库互斥锁，防止并发导致超卖</summary>
    private static readonly SemaphoreSlim _stockOutLock = new(1, 1);

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

        // P1 H-09：使用互斥锁保护入库批次合并，防止并发创建重复批次
        await _stockInLock.WaitAsync(ct);
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
            _stockInLock.Release();
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

        // P2：使用互斥锁保护出库扣减，防止并发导致超卖
        await _stockOutLock.WaitAsync(ct);
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
            _stockOutLock.Release();
        }
    }

    public async Task<decimal> GetStockQuantityAsync(long drugId, CancellationToken ct = default)
    {
        // P1：权限检查——读取库存需 Doctor/Nurse/Readonly 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly);

        var stocks = await _stockRepo.FindAsync(s => s.DrugId == drugId, ct);
        return stocks.Sum(s => s.QtyRemaining);
    }

    public async Task<IReadOnlyList<ExpiryAlertDto>> GetExpiryAlertsAsync(CancellationToken ct = default)
    {
        // P1：权限检查——读取效期预警需 Doctor/Nurse/Readonly 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly);

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
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly);

        var drugs = await _drugRepo.GetAllAsync(ct);
        return drugs.Select(d => new DrugDto(
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
            d.RetailPriceRef)).ToList();
    }

    public async Task<IReadOnlyList<DrugStockSummaryDto>> GetAllStockSummaryAsync(CancellationToken ct = default)
    {
        // P1：权限检查——读取库存汇总需 Doctor/Nurse/Readonly 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly);

        var drugs = await _drugRepo.GetAllAsync(ct);
        var allStocks = await _stockRepo.GetAllAsync(ct);

        var result = new List<DrugStockSummaryDto>();
        foreach (var drug in drugs)
        {
            var stocks = allStocks.Where(s => s.DrugId == drug.Id && s.QtyRemaining > 0).ToList();
            var totalQty = stocks.Sum(s => s.QtyRemaining);
            var batchCount = stocks.Count;
            var earliestExpiry = stocks.Count > 0 ? stocks.Min(s => s.ExpiryDate) : (DateOnly?)null;

            result.Add(new DrugStockSummaryDto(
                drug.Id,
                drug.GenericNameCn,
                drug.Spec,
                drug.Unit,
                totalQty,
                batchCount,
                earliestExpiry,
                drug.RetailPriceRef));
        }

        return result;
    }

    public async Task<IReadOnlyList<StockBatchDto>> GetStockBatchesAsync(long drugId, CancellationToken ct = default)
    {
        // P1：权限检查——读取库存批次需 Doctor/Nurse/Readonly 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly);

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
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly);

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
}
