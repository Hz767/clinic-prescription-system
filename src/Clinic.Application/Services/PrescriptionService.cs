using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Clinic.Application.Validators;
using Clinic.Domain.Entities;
using Clinic.Domain.Interfaces;
using Clinic.Shared.Enums;
using FluentValidation;

namespace Clinic.Application.Services;

/// <summary>
/// 处方开具服务实现。核心业务流程：
/// 1. 创建处方（生成年度编号）→ 2. 添加药品明细（含药品交互检查）→ 3. 保存（计算金额 + 库存扣减 + 事务提交）
/// 处方编号格式：{年份}-{5位序号}，如 2026-00001
/// 保存时按 FIFO（效期优先）策略扣减库存，生成 DrugOut 出库流水。
/// 作废时生成 IsReversal=true 的冲正出库记录，恢复 DrugStock 库存。
/// </summary>
public class PrescriptionService : IPrescriptionService
{
    private const int MaxDrugItemsPerPrescription = 5;
    private const int EmergencyMaxDays = 3;
    private const int NormalMaxDays = 7;
    private const int ExtendedMaxDays = 84;

    /// <summary>P0 修复：处方编号生成互斥锁，防止并发导致序号重复</summary>
    private static readonly SemaphoreSlim _prescriptionNoLock = new(1, 1);

    /// <summary>P0 修复：处方明细添加互斥锁，防止并发突破药品品种上限</summary>
    private static readonly SemaphoreSlim _addItemLock = new(1, 1);

    /// <summary>P0 修复：库存 FIFO 扣减互斥锁，防止并发导致超卖</summary>
    private static readonly SemaphoreSlim _inventoryLock = new(1, 1);

    private readonly IRepository<Prescription> _prescriptionRepo;
    private readonly IRepository<PrescriptionItem> _itemRepo;
    private readonly IRepository<DrugMaster> _drugRepo;
    private readonly IRepository<DrugStock> _stockRepo;
    private readonly IRepository<DrugOut> _drugOutRepo;
    private readonly IRepository<DrugInteraction> _interactionRepo;
    private readonly IRepository<Patient> _patientRepo;
    private readonly IRepository<SysUser> _userRepo;
    private readonly IRepository<PaymentLog> _paymentRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IValidator<CreatePrescriptionRequest> _createPrescriptionValidator;
    private readonly IValidator<AddPrescriptionItemRequest> _addItemValidator;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IAuditService _auditService;
    private readonly IPdfService _pdfService;
    private readonly IEncryptionService _encryption;
    private readonly IUserSession _session;

    public PrescriptionService(
        IRepository<Prescription> prescriptionRepo,
        IRepository<PrescriptionItem> itemRepo,
        IRepository<DrugMaster> drugRepo,
        IRepository<DrugStock> stockRepo,
        IRepository<DrugOut> drugOutRepo,
        IRepository<DrugInteraction> interactionRepo,
        IRepository<Patient> patientRepo,
        IRepository<SysUser> userRepo,
        IRepository<PaymentLog> paymentRepo,
        IUnitOfWork unitOfWork,
        IClock clock,
        IValidator<CreatePrescriptionRequest> createPrescriptionValidator,
        IValidator<AddPrescriptionItemRequest> addItemValidator,
        IPermissionChecker permissionChecker,
        IAuditService auditService,
        IPdfService pdfService,
        IEncryptionService encryption,
        IUserSession session)
    {
        _prescriptionRepo = prescriptionRepo;
        _itemRepo = itemRepo;
        _drugRepo = drugRepo;
        _stockRepo = stockRepo;
        _drugOutRepo = drugOutRepo;
        _interactionRepo = interactionRepo;
        _patientRepo = patientRepo;
        _userRepo = userRepo;
        _paymentRepo = paymentRepo;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _createPrescriptionValidator = createPrescriptionValidator;
        _addItemValidator = addItemValidator;
        _permissionChecker = permissionChecker;
        _auditService = auditService;
        _pdfService = pdfService;
        _encryption = encryption;
        _session = session;
    }

