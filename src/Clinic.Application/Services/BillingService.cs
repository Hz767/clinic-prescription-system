using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Clinic.Application.Validators;
using Clinic.Domain.Entities;
using Clinic.Domain.Interfaces;
using Clinic.Shared.Enums;
using FluentValidation;

namespace Clinic.Application.Services;

/// <summary>
/// 收费结算服务实现。
/// 记录收费流水（PaymentLog），生成日报表（按日期聚合）。
/// </summary>
public class BillingService : IBillingService
{
    /// <summary>P1 H-01：收费操作互斥锁，防止并发导致重复收费</summary>
    private static readonly SemaphoreSlim _paymentLock = new(1, 1);

    /// <summary>P2：退费操作互斥锁，防止并发导致重复退费</summary>
    private static readonly SemaphoreSlim _refundLock = new(1, 1);

    private readonly IRepository<PaymentLog> _paymentRepo;
    private readonly IRepository<Prescription> _prescriptionRepo;
    private readonly IRepository<DrugOut> _drugOutRepo;
    private readonly IRepository<DrugStock> _stockRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IValidator<RecordPaymentRequest> _recordPaymentValidator;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IAuditService _auditService;
    private readonly IUserSession _session;

    public BillingService(
        IRepository<PaymentLog> paymentRepo,
        IRepository<Prescription> prescriptionRepo,
        IRepository<DrugOut> drugOutRepo,
        IRepository<DrugStock> stockRepo,
        IUnitOfWork unitOfWork,
        IClock clock,
        IValidator<RecordPaymentRequest> recordPaymentValidator,
        IPermissionChecker permissionChecker,
        IAuditService auditService,
        IUserSession session)
    {
        _paymentRepo = paymentRepo;
        _prescriptionRepo = prescriptionRepo;
        _drugOutRepo = drugOutRepo;
        _stockRepo = stockRepo;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _recordPaymentValidator = recordPaymentValidator;
        _permissionChecker = permissionChecker;
        _auditService = auditService;
        _session = session;
    }