    public async Task<long> CreatePrescriptionAsync(
        long patientId, long doctorId, string? chiefComplaint, string diagnosisText,
        string? diagnosisCode, int prescriptionType,
        string? extendedReason,
        decimal? weight = null, decimal? temperature = null,
        int? systolicBP = null, int? diastolicBP = null, int? heartRate = null,
        CancellationToken ct = default)
    {
        // 权限检查：处方开具仅限 Doctor
        _permissionChecker.RequireCanPrescribe();

        // P1 H-06：校验 doctorId 必须为当前登录用户，防止冒名开方
        if (doctorId != _session.UserId)
            throw new UnauthorizedAccessException("只能以本人身份开具处方");

        // 输入验证：ID 有效性、诊断内容、处方类型等由 FluentValidation 处理
        var request = new CreatePrescriptionRequest(
            patientId, doctorId, chiefComplaint, diagnosisText, diagnosisCode, prescriptionType, extendedReason,
            weight, temperature, systolicBP, diastolicBP, heartRate);
        var validationResult = await _createPrescriptionValidator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        var now = _clock.Now;
        var prescriptionNo = await GeneratePrescriptionNoAsync(now.Year, ct);

        var prescription = new Prescription
        {
            NoYearSeq = prescriptionNo,
            PatientId = patientId,
            DoctorId = doctorId,
            ChiefComplaint = chiefComplaint?.Trim(),
            DiagnosisText = diagnosisText.Trim(),
            DiagnosisCode = diagnosisCode?.Trim(),
            Type = (PrescriptionType)prescriptionType,
            ExtendedReason = extendedReason?.Trim(),
            Status = PrescriptionStatus.Draft,
            TotalAmount = 0m,
            // 本次就诊体征（实时变量，保存到处方记录中）
            Weight = weight,
            Temperature = temperature,
            SystolicBP = systolicBP,
            DiastolicBP = diastolicBP,
            HeartRate = heartRate
        };

        await _prescriptionRepo.AddAsync(prescription, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        await SafeAuditAsync("PRESCRIPTION_CREATE",
            $"Prescription:{prescription.NoYearSeq}", $"PatientId:{patientId}", ct);

        return prescription.Id;
    }

    public async Task<long> AddPrescriptionItemAsync(
        long prescriptionId, long drugId, decimal dose, string doseUnit,
        string frequency, string route, int durationDays, decimal qty,
        CancellationToken ct = default)
    {
        // 权限检查：处方操作仅限 Doctor
        _permissionChecker.RequireCanPrescribe();

        // 输入验证：药品 ID、剂量、频次、疗程、数量等由 FluentValidation 处理
        var request = new AddPrescriptionItemRequest(
            prescriptionId, drugId, dose, doseUnit, frequency, route, durationDays, qty);
        var validationResult = await _addItemValidator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        var prescription = await _prescriptionRepo.GetByIdAsync(prescriptionId, ct);
        if (prescription is null)
            return 0;
        if (prescription.Status != PrescriptionStatus.Draft)
            throw new InvalidOperationException("处方已保存或已作废，不允许再添加药品");

        var drug = await _drugRepo.GetByIdAsync(drugId, ct);
        if (drug is null)
            return 0;

        // ── P2 领域规则：特殊药品管控 ──

        // 2.1 抗生素分级管控：特殊使用级抗菌药物需审批后使用
        if (drug.IsAntibiotic && drug.AntibioticLevel == AntibioticLevel.Special)
            throw new InvalidOperationException($"药品「{drug.GenericNameCn}」为特殊使用级抗菌药物，需审批后使用");

        // 2.2 毒性药品管控：毒性药品需特殊审批
        if (drug.IsToxicDrug)
            throw new InvalidOperationException($"药品「{drug.GenericNameCn}」为毒性药品，需特殊审批");

        // ── 处方校验规则（P0 合规要求）──

        // P0 修复：使用互斥锁保护品种上限检查，防止并发突破 5 种限制
        await _addItemLock.WaitAsync(ct);
        try
        {
            // 1. 药品品种上限：单张处方药品数 ≤ 5（法规强制）
            var existingItems = await _itemRepo.FindAsync(
                i => i.PrescriptionId == prescriptionId, ct);
            if (existingItems.Count >= MaxDrugItemsPerPrescription)
                throw new InvalidOperationException(
                    $"单张处方药品品种不得超过 {MaxDrugItemsPerPrescription} 种，当前已有 {existingItems.Count} 种");

            // P1 H-05：重复药品检查——同一药品不允许在同一处方中重复添加
            var existingItem = existingItems.FirstOrDefault(i => i.DrugId == drugId);
            if (existingItem is not null)
                throw new InvalidOperationException($"处方中已包含药品「{drug.GenericNameCn}」，不允许重复添加");

            // 2. 用药天数限制：急诊 ≤ 3 天；普通 ≤ 7 天（有延长理由时 ≤ 84 天）
            var maxDays = prescription.Type == PrescriptionType.Emergency
                ? EmergencyMaxDays
                : string.IsNullOrWhiteSpace(prescription.ExtendedReason)
                    ? NormalMaxDays
                    : ExtendedMaxDays;

            if (durationDays > maxDays)
            {
                var typeText = prescription.Type == PrescriptionType.Emergency ? "急诊" : "普通";
                var extendHint = prescription.Type == PrescriptionType.Normal && maxDays == NormalMaxDays
                    ? "，如需长期用药请在处方信息中填写延长理由"
                    : "";
                throw new InvalidOperationException(
                    $"{typeText}处方用药天数不得超过 {maxDays} 天，当前为 {durationDays} 天{extendHint}");
            }

            // 3. 过敏史匹配拦截：检查药品名称是否与患者过敏史匹配
            await CheckAllergyAsync(prescription.PatientId, drug, ct);

            // 药品交互检查：阻断 Major 级交互，记录 Moderate 级警告
            await CheckDrugInteractionsAsync(prescriptionId, drug, existingItems, ct);

            var unitPrice = drug.RetailPriceRef ?? 0m;
            var subtotal = Math.Round(unitPrice * qty, 2, MidpointRounding.AwayFromZero);

            var item = new PrescriptionItem
            {
                PrescriptionId = prescriptionId,
                DrugId = drugId,
                DrugName = drug.GenericNameCn,
                Spec = drug.Spec,
                Dose = dose,
                DoseUnit = doseUnit,
                Frequency = frequency,
                Route = route,
                DurationDays = durationDays,
                Qty = qty,
                UnitPrice = unitPrice,
                Subtotal = subtotal
            };

            await _itemRepo.AddAsync(item, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            await SafeAuditAsync("PRESCRIPTION_ADD_ITEM",
                $"Prescription:{prescriptionId}", $"Drug:{drug.GenericNameCn} Qty:{qty}", ct);

            return item.Id;
        }
        finally
        {
            _addItemLock.Release();
        }
    }

    /// <summary>
    /// 更新处方明细（支持内联编辑后持久化）。
    /// 仅 Draft 状态处方可修改明细。更新剂量/频次/疗程后自动重算数量和小计。
    /// </summary>
    public async Task<bool> UpdatePrescriptionItemAsync(
        long prescriptionId, long itemId, decimal dose, string doseUnit,
        string frequency, string route, int durationDays, decimal qty,
        CancellationToken ct = default)
    {
        _permissionChecker.RequireCanPrescribe();

        var prescription = await _prescriptionRepo.GetByIdAsync(prescriptionId, ct);
        if (prescription is null)
            return false;
        if (prescription.Status != PrescriptionStatus.Draft)
            throw new InvalidOperationException("处方已保存或已作废，不允许修改药品明细");

        var items = await _itemRepo.FindAsync(i => i.PrescriptionId == prescriptionId, ct);
        var item = items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
            throw new InvalidOperationException($"处方中不存在明细 ID={itemId}");

        // 用药天数限制校验
        var maxDays = prescription.Type == PrescriptionType.Emergency
            ? EmergencyMaxDays
            : string.IsNullOrWhiteSpace(prescription.ExtendedReason)
                ? NormalMaxDays
                : ExtendedMaxDays;
        if (durationDays > maxDays)
            throw new InvalidOperationException($"用药天数不得超过 {maxDays} 天");

        var drug = await _drugRepo.GetByIdAsync(item.DrugId, ct);
        var unitPrice = drug?.RetailPriceRef ?? item.UnitPrice;
        var subtotal = Math.Round(unitPrice * qty, 2, MidpointRounding.AwayFromZero);

        item.Dose = dose;
        item.DoseUnit = doseUnit;
        item.Frequency = frequency;
        item.Route = route;
        item.DurationDays = durationDays;
        item.Qty = qty;
        item.UnitPrice = unitPrice;
        item.Subtotal = subtotal;

        _itemRepo.Update(item);
        await _unitOfWork.SaveChangesAsync(ct);

        await SafeAuditAsync("PRESCRIPTION_UPDATE_ITEM",
            $"Prescription:{prescriptionId}",
            $"ItemId:{itemId} Drug:{item.DrugName} Qty:{qty}", ct);

        return true;
    }

    /// <summary>
    /// 删除处方中的指定药品明细（P2）。
    /// 仅 Draft 状态的处方允许删除明细，已保存/已收费/已作废的处方不可修改。
    /// 通过明细 ID 定位要删除的药品项。
    /// </summary>
    public async Task RemovePrescriptionItemAsync(
        long prescriptionId, long itemId, CancellationToken ct = default)
    {
        // 权限检查：处方操作仅限 Doctor
        _permissionChecker.RequireCanPrescribe();

        var prescription = await _prescriptionRepo.GetByIdAsync(prescriptionId, ct);
        if (prescription is null)
            throw new InvalidOperationException("处方不存在");

        if (prescription.Status != PrescriptionStatus.Draft)
            throw new InvalidOperationException("处方已保存或已作废，不允许删除药品明细");

        var items = await _itemRepo.FindAsync(
            i => i.PrescriptionId == prescriptionId, ct);
        var item = items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
            throw new InvalidOperationException(
                $"处方 {prescription.NoYearSeq} 中不存在明细 ID={itemId}");

        _itemRepo.SoftDelete(item);
        await _unitOfWork.SaveChangesAsync(ct);

        await SafeAuditAsync("PRESCRIPTION_REMOVE_ITEM",
            $"Prescription:{prescriptionId}",
            $"ItemId:{itemId} Drug:{item.DrugName}", ct);
    }

    public async Task<bool> SavePrescriptionAsync(long prescriptionId, CancellationToken ct = default)
    {
        // 权限检查：处方保存仅限 Doctor
        _permissionChecker.RequireCanPrescribe();

        var prescription = await _prescriptionRepo.GetByIdAsync(prescriptionId, ct);
        if (prescription is null)
            return false;
        if (prescription.Status != PrescriptionStatus.Draft)
            throw new InvalidOperationException("处方已保存或已作废，无法再次保存");

        var items = await _itemRepo.FindAsync(i => i.PrescriptionId == prescriptionId, ct);
        if (items.Count == 0)
            throw new InvalidOperationException("处方没有明细，无法保存");

        // ── P2 领域规则：禁忌症检查 ──
        // 获取患者慢病标签，与药品禁忌症标签交叉匹配
        var patient = await _patientRepo.GetByIdAsync(prescription.PatientId, ct);
        if (patient?.ChronicTags is not null)
        {
            var chronicTags = patient.ChronicTags.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in items)
            {
                var drug = await _drugRepo.GetByIdAsync(item.DrugId, ct);
                if (drug?.ContraindicationTags is not null)
                {
                    var contraTags = drug.ContraindicationTags.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    var matched = chronicTags.Intersect(contraTags, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                    if (matched is not null)
                        throw new InvalidOperationException($"药品「{drug.GenericNameCn}」禁忌症包含患者慢病「{matched}」，禁止开具");
                }
            }
        }

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // 1. 计算总金额
            prescription.TotalAmount = Math.Round(
                items.Sum(i => i.Subtotal), 2, MidpointRounding.AwayFromZero);
            // 保存后状态变为 Saved（草稿 → 已保存），待药师审核
            prescription.Status = PrescriptionStatus.Saved;
            _prescriptionRepo.Update(prescription);

            // 2. 库存扣减（FIFO：效期优先，库存不足时抛异常触发回滚）
            await DeductInventoryAsync(items, prescriptionId, prescription.DoctorId, ct);

            // 3. 审计日志（事务内，与业务操作原子提交）
            await _auditService.LogAsync("PRESCRIPTION_SAVE",
                $"Prescription:{prescriptionId}",
                $"Amount:{prescription.TotalAmount} Items:{items.Count}", ct);

            await _unitOfWork.CommitAsync(ct);
            return true;
        }
        catch
        {
            await _unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// 药师审核处方（《处方管理办法》要求药师审核后方可收费）。
    /// 仅 Saved 状态处方可审核，审核后变为 Reviewed 状态。
    /// Doctor 或 Nurse 角色可执行审核（诊所场景下医生可兼任药师）。
    /// </summary>
    public async Task<bool> ReviewPrescriptionAsync(
        long prescriptionId, string? reviewNote, CancellationToken ct = default)
    {
        // 权限检查：药师审核需 Doctor 或 Nurse 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse);

        var prescription = await _prescriptionRepo.GetByIdAsync(prescriptionId, ct);
        if (prescription is null)
            return false;

        if (prescription.Status != PrescriptionStatus.Saved)
            throw new InvalidOperationException(
                $"处方当前状态为「{GetStatusText(prescription.Status)}」，仅「已保存」状态的处方可审核");

        // 审核前再次执行安全检查（防止保存后数据变更）
        var items = await _itemRepo.FindAsync(i => i.PrescriptionId == prescriptionId, ct);
        if (items.Count == 0)
            throw new InvalidOperationException("处方没有明细，无法审核");

        // 复查过敏史
        foreach (var item in items)
        {
            var drug = await _drugRepo.GetByIdAsync(item.DrugId, ct);
            if (drug is not null)
                await CheckAllergyAsync(prescription.PatientId, drug, ct);
        }

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            prescription.Status = PrescriptionStatus.Reviewed;
            _prescriptionRepo.Update(prescription);

            var reviewerId = _session.UserId ?? 0;
            await _auditService.LogAsync("PRESCRIPTION_REVIEW",
                $"Prescription:{prescriptionId}",
                $"Reviewer:{reviewerId} Note:{reviewNote ?? "审核通过"}", ct);

            await _unitOfWork.CommitAsync(ct);
            return true;
        }
        catch
        {
            await _unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// AI 辅助预审处方（规则引擎，辅助药师审核，不替代人工决定）。
    /// 基于《处方管理办法》和《电子病历系统功能规范》要求执行以下检查：
    /// 1. 药物相互作用（Major阻断 / Moderate警告）
    /// 2. 过敏史匹配
    /// 3. 用药天数合规性（急诊≤3天 / 普通≤7天 / 慢性≤84天）
    /// 4. 抗菌药物分级使用检查
    /// 5. 重复用药检查
    /// 6. 禁忌症匹配
    /// 返回结构化预审报告文本。
    /// </summary>
    public async Task<string> GetAiReviewSuggestionsAsync(
        long prescriptionId, CancellationToken ct = default)
    {
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly);

        var prescription = await _prescriptionRepo.GetByIdAsync(prescriptionId, ct);
        if (prescription is null)
            return "处方不存在";

        var items = await _itemRepo.FindAsync(i => i.PrescriptionId == prescriptionId, ct);
        var patient = await _patientRepo.GetByIdAsync(prescription.PatientId, ct);
        var allInteractions = await _interactionRepo.GetAllAsync(ct);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══ AI 辅助预审报告 ═══");
        sb.AppendLine($"处方编号：{prescription.NoYearSeq}");
        sb.AppendLine($"处方类型：{GetTypeText(prescription.Type)}");
        sb.AppendLine($"诊断：{prescription.DiagnosisText}");
        sb.AppendLine($"药品数量：{items.Count}/5 种");
        sb.AppendLine();

        var hasWarnings = false;
        var hasBlockers = false;

        // 1. 药品品种上限检查
        if (items.Count > 5)
        {
            sb.AppendLine("【严重】药品品种超过5种上限，违反《处方管理办法》第六条");
            hasBlockers = true;
        }

        // 2. 用药天数合规性
        var maxDays = prescription.Type == PrescriptionType.Emergency
            ? 3 : string.IsNullOrWhiteSpace(prescription.ExtendedReason) ? 7 : 84;
        foreach (var item in items)
        {
            if (item.DurationDays > maxDays)
            {
                sb.AppendLine($"【警告】{item.DrugName} 疗程 {item.DurationDays} 天超过上限 {maxDays} 天");
                hasWarnings = true;
            }
        }

        // 3. 重复用药检查
        var drugGroups = items.GroupBy(i => i.DrugId);
        foreach (var group in drugGroups.Where(g => g.Count() > 1))
        {
            sb.AppendLine($"【警告】重复用药：{group.First().DrugName} 出现 {group.Count()} 次");
            hasWarnings = true;
        }

        // 4. 抗菌药物分级检查
        foreach (var item in items)
        {
            var drug = await _drugRepo.GetByIdAsync(item.DrugId, ct);
            if (drug?.IsAntibiotic == true && drug.AntibioticLevel == AntibioticLevel.Special)
            {
                sb.AppendLine($"【严重】{item.DrugName} 为特殊使用级抗菌药物，需审批后使用");
                hasBlockers = true;
            }
            if (drug?.IsToxicDrug == true)
            {
                sb.AppendLine($"【严重】{item.DrugName} 为毒性药品，需特殊审批");
                hasBlockers = true;
            }
        }

        // 5. 药物相互作用检查
        foreach (var item in items)
        {
            var drug = await _drugRepo.GetByIdAsync(item.DrugId, ct);
            if (drug is null) continue;

            foreach (var other in items.Where(i => i.DrugId != item.DrugId))
            {
                foreach (var interaction in allInteractions)
                {
                    var nameAMatches = NamesMatch(interaction.DrugNameA, drug.GenericNameCn) ||
                                       NamesMatch(interaction.DrugNameA, drug.GenericNameEn ?? "");
                    var nameBMatches = NamesMatch(interaction.DrugNameB, other.DrugName);

                    if (nameAMatches && nameBMatches)
                    {
                        if (interaction.Level == DrugInteractionLevel.Major)
                        {
                            sb.AppendLine($"【严重】药物相互作用（Major）：{item.DrugName} 与 {other.DrugName}");
                            hasBlockers = true;
                        }
                        else if (interaction.Level == DrugInteractionLevel.Moderate)
                        {
                            sb.AppendLine($"【警告】药物相互作用（Moderate）：{item.DrugName} 与 {other.DrugName}");
                            hasWarnings = true;
                        }
                    }
                }
            }
        }

        // 6. 过敏史检查
        if (patient?.Allergies is not null)
        {
            var allergyKeywords = patient.Allergies
                .Split([',', '，', '、', ';', '；'], StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList();

            foreach (var item in items)
            {
                var drug = await _drugRepo.GetByIdAsync(item.DrugId, ct);
                if (drug is null) continue;

                var drugNames = new[] { drug.GenericNameCn, drug.GenericNameEn }
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!.Trim().ToLowerInvariant())
                    .ToList();

                foreach (var keyword in allergyKeywords)
                {
                    var kw = keyword.ToLowerInvariant();
                    foreach (var drugName in drugNames)
                    {
                        if (drugName.Contains(kw))
                        {
                            sb.AppendLine($"【严重】过敏史匹配：患者对「{keyword}」过敏，处方含「{item.DrugName}」");
                            hasBlockers = true;
                        }
                    }
                }
            }
        }

        // 7. 禁忌症检查
        if (patient?.ChronicTags is not null)
        {
            var chronicTags = patient.ChronicTags.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in items)
            {
                var drug = await _drugRepo.GetByIdAsync(item.DrugId, ct);
                if (drug?.ContraindicationTags is not null)
                {
                    var contraTags = drug.ContraindicationTags.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    var matched = chronicTags.Intersect(contraTags, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                    if (matched is not null)
                    {
                        sb.AppendLine($"【严重】禁忌症匹配：患者慢病「{matched}」与药品「{item.DrugName}」禁忌");
                        hasBlockers = true;
                    }
                }
            }
        }

        // 总结
        sb.AppendLine();
        sb.AppendLine("═══ 预审结论 ═══");
        if (hasBlockers)
            sb.AppendLine("❌ 存在严重问题，建议不予审核通过");
        else if (hasWarnings)
            sb.AppendLine("⚠ 存在警告事项，请药师确认后决定");
        else
            sb.AppendLine("✅ 未发现明显问题，建议审核通过");

        sb.AppendLine();
        sb.AppendLine("注：本报告由规则引擎自动生成，仅辅助参考，不替代药师人工审核决定。");

        return sb.ToString();
    }

    /// <summary>获取处方状态中文文本</summary>
    private static string GetStatusText(PrescriptionStatus status) => status switch
    {
        PrescriptionStatus.Draft => "草稿",
        PrescriptionStatus.Saved => "已保存（待审核）",
        PrescriptionStatus.Reviewed => "已审核",
        PrescriptionStatus.Paid => "已收费",
        PrescriptionStatus.Voided => "已作废",
        _ => status.ToString()
    };

    /// <summary>获取处方类型中文文本</summary>
    private static string GetTypeText(PrescriptionType type) => type switch
    {
        PrescriptionType.Normal => "普通处方",
        PrescriptionType.Emergency => "急诊处方",
        PrescriptionType.Pediatric => "儿科处方",
        _ => type.ToString()
    };

    /// <summary>
    /// 药品交互检查：检查新添加的药品与处方已有药品之间是否存在 Major 级交互。
    /// 匹配策略：按药品名称模糊匹配（DrugNameA/DrugNameB ↔ GenericNameCn/GenericNameEn），
    /// 双向匹配（A↔B 和 B↔A 均检查）。
    /// 发现 Major 级交互时抛出 InvalidOperationException，阻止添加。
    /// P1 H-08：Moderate 级交互不阻断操作，但记录到审计日志作为警告。
    /// </summary>
    private async Task CheckDrugInteractionsAsync(
        long prescriptionId, DrugMaster newDrug,
        IReadOnlyList<PrescriptionItem> existingItems, CancellationToken ct)
    {
        if (existingItems.Count == 0)
            return; // 第一项药品，无需检查

        // 获取所有交互数据（诊所药品目录有限，全量加载后内存匹配）
        var allInteractions = await _interactionRepo.GetAllAsync(ct);
        if (allInteractions.Count == 0)
            return; // 无交互数据，跳过检查

        var newDrugNames = new[] { newDrug.GenericNameCn, newDrug.GenericNameEn }
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!.Trim())
            .ToList();

        var moderateWarnings = new List<string>();

        foreach (var existingItem in existingItems)
        {
            var existingDrugName = existingItem.DrugName;

            foreach (var interaction in allInteractions)
            {
                // 双向匹配：新药品 ↔ 已有药品
                var nameAMatchesNew = NamesMatch(interaction.DrugNameA, newDrugNames);
                var nameBMatchesExisting = NamesMatch(interaction.DrugNameB, existingDrugName);
                var nameAMatchesExisting = NamesMatch(interaction.DrugNameA, existingDrugName);
                var nameBMatchesNew = NamesMatch(interaction.DrugNameB, newDrugNames);

                if ((nameAMatchesNew && nameBMatchesExisting) ||
                    (nameAMatchesExisting && nameBMatchesNew))
                {
                    if (interaction.Level == DrugInteractionLevel.Major)
                    {
                        // Major 级交互：阻断操作
                        throw new InvalidOperationException(
                            $"药品交互警告（Major）：「{newDrug.GenericNameCn}」与「{existingItem.DrugName}」" +
                            $"存在严重交互风险，不可同时开具。");
                    }
                    else if (interaction.Level == DrugInteractionLevel.Moderate)
                    {
                        // P1 H-08：Moderate 级交互不阻断，收集警告信息用于审计记录
                        moderateWarnings.Add(
                            $"「{newDrug.GenericNameCn}」与「{existingItem.DrugName}」存在中度交互风险");
                    }
                }
            }
        }

        // P1 H-08：Moderate 交互不阻断操作，但记录到审计日志
        if (moderateWarnings.Count > 0)
        {
            await SafeAuditAsync("DRUG_INTERACTION_MODERATE_WARNING",
                $"Prescription:{prescriptionId}",
                string.Join("; ", moderateWarnings), ct);
        }
    }

    /// <summary>
    /// 名称模糊匹配：交互表中的药品名称可能是不含剂型的短名（如"阿莫西林"），
    /// 而药品目录中是完整名（如"阿莫西林胶囊"）。
    /// 匹配规则：忽略大小写，交互表名称包含在药品目录名称中，或反过来。
    /// </summary>
    private static bool NamesMatch(string interactionName, string drugName)
    {
        if (string.IsNullOrEmpty(interactionName) || string.IsNullOrEmpty(drugName))
            return false;

        var iName = interactionName.Trim().ToLowerInvariant();
        var dName = drugName.Trim().ToLowerInvariant();

        return dName.Contains(iName) || iName.Contains(dName);
    }

    /// <summary>
    /// 名称模糊匹配（多名称版本）：检查交互表名称是否匹配药品的任一名称。
    /// </summary>
    private static bool NamesMatch(string interactionName, IReadOnlyList<string> drugNames)
    {
        foreach (var drugName in drugNames)
        {
            if (NamesMatch(interactionName, drugName))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 过敏史匹配检查：检查药品名称是否与患者过敏史中的任何关键词匹配。
    /// 过敏史为自由文本（如"青霉素过敏、磺胺类"），按逗号/顿号/分号分割后逐项匹配。
    /// 匹配规则：先剥离常见后缀词（过敏、过敏性、过敏史），再检查药品中文名或英文名是否包含关键词（忽略大小写）。
    /// 发现匹配时抛出 InvalidOperationException，阻止添加。
    /// </summary>
    private async Task CheckAllergyAsync(long patientId, DrugMaster drug, CancellationToken ct)
    {
        var patient = await _patientRepo.GetByIdAsync(patientId, ct);
        if (patient is null || string.IsNullOrWhiteSpace(patient.Allergies))
            return;

        // 分割过敏史关键词（支持中英文逗号、顿号、分号）
        var allergyKeywords = patient.Allergies
            .Split([',', '，', '、', ';', '；'], StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim())
            .Where(k => !string.IsNullOrEmpty(k))
            .ToList();

        if (allergyKeywords.Count == 0)
            return;

        // 剥离过敏史描述中的常见后缀词，提取药物关键词
        var cleanedKeywords = allergyKeywords
            .Select(k =>
            {
                // 去掉尾部描述词：过敏、过敏性、过敏史、不耐受、皮疹、荨麻疹等
                var trimmed = k;
                var suffixes = new[] { "过敏", "过敏性", "过敏史", "不耐受", "皮疹", "荨麻疹", "呼吸困难" };
                foreach (var suffix in suffixes)
                {
                    if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        trimmed = trimmed[..^suffix.Length].Trim();
                        break;
                    }
                }
                return trimmed;
            })
            .Where(k => !string.IsNullOrEmpty(k))
            .ToList();

        if (cleanedKeywords.Count == 0)
            return;

        var drugNames = new[] { drug.GenericNameCn, drug.GenericNameEn }
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!.Trim().ToLowerInvariant())
            .ToList();

        foreach (var keyword in cleanedKeywords)
        {
            var kw = keyword.ToLowerInvariant();
            foreach (var drugName in drugNames)
            {
                // P1 优化：单向匹配——药品名称包含过敏关键词即拦截
                // 去掉 kw.Contains(drugName) 反向匹配，避免短药品名误匹配长过敏描述
                if (drugName.Contains(kw))
                {
                    throw new InvalidOperationException(
                        $"过敏史拦截：患者过敏史中包含「{keyword}」，" +
                        $"与药品「{drug.GenericNameCn}」匹配，不可开具此药品");
                }
            }
        }
    }

    /// <summary>
    /// 按 FIFO（效期优先）策略扣减库存。
    /// 遍历处方明细，从最早过期的批次开始扣减，生成 DrugOut 出库流水。
    /// 库存不足时抛出 InvalidOperationException，事务回滚。
    /// </summary>
    private async Task DeductInventoryAsync(
        IReadOnlyList<PrescriptionItem> items, long prescriptionId, long operatorId,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var today = now.Date;

        // P0 修复：使用互斥锁保护库存扣减，防止并发导致超卖
        await _inventoryLock.WaitAsync(ct);
        try
        {
            foreach (var item in items)
            {
                // 获取该药品的所有可用库存批次，按效期升序（FIFO）
                var stocks = await _stockRepo.FindAsync(
                    s => s.DrugId == item.DrugId && s.QtyRemaining > 0, ct);

                // P2 领域规则：过期药品拦截——跳过已过期的批次
                var validStocks = stocks.Where(b => b.ExpiryDate > DateOnly.FromDateTime(today)).ToList();

                if (validStocks.Count == 0 && stocks.Count > 0)
                    throw new InvalidOperationException(
                        $"药品「{item.DrugName}」所有库存批次均已过期，无法开具");

                var orderedStocks = validStocks.OrderBy(s => s.ExpiryDate).ToList();
                var totalAvailable = orderedStocks.Sum(s => s.QtyRemaining);

                if (totalAvailable < item.Qty)
                    throw new InvalidOperationException(
                        $"药品「{item.DrugName}」库存不足：需要 {item.Qty}，可用 {totalAvailable}");

                var remainingQty = item.Qty;

                foreach (var stock in orderedStocks)
                {
                    if (remainingQty <= 0) break;

                    var deductQty = Math.Min(stock.QtyRemaining, remainingQty);

                    // 扣减库存批次
                    stock.QtyRemaining -= deductQty;
                    _stockRepo.Update(stock);

                    // 生成出库流水
                    await _drugOutRepo.AddAsync(new DrugOut
                    {
                        DrugId = item.DrugId,
                        BatchNo = stock.BatchNo,
                        Qty = deductQty,
                        PrescriptionId = prescriptionId,
                        OccurredAt = now,
                        OperatorId = operatorId,
                        IsReversal = false
                    }, ct);

                    remainingQty -= deductQty;
                }
            }
        }
        finally
        {
            _inventoryLock.Release();
        }
    }

    public async Task<bool> VoidPrescriptionAsync(
        long prescriptionId, string voidReason, CancellationToken ct = default)
    {
        // 权限检查：处方作废仅限 Doctor
        _permissionChecker.RequireCanPrescribe();

        if (string.IsNullOrWhiteSpace(voidReason))
            throw new ArgumentException("作废原因不能为空", nameof(voidReason));

        var prescription = await _prescriptionRepo.GetByIdAsync(prescriptionId, ct);
        if (prescription is null)
            return false;
        if (prescription.Status is not (
            PrescriptionStatus.Draft or PrescriptionStatus.Saved
            or PrescriptionStatus.Reviewed or PrescriptionStatus.Paid))
            throw new InvalidOperationException("当前处方状态不可作废");

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // 1. 修改处方状态为作废
            prescription.Status = PrescriptionStatus.Voided;
            prescription.VoidReason = voidReason.Trim();
            _prescriptionRepo.Update(prescription);

            // P1 H-07：作废操作使用当前操作人 ID（而非处方原医生 ID）
            var operatorId = _session.UserId ?? prescription.DoctorId;

            // 2. 回退库存：查找该处方的所有出库记录（非冲正），逐笔恢复
            await ReverseInventoryAsync(prescriptionId, operatorId, ct);

            // 3. 若处方已收费，生成退款冲正记录（IsReversal=true）
            await ReversePaymentsAsync(prescriptionId, operatorId, ct);

            // 3. 审计日志（事务内，与业务操作原子提交）
            await _auditService.LogAsync("PRESCRIPTION_VOID",
                $"Prescription:{prescriptionId}",
                $"Reason:{voidReason.Trim()}", ct);

            await _unitOfWork.CommitAsync(ct);
            return true;
        }
        catch
        {
            await _unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// 回退处方已扣减的库存。
    /// 查找该处方所有 IsReversal=false 的 DrugOut 记录，
    /// 逐笔恢复 DrugStock 库存，并生成 IsReversal=true 的冲正出库记录。
    /// </summary>
    private async Task ReverseInventoryAsync(
        long prescriptionId, long operatorId, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        // 获取该处方的所有出库记录（非冲正）
        var outRecords = await _drugOutRepo.FindAsync(
            o => o.PrescriptionId == prescriptionId && !o.IsReversal, ct);

        if (outRecords.Count == 0)
            return; // 处方未保存（无出库记录），无需回退

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

    /// <summary>
    /// 作废已收费处方时，为每笔非冲正收费记录生成退款冲正记录（IsReversal=true）。
    /// 冲正记录金额与原收款一致、收款方式一致，用于日报表和流水核算时抵减收入。
    /// </summary>
    private async Task ReversePaymentsAsync(
        long prescriptionId, long operatorId, CancellationToken ct)
    {
        var payments = await _paymentRepo.FindAsync(
            p => p.PrescriptionId == prescriptionId && !p.IsReversal, ct);

        if (payments.Count == 0)
            return; // 处方未收费，无需冲正

        var now = _clock.Now;
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
                Note = $"退款冲正：作废处方，原流水 {payment.Id}",
                IsReversal = true
            }, ct);
        }
    }

    /// <summary>
    /// 生成处方编号：{年份}-{5位序号}
    /// 序号 = 当年已有处方数 + 1
    /// P0 修复：使用互斥锁防止并发生成重复序号
    /// </summary>
    private async Task<string> GeneratePrescriptionNoAsync(int year, CancellationToken ct)
    {
        await _prescriptionNoLock.WaitAsync(ct);
        try
        {
            var allPrescriptions = await _prescriptionRepo.GetAllAsync(ct);
            var yearPrefix = $"{year}-";
            var count = allPrescriptions.Count(p => p.NoYearSeq.StartsWith(yearPrefix));
            var seq = count + 1;
            return $"{year}-{seq:D5}";
        }
        finally
        {
            _prescriptionNoLock.Release();
        }
    }

    /// <summary>
    /// 安全审计日志：审计失败不影响业务操作（best-effort）。
    /// 用于非事务操作（CreatePrescription、AddPrescriptionItem）。
    /// 事务操作中的审计日志直接调用 _auditService.LogAsync（失败则回滚）。
    /// </summary>
    private async Task SafeAuditAsync(string action, string target, string? payload, CancellationToken ct)
    {
        try
        {
            await _auditService.LogAsync(action, target, payload, ct);
        }
        catch
        {
            // 审计日志失败不影响业务操作
        }
    }

    /// <summary>
    /// 生成处方 PDF。加载处方完整数据（患者、医生、药品明细），调用 PDF 服务渲染。
    /// 生成后更新 Prescription.PdfPath 并保存。
    /// </summary>
    public async Task<string?> GeneratePrescriptionPdfAsync(
        long prescriptionId, string outputDir, CancellationToken ct = default)
    {
        // 权限检查：生成处方 PDF 仅限 Doctor
        _permissionChecker.RequireCanPrescribe();

        var prescription = await _prescriptionRepo.GetByIdAsync(prescriptionId, ct);
        if (prescription is null)
            return null;

        var patient = await _patientRepo.GetByIdAsync(prescription.PatientId, ct);
        var doctor = await _userRepo.GetByIdAsync(prescription.DoctorId, ct);
        var items = await _itemRepo.FindAsync(i => i.PrescriptionId == prescriptionId, ct);

        // 构建 PDF 数据
        var pdfItems = new List<PrescriptionItemPdfRow>();
        var seq = 1;
        foreach (var item in items.OrderBy(i => i.Id))
        {
            // 查找药品单位
            var drug = await _drugRepo.GetByIdAsync(item.DrugId, ct);
            var unit = drug?.Unit ?? item.DoseUnit;

            pdfItems.Add(new PrescriptionItemPdfRow(
                seq++,
                item.DrugName,
                item.Spec,
                $"{item.Dose}{item.DoseUnit}",
                item.Frequency,
                item.Route,
                item.DurationDays,
                item.Qty,
                unit,
                item.UnitPrice,
                item.Subtotal));
        }

        // 计算患者年龄
        int? age = null;
        if (patient?.Dob is not null)
        {
            var today = DateOnly.FromDateTime(_clock.Now);
            age = today.Year - patient.Dob.Value.Year;
            if (patient.Dob.Value > today.AddYears(-age.Value)) age--;
        }

        // 解密患者手机号
        var phone = patient is not null
            ? _encryption.Decrypt(patient.PhoneEncrypted)
            : "";

        var typeText = prescription.Type switch
        {
            PrescriptionType.Emergency => "急诊处方",
            PrescriptionType.Pediatric => "儿科处方",
            _ => "普通处方"
        };

        var data = new PrescriptionPdfData(
            prescription.NoYearSeq,
            prescription.CreatedAt,
            typeText,
            (int)prescription.Type,
            prescription.ExtendedReason,
            patient?.Name ?? "",
            patient?.Gender ?? "",
            age,
            phone,
            doctor?.DisplayName ?? "",
            "陈医生诊所",
            prescription.ChiefComplaint,
            prescription.DiagnosisText,
            pdfItems,
            prescription.TotalAmount,
            prescription.ExtendedReason is not null
                ? $"延长用药理由：{prescription.ExtendedReason}"
                : null);

        // 确保输出目录存在
        Directory.CreateDirectory(outputDir);

        // 生成 PDF（文件名 = 处方编号）
        var outputPath = Path.Combine(outputDir, $"{prescription.NoYearSeq}");
        var pdfPath = _pdfService.GeneratePrescriptionPdf(data, outputPath);

        // 更新 PdfPath
        prescription.PdfPath = pdfPath;
        _prescriptionRepo.Update(prescription);
        await _unitOfWork.SaveChangesAsync(ct);

        return pdfPath;
    }

    /// <summary>
    /// 获取处方完整信息（含明细列表），用于处方详情查看。
    /// </summary>
    public async Task<PrescriptionDto?> GetPrescriptionByIdAsync(
        long prescriptionId, CancellationToken ct = default)
    {
        // P1：权限检查——读取处方详情需 Doctor/Nurse/Readonly 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly);

        var prescription = await _prescriptionRepo.GetByIdAsync(prescriptionId, ct);
        if (prescription is null)
            return null;

        var patient = await _patientRepo.GetByIdAsync(prescription.PatientId, ct);
        var doctor = await _userRepo.GetByIdAsync(prescription.DoctorId, ct);
        var items = await _itemRepo.FindAsync(i => i.PrescriptionId == prescriptionId, ct);

        var itemDtos = items
            .OrderBy(i => i.Id)
            .Select(i => new PrescriptionItemDto
            {
                Id = i.Id,
                DrugId = i.DrugId,
                DrugName = i.DrugName,
                Spec = i.Spec,
                Dose = i.Dose,
                DoseUnit = i.DoseUnit,
                Frequency = i.Frequency,
                Route = i.Route,
                DurationDays = i.DurationDays,
                Qty = i.Qty,
                UnitPrice = i.UnitPrice,
                Subtotal = i.Subtotal
            })
            .ToList();

        return new PrescriptionDto(
            prescription.Id,
            prescription.NoYearSeq,
            prescription.PatientId,
            patient?.Name ?? $"[患者ID:{prescription.PatientId}]",
            prescription.DoctorId,
            doctor?.DisplayName ?? $"[医生ID:{prescription.DoctorId}]",
            prescription.ChiefComplaint,
            prescription.DiagnosisText,
            prescription.DiagnosisCode,
            prescription.TotalAmount,
            (int)prescription.Type,
            (int)prescription.Status,
            prescription.CreatedAt,
            itemDtos,
            // 本次就诊体征
            prescription.Weight,
            prescription.Temperature,
            prescription.SystolicBP,
            prescription.DiastolicBP,
            prescription.HeartRate);
    }

    /// <summary>
    /// 获取处方历史记录（可按患者姓名或处方编号筛选）。
    /// </summary>
    public async Task<IReadOnlyList<PrescriptionHistoryDto>> GetPrescriptionHistoryAsync(
        string? searchKeyword = null, DateTime? fromDate = null, DateTime? toDate = null,
        CancellationToken ct = default)
    {
        // P1：权限检查——读取处方历史需 Doctor/Nurse/Readonly 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly);

        var prescriptions = await _prescriptionRepo.GetAllAsync(ct);

        if (fromDate.HasValue)
            prescriptions = prescriptions.Where(p => p.CreatedAt >= fromDate.Value).ToList();

        if (toDate.HasValue)
            prescriptions = prescriptions.Where(p => p.CreatedAt < toDate.Value.AddDays(1)).ToList();

        if (prescriptions.Count == 0)
            return [];

        // 批量获取患者和医生信息
        var patientIds = prescriptions.Select(p => p.PatientId).Distinct().ToList();
        var patientNames = new Dictionary<long, string>();
        foreach (var patientId in patientIds)
        {
            var patient = await _patientRepo.GetByIdAsync(patientId, ct);
            patientNames[patientId] = patient?.Name ?? $"[患者ID:{patientId}]";
        }

        var doctorIds = prescriptions.Select(p => p.DoctorId).Distinct().ToList();
        var doctorNames = new Dictionary<long, string>();
        foreach (var doctorId in doctorIds)
        {
            var doctor = await _userRepo.GetByIdAsync(doctorId, ct);
            doctorNames[doctorId] = doctor?.DisplayName ?? $"[医生ID:{doctorId}]";
        }

        // 按搜索关键词筛选（患者姓名或处方编号）
        var result = new List<PrescriptionHistoryDto>();
        var hasKeyword = !string.IsNullOrWhiteSpace(searchKeyword);
        var keyword = hasKeyword ? searchKeyword!.Trim().ToLowerInvariant() : string.Empty;

        foreach (var prescription in prescriptions.OrderByDescending(p => p.CreatedAt))
        {
            var patientName = patientNames.GetValueOrDefault(
                prescription.PatientId, $"[患者ID:{prescription.PatientId}]");

            if (hasKeyword)
            {
                if (!prescription.NoYearSeq.ToLowerInvariant().Contains(keyword) &&
                    !patientName.ToLowerInvariant().Contains(keyword))
                    continue;
            }

            // 获取处方明细数
            var items = await _itemRepo.FindAsync(i => i.PrescriptionId == prescription.Id, ct);

            result.Add(new PrescriptionHistoryDto(
                prescription.Id,
                prescription.NoYearSeq,
                prescription.PatientId,
                patientName,
                doctorNames.GetValueOrDefault(prescription.DoctorId, $"[医生ID:{prescription.DoctorId}]"),
                prescription.DiagnosisText,
                prescription.TotalAmount,
                (int)prescription.Type,
                (int)prescription.Status,
                prescription.CreatedAt,
                items.Count));
        }

        return result;
    }
}