    public async Task<long> RecordPaymentAsync(
        long prescriptionId, int method, decimal amount,
        long operatorId, string? posSerialNo, string? note,
        CancellationToken ct = default)
    {
        // 权限检查：收费仅限 Doctor
        _permissionChecker.RequireCanBill();

        // P1 H-03：校验 operatorId 必须为当前登录用户，防止冒名收费
        if (operatorId != _session.UserId)
            throw new UnauthorizedAccessException("只能以本人身份收费");

        // 输入验证：处方 ID、收费方式、金额、操作人等由 FluentValidation 处理
        var request = new RecordPaymentRequest(prescriptionId, method, amount, operatorId, posSerialNo, note);
        var validationResult = await _recordPaymentValidator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        // P1 H-01：使用互斥锁防止并发导致重复收费
        await _paymentLock.WaitAsync(ct);
        try
        {
            var prescription = await _prescriptionRepo.GetByIdAsync(prescriptionId, ct);
            if (prescription is null)
                throw new InvalidOperationException("处方不存在");

            // 《处方管理办法》要求：处方须经药师审核后方可收费
            if (prescription.Status == PrescriptionStatus.Saved)
                throw new InvalidOperationException(
                    "处方尚未经药师审核，请先在「药师审核」中审核通过后再收费");
            if (prescription.Status != PrescriptionStatus.Reviewed)
                throw new InvalidOperationException(
                    $"处方当前状态为「{prescription.Status}」，仅「已审核」状态的处方可收费");

            // P1 H-02：金额校验——收费金额必须与处方金额一致
            if (amount != prescription.TotalAmount)
                throw new InvalidOperationException(
                    $"收费金额 ¥{amount:F2} 与处方金额 ¥{prescription.TotalAmount:F2} 不一致");

            // 幂等性检查：同一处方不允许重复收费
            var existingPayments = await _paymentRepo.FindAsync(
                p => p.PrescriptionId == prescriptionId, ct);
            if (existingPayments.Count > 0)
                throw new InvalidOperationException(
                    $"处方 {prescription.NoYearSeq} 已收费（流水号：{existingPayments[0].Id}），不允许重复收费");

            // P1：使用显式事务保证收费操作的原子性
            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var payment = new PaymentLog
                {
                    PrescriptionId = prescriptionId,
                    Method = (PaymentMethod)method,
                    Amount = amount,
                    OccurredAt = _clock.Now,
                    OperatorId = operatorId,
                    PosSerialNo = posSerialNo?.Trim(),
                    Note = note?.Trim()
                };

                await _paymentRepo.AddAsync(payment, ct);

                // 收费成功后处方状态变为 Paid（已保存 → 已收费）
                prescription.Status = PrescriptionStatus.Paid;
                _prescriptionRepo.Update(prescription);

                await _unitOfWork.SaveChangesAsync(ct);

                // 审计日志（事务内，与业务操作原子提交）
                await _auditService.LogAsync("PAYMENT_RECORD",
                    $"Prescription:{prescriptionId}",
                    $"Method:{(PaymentMethod)method} Amount:{amount}", ct);

                await _unitOfWork.CommitAsync(ct);

                return payment.Id;
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }
        }
        finally
        {
            _paymentLock.Release();
        }
    }

    /// <summary>
    /// 独立退费（P2）：将已收费处方退费。
    /// 流程：验证处方为 Paid → 生成反转支付记录（IsReversal=true）→ 处方状态改为 Voided
    ///       → 恢复库存（冲正出库记录）→ 审计日志。全程事务保护。
    /// </summary>
    public async Task RefundAsync(
        long prescriptionId, long operatorId, string? reason,
        CancellationToken ct = default)
    {
        // 权限检查：退费仅限 Doctor
        _permissionChecker.RequireCanBill();

        // 校验 operatorId 必须为当前登录用户，防止冒名退费
        if (operatorId != _session.UserId)
            throw new UnauthorizedAccessException("只能以本人身份退费");

        // P2：使用互斥锁防止并发导致重复退费
        await _refundLock.WaitAsync(ct);
        try
        {
            var prescription = await _prescriptionRepo.GetByIdAsync(prescriptionId, ct);
            if (prescription is null)
                throw new InvalidOperationException("处方不存在");

            if (prescription.Status != PrescriptionStatus.Paid)
                throw new InvalidOperationException(
                    $"处方当前状态为「{prescription.Status}」，仅已收费（Paid）处方可退费");

            // 幂等性检查：确认尚未生成过退费冲正记录
            var existingReversals = await _paymentRepo.FindAsync(
                p => p.PrescriptionId == prescriptionId && p.IsReversal, ct);
            if (existingReversals.Count > 0)
                throw new InvalidOperationException(
                    $"处方 {prescription.NoYearSeq} 已退费，不允许重复退费");

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var now = _clock.Now;

                // 1. 生成反转支付记录（IsReversal=true）
                var payments = await _paymentRepo.FindAsync(
                    p => p.PrescriptionId == prescriptionId && !p.IsReversal, ct);

                foreach (var payment in payments)
                {
                    await _paymentRepo.AddAsync(new PaymentLog
                    {
                        PrescriptionId = payment.PrescriptionId,
                        Method = payment.Method,
                        Amount = payment.Amount,
                        OccurredAt = now,
                        OperatorId = operatorId,
                        PosSerialNo = payment.PosSerialNo,
                        Note = string.IsNullOrWhiteSpace(reason)
                            ? $"独立退费冲正：原流水 {payment.Id}"
                            : $"独立退费冲正：{reason.Trim()}，原流水 {payment.Id}",
                        IsReversal = true
                    }, ct);
                }

                // 2. 处方状态改为 Voided
                prescription.Status = PrescriptionStatus.Voided;
                prescription.VoidReason = string.IsNullOrWhiteSpace(reason)
                    ? "独立退费"
                    : $"独立退费：{reason.Trim()}";
                _prescriptionRepo.Update(prescription);

                // 3. 恢复库存：查找该处方的所有出库记录（非冲正），逐笔恢复
                await RestoreInventoryAsync(prescriptionId, operatorId, ct);

                // 4. 审计日志（事务内，与业务操作原子提交）
                await _auditService.LogAsync("PAYMENT_REFUND",
                    $"Prescription:{prescriptionId}",
                    $"Operator:{operatorId} Reason:{reason ?? "独立退费"}", ct);

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
            _refundLock.Release();
        }
    }

    /// <summary>
    /// 恢复处方已扣减的库存（退费时调用）。
    /// 查找该处方所有 IsReversal=false 的 DrugOut 记录，
    /// 逐笔恢复 DrugStock 库存，并生成 IsReversal=true 的冲正出库记录。
    /// </summary>
    private async Task RestoreInventoryAsync(
        long prescriptionId, long operatorId, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        var outRecords = await _drugOutRepo.FindAsync(
            o => o.PrescriptionId == prescriptionId && !o.IsReversal, ct);

        if (outRecords.Count == 0)
            return; // 处方未出库（理论上不应发生），无需恢复

        foreach (var outRecord in outRecords)
        {
            // 恢复对应库存批次
            var stocks = await _stockRepo.FindAsync(
                s => s.DrugId == outRecord.DrugId && s.BatchNo == outRecord.BatchNo, ct);
            var stock = stocks.FirstOrDefault();

            if (stock is not null)
            {
                stock.QtyRemaining += outRecord.Qty;
                _stockRepo.Update(stock);
            }
            else
            {
                // 库存批次不存在（可能已被清理），重新创建
                stock = new DrugStock
                {
                    DrugId = outRecord.DrugId,
                    BatchNo = outRecord.BatchNo,
                    ExpiryDate = DateOnly.FromDateTime(now.AddDays(365)),
                    QtyRemaining = outRecord.Qty,
                    CostPrice = 0m,
                    ReceivedAt = now
                };
                await _stockRepo.AddAsync(stock, ct);
            }

            // 生成冲正出库记录
            await _drugOutRepo.AddAsync(new DrugOut
            {
                DrugId = outRecord.DrugId,
                BatchNo = outRecord.BatchNo,
                Qty = outRecord.Qty,
                PrescriptionId = prescriptionId,
                OccurredAt = now,
                OperatorId = operatorId,
                IsReversal = true
            }, ct);
        }
    }

    public async Task<DailyReportDto?> GetDailyReportAsync(DateTime date, CancellationToken ct = default)
    {
        // P1：权限检查——读取日报表需 Doctor/Nurse/Readonly 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly);

        // 按 UTC 日期筛选（当天 00:00 ~ 次日 00:00）
        var dateStart = date.Date;
        var dateEnd = dateStart.AddDays(1);

        var payments = await _paymentRepo.FindAsync(
            p => p.OccurredAt >= dateStart && p.OccurredAt < dateEnd, ct);

        if (payments.Count == 0)
            return new DailyReportDto(dateStart, 0, 0m, 0m, 0m);

        // 冲正（退款）记录用于抵减收入：有效收费 = 非冲正合计 - 冲正合计
        var charges = payments.Where(p => !p.IsReversal).ToList();
        var reversals = payments.Where(p => p.IsReversal).ToList();

        var prescriptionCount = charges.Select(p => p.PrescriptionId).Distinct().Count();
        var totalAmount = Math.Round(
            charges.Sum(p => p.Amount) - reversals.Sum(p => p.Amount),
            2, MidpointRounding.AwayFromZero);
        var cashAmount = Math.Round(
            charges.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount)
            - reversals.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount),
            2, MidpointRounding.AwayFromZero);
        var posAmount = Math.Round(
            charges.Where(p => p.Method == PaymentMethod.Pos).Sum(p => p.Amount)
            - reversals.Where(p => p.Method == PaymentMethod.Pos).Sum(p => p.Amount),
            2, MidpointRounding.AwayFromZero);

        return new DailyReportDto(dateStart, prescriptionCount, totalAmount, cashAmount, posAmount);
    }

    public async Task<IReadOnlyList<PaymentRecordDto>> GetPaymentHistoryAsync(
        DateTime? fromDate = null, DateTime? toDate = null, CancellationToken ct = default)
    {
        // P1：权限检查——读取收费流水需 Doctor/Nurse/Readonly 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly);

        var payments = await _paymentRepo.GetAllAsync(ct);

        if (fromDate.HasValue)
            payments = payments.Where(p => p.OccurredAt >= fromDate.Value).ToList();

        if (toDate.HasValue)
            payments = payments.Where(p => p.OccurredAt < toDate.Value.AddDays(1)).ToList();

        if (payments.Count == 0)
            return [];

        // 批量获取处方和患者信息
        var prescriptionIds = payments.Select(p => p.PrescriptionId).Distinct().ToList();
        var prescriptionDict = new Dictionary<long, (string No, long PatientId)>();

        foreach (var prescriptionId in prescriptionIds)
        {
            var prescription = await _prescriptionRepo.GetByIdAsync(prescriptionId, ct);
            if (prescription is not null)
                prescriptionDict[prescriptionId] = (prescription.NoYearSeq, prescription.PatientId);
        }

        return payments
            .OrderByDescending(p => p.OccurredAt)
            .Select(p =>
            {
                var presInfo = prescriptionDict.GetValueOrDefault(p.PrescriptionId);
                var methodText = p.Method == PaymentMethod.Cash ? "现金" : "POS";
                return new PaymentRecordDto(
                    p.Id,
                    p.PrescriptionId,
                    presInfo.No ?? string.Empty,
                    string.Empty, // PatientName 需要额外查询，暂留空
                    (int)p.Method,
                    methodText,
                    p.Amount,
                    p.OccurredAt,
                    p.OperatorId,
                    string.Empty, // OperatorName 暂留空
                    p.PosSerialNo,
                    p.Note,
                    p.IsReversal);
            })
            .ToList();
    }
}
