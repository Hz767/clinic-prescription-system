// ============================================================================
// 诊所处方系统 · 全业务流程测试驱动（模拟人工操作序列）
// 说明：
//   - 不使用任何 SQL 直改数据，全部通过系统服务层（人工操作对应的业务入口）执行
//   - 使用独立测试库 flowtest.db，不影响诊所正式数据
//   - 覆盖 P0-P1 新功能：临床覆盖、药师角色/发药工作台、低库存补货线、
//     患者标签、同名提示、批量药单解析（LLM 关闭时跳过）、重复建档拦截提示
// 运行：dotnet run --project testdata/flowtest
// ============================================================================
using System.Text;
using Clinic.Application;
using Clinic.Application.Interfaces;
using Clinic.Domain.Entities;
using Clinic.Domain.Interfaces;
using Clinic.Infrastructure;
using Clinic.Infrastructure.Data;
using Clinic.Shared.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClinicFlowTest;

public static class Program
{
    private static readonly string TestDbPath = Path.Combine(
        AppContext.BaseDirectory, "flowtest.db");

    private static readonly List<(string Id, string Name, string Result, string Detail)> Report = new();
    private static int _stepNo;
    private static List<DrugMaster> _drugs = new();
    private static long _normalPatientId, _allergyPatientId, _htnPatientId;

    public static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("═══ 诊所处方系统 · 全业务流程测试 ═══\n");

        try
        {
            // 0. 准备独立测试环境
            if (File.Exists(TestDbPath))
                File.Delete(TestDbPath);

            var services = new ServiceCollection();
            services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
            services.AddInfrastructure(
                TestDbPath,
                "JQqOAFkXsovwPnF2co89Xh3of3UXBPJ8rQxNtP3wOoo=",
                "ClinicPrescriptionPepper2026-ChangeInProduction",
                llmEnabled: false);
            services.AddApplication();
            await using var sp = services.BuildServiceProvider();

            // 初始化数据库（与 App 启动流程一致：EnsureCreated + 手动迁移 + 种子）
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
                await db.Database.EnsureCreatedAsync();
                await MigrateVitalsAsync(db);
                await MigrateP0P1Async(db);
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                var enc = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
                await DbSeeder.SeedAsync(db, hasher, enc);
            }

            // ──────────────────────────────────────────────
            // 1. 登录流程
            // ──────────────────────────────────────────────
            await T("L-01", "错误密码登录被拒绝", async () =>
            {
                using var s = sp.CreateScope();
                var auth = s.ServiceProvider.GetRequiredService<IAuthService>();
                var ok = await auth.LoginAsync("admin", "wrongpass");
                Assert(!ok, "错误密码应登录失败");
            });

            await T("L-02", "医生(admin)正确登录", async () =>
            {
                using var s = sp.CreateScope();
                var auth = s.ServiceProvider.GetRequiredService<IAuthService>();
                var ok = await auth.LoginAsync("admin", "admin123");
                Assert(ok, "正确密码应登录成功");
                Assert(auth.CurrentUserName == "admin", $"会话用户名应为 admin，实际 {auth.CurrentUserName}");
            });

            // ──────────────────────────────────────────────
            // 2. 患者建档（P1-3 同名提示基础 + P1-1 标签）
            // ──────────────────────────────────────────────
            await T("P-01", "建档：普通患者（含标签）", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPatientService>();
                _normalPatientId = await svc.CreatePatientAsync(
                    "测试患者甲", "男", new DateOnly(1980, 5, 1), "13900001001",
                    allergies: null, history: "无", chronicTags: null,
                    weight: 70, temperature: 36.5m, systolicBP: 120, diastolicBP: 80, heartRate: 75,
                    tags: "慢病随访");
                Assert(_normalPatientId > 0, "应返回患者ID");
            });

            await T("P-02", "建档：青霉素过敏患者", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPatientService>();
                _allergyPatientId = await svc.CreatePatientAsync(
                    "测试患者乙", "女", new DateOnly(1990, 3, 15), "13900001002",
                    allergies: "青霉素类", history: "无", chronicTags: null);
                Assert(_allergyPatientId > 0, "应返回患者ID");
            });

            await T("P-03", "建档：高血压患者（禁忌症场景）", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPatientService>();
                _htnPatientId = await svc.CreatePatientAsync(
                    "测试患者丙", "男", new DateOnly(1960, 8, 20), "13900001003",
                    allergies: null, history: "高血压病史", chronicTags: "高血压",
                    weight: 75, temperature: 36.6m, systolicBP: 152, diastolicBP: 96, heartRate: 82,
                    tags: "高血压随访");
                Assert(_htnPatientId > 0, "应返回患者ID");
            });

            await T("P-04", "同名提示基础：按姓名搜索返回同名患者", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPatientService>();
                // 再建一个同名患者，模拟输入姓名时的同名查询（P1-3 UI 依赖此接口）
                var id = await svc.CreatePatientAsync(
                    "测试患者甲", "女", new DateOnly(1995, 1, 1), "13900001004",
                    allergies: null, history: null, chronicTags: null);
                var matches = await svc.SearchByNameAsync("测试患者甲");
                Assert(matches.Count >= 2, $"应查到至少2位同名患者，实际 {matches.Count}");
                Assert(matches.All(m => m.Name == "测试患者甲"), "返回结果应全部同名");
            });

            await T("P-05", "手机号精确查找", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPatientService>();
                var p = await svc.FindByPhoneAsync("13900001001");
                Assert(p is not null && p.Name == "测试患者甲", "应按手机号查到患者");
            });

            // ──────────────────────────────────────────────
            // 3. 药品目录 + 入库（P0-3 低库存补货线）
            // ──────────────────────────────────────────────
            await T("I-01", "确认/补齐药品目录（复用种子，补齐测试用药）", async () =>
            {
                using var s = sp.CreateScope();
                var repo = s.ServiceProvider.GetRequiredService<IRepository<DrugMaster>>();
                var uow = s.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var existing = (await repo.GetAllAsync()).ToList();
                bool Has(string cn) => existing.Any(d => d.GenericNameCn == cn);

                var toAdd = new[]
                {
                    // (cn, en, spec, unit, usage, isAb, level, contra, price, reorder)
                    ("对乙酰氨基酚片", "Paracetamol Tablets", "0.5g×20片", "盒", "口服 0.5g tid", false, AntibioticLevel.None, "", 5.00m, 30m),
                    ("复方伪麻黄碱缓释胶囊", "Compound Pseudoephedrine SR Capsules", "10粒", "盒", "口服 1粒 bid", false, AntibioticLevel.None, "高血压", 18.00m, 15m),
                    ("维生素C片", "Vitamin C Tablets", "0.1g×100片", "瓶", "口服 0.1g tid", false, AntibioticLevel.None, "", 3.00m, 20m),
                };
                foreach (var (cn, en, spec, unit, usage, ab, level, contra, price, reorder) in toAdd)
                {
                    if (Has(cn)) continue;
                    await repo.AddAsync(new DrugMaster
                    {
                        GenericNameCn = cn, GenericNameEn = en, Spec = spec, Unit = unit,
                        DefaultUsage = usage, IsAntibiotic = ab, AntibioticLevel = level,
                        IsToxicDrug = false, ContraindicationTags = string.IsNullOrEmpty(contra) ? null : contra,
                        CostPriceRef = price * 0.62m, RetailPriceRef = price, ReorderLevel = reorder
                    });
                }
                await uow.SaveChangesAsync();
                _drugs = (await repo.GetAllAsync()).ToList();
                Assert(_drugs.Count >= 6, $"应有至少6种药品（种子5+新增），实际 {_drugs.Count}");
                // 确认种子药品带补货线
                Assert(_drugs.First(d => d.GenericNameCn == "阿莫西林胶囊").ReorderLevel == 20m, "种子阿莫西林补货线应为20");
                Assert(_drugs.First(d => d.GenericNameCn == "布洛芬片").ReorderLevel == 30m, "种子布洛芬补货线应为30");
            });

            var amox = () => _drugs.First(d => d.GenericNameCn == "阿莫西林胶囊");
            var cefaclor = () => _drugs.First(d => d.GenericNameCn == "头孢克洛胶囊");
            var ibuprofen = () => _drugs.First(d => d.GenericNameCn == "布洛芬片");
            var paracetamol = () => _drugs.First(d => d.GenericNameCn == "对乙酰氨基酚片");
            var pseudo = () => _drugs.First(d => d.GenericNameCn == "复方伪麻黄碱缓释胶囊");
            var vc = () => _drugs.First(d => d.GenericNameCn == "维生素C片");

            await T("I-02", "入库：对乙酰氨基酚200盒/伪麻黄碱30盒/维生素C仅5盒(低于补货线)", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IInventoryService>();
                var op = s.ServiceProvider.GetRequiredService<IAuthService>().CurrentUserId!.Value;
                await svc.StockInAsync(paracetamol().Id, "B20260103", new DateOnly(2027, 12, 1), 200, 3.1m, "测试供应商", op);
                await svc.StockInAsync(pseudo().Id, "B20260104", new DateOnly(2028, 3, 1), 30, 11.2m, "测试供应商", op);
                await svc.StockInAsync(vc().Id, "B20260105", new DateOnly(2028, 6, 1), 5, 1.9m, "测试供应商", op); // 5 < 20 → 低库存
            });

            await T("I-03", "低库存标记（P0-3）：维生素C IsLowStock=true，对乙酰氨基酚/阿莫西林 IsLowStock=false", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IInventoryService>();
                var summary = await svc.GetAllStockSummaryAsync();
                var v = summary.First(x => x.DrugId == vc().Id);
                var p = summary.First(x => x.DrugId == paracetamol().Id);
                var am = summary.First(x => x.DrugId == amox().Id);
                Assert(v.IsLowStock, "维生素C库存5<补货线20 应标记低库存");
                Assert(v.ReorderLevel == 20m, $"维生素C补货线应为20，实际 {v.ReorderLevel}");
                Assert(!p.IsLowStock, "对乙酰氨基酚库存200≥30 不应标记低库存");
                Assert(p.ReorderLevel == 30m, $"对乙酰氨基酚补货线应为30，实际 {p.ReorderLevel}");
                Assert(!am.IsLowStock, "阿莫西林库存≥20 不应标记低库存");
            });

            // ──────────────────────────────────────────────
            // 4. 开方：正常流
            // ──────────────────────────────────────────────
            long rxNormal = 0, rxContra = 0, rxAllergy = 0, rxPaid = 0, rxPaid2 = 0, rxUnpaid = 0;
            decimal stockBeforeDispense = 0;

            await T("R-01", "开方：普通患者 + 对乙酰氨基酚（无阻断）", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var me = s.ServiceProvider.GetRequiredService<IAuthService>().CurrentUserId!.Value;
                rxNormal = await svc.CreatePrescriptionAsync(
                    _normalPatientId, me, "发热头痛2天", "急性上呼吸道感染", "J06.9", 0,
                    null, 70, 37.8m, 118, 78, 88);
                await svc.AddPrescriptionItemAsync(rxNormal, paracetamol().Id, 0.5m, "g", "tid", "口服", 3, 18);
                var blockers = await svc.GetPrescriptionBlockersAsync(rxNormal);
                Assert(blockers.Count == 0, $"不应有阻断项，实际：{string.Join(";", blockers)}");
                await svc.SavePrescriptionAsync(rxNormal);
                var rx = await svc.GetPrescriptionByIdAsync(rxNormal);
                Assert(rx!.Status == (int)PrescriptionStatus.Saved, "状态应为已保存");
                Assert(rx.TotalAmount == 90m, $"金额应为90元（5×18），实际 {rx.TotalAmount}");
            });

            // ──────────────────────────────────────────────
            // 5. 开方：禁忌症 → 临床覆盖（P0-1 核心）
            // ──────────────────────────────────────────────
            await T("R-02", "开方：高血压患者 + 伪麻黄碱（禁忌症阻断检出）", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var me = s.ServiceProvider.GetRequiredService<IAuthService>().CurrentUserId!.Value;
                rxContra = await svc.CreatePrescriptionAsync(
                    _htnPatientId, me, "鼻塞流涕3天", "急性鼻炎", "J31.0", 0,
                    null, 75, 36.6m, 152, 96, 82);
                await svc.AddPrescriptionItemAsync(rxContra, pseudo().Id, 1m, "粒", "bid", "口服", 5, 10);
                var blockers = await svc.GetPrescriptionBlockersAsync(rxContra);
                Assert(blockers.Count > 0 && blockers.Any(b => b.Contains("禁忌症")),
                    $"应检出禁忌症阻断，实际：{string.Join(";", blockers)}");
            });

            await T("R-03", "无覆盖理由直接保存 → 应被拒绝并列出阻断项", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var ex = await CaptureAsync(() => svc.SavePrescriptionAsync(rxContra));
                Assert(ex is InvalidOperationException && ex.Message.Contains("临床覆盖理由"),
                    $"应抛出要求覆盖理由的异常，实际：{ex?.Message}");
                Assert(ex!.Message.Contains("禁忌症"), "异常应包含禁忌症阻断项");
            });

            await T("R-04", "填写临床覆盖理由后保存成功 + 理由入库 + 审计", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var db = s.ServiceProvider.GetRequiredService<ClinicDbContext>();
                var ok = await svc.SavePrescriptionAsync(rxContra, "患者鼻塞明显影响睡眠，血压控制稳定，短期(5天)使用并监测血压");
                Assert(ok, "带覆盖理由应保存成功");
                var rx = await db.Prescriptions.FirstAsync(p => p.Id == rxContra);
                Assert(rx.Status == PrescriptionStatus.Saved, "状态应为已保存");
                Assert(!string.IsNullOrWhiteSpace(rx.OverrideReason), "OverrideReason 应写入");
                Assert(rx.OverrideReason!.Contains("血压"), "覆盖理由应保留原文");
                var audit = await db.AuditLogs.AnyAsync(a => a.Action == "PRESCRIPTION_SAVE_OVERRIDE" && a.Target.Contains(rxContra.ToString()));
                Assert(audit, "应存在 PRESCRIPTION_SAVE_OVERRIDE 审计记录");
            });

            // ──────────────────────────────────────────────
            // 6. 开方：过敏/交互覆盖链路（F-01/F-02 修复后验证）
            // ──────────────────────────────────────────────
            await T("R-05", "过敏患者开阿莫西林 → 类别标签拦截（F-02修复）+ 覆盖放行全链路（F-01修复）", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var db = s.ServiceProvider.GetRequiredService<ClinicDbContext>();
                var me = s.ServiceProvider.GetRequiredService<IAuthService>().CurrentUserId!.Value;
                rxAllergy = await svc.CreatePrescriptionAsync(
                    _allergyPatientId, me, "咳嗽黄痰3天", "急性支气管炎", "J18.9", 0,
                    null, 55, 37.2m, 110, 70, 80);
                // F-01 修复：添加药品阶段不再硬拦截（改为收集，与禁忌症一致）
                var ex = await CaptureAsync(() => svc.AddPrescriptionItemAsync(rxAllergy, amox().Id, 0.5m, "g", "tid", "口服", 5, 15));
                Assert(ex is null, $"添加药品不应被硬拦截（F-01 修复），实际：{ex?.Message}");
                // F-02 修复：保存前阻断检查应通过类别标签（ContraindicationTags=青霉素类）命中「青霉素类」过敏
                var blockers = await svc.GetPrescriptionBlockersAsync(rxAllergy);
                Assert(blockers.Count > 0 && blockers.Any(b => b.Contains("过敏")),
                    $"应通过类别标签检出过敏阻断（F-02 修复），实际：{string.Join(";", blockers)}");
                // 无覆盖理由保存 → 拒绝
                var ex2 = await CaptureAsync(() => svc.SavePrescriptionAsync(rxAllergy));
                Assert(ex2 is InvalidOperationException && ex2.Message.Contains("临床覆盖理由"),
                    $"无覆盖理由保存应被拒绝，实际：{ex2?.Message}");
                // 有覆盖理由 → 保存成功 + 理由入库 + 审计
                var ok = await svc.SavePrescriptionAsync(rxAllergy, "患者既往青霉素皮试阴性，本次需用阿莫西林控制感染，签署知情同意并密切观察");
                Assert(ok, "带覆盖理由应保存成功");
                var rxA = await db.Prescriptions.FirstAsync(p => p.Id == rxAllergy);
                Assert(!string.IsNullOrWhiteSpace(rxA.OverrideReason), "OverrideReason 应写入");
                var audit = await db.AuditLogs.AnyAsync(a => a.Action == "PRESCRIPTION_SAVE_OVERRIDE" && a.Target.Contains(rxAllergy.ToString()));
                Assert(audit, "应存在 PRESCRIPTION_SAVE_OVERRIDE 审计记录");
                // F-01 修复（审核阶段）：覆盖处方药师审核应可正常通过（不再被过敏复查硬拦截）
                var reviewOk = await svc.ReviewPrescriptionAsync(rxAllergy, "遵医嘱，同意用药");
                Assert(reviewOk, "覆盖处方应可通过药师审核");
            });

            await T("R-06", "阿莫西林+头孢克洛（Major交互）→ 添加不拦截，保存时覆盖放行（F-01修复）", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var db = s.ServiceProvider.GetRequiredService<ClinicDbContext>();
                var me = s.ServiceProvider.GetRequiredService<IAuthService>().CurrentUserId!.Value;
                var rx = await svc.CreatePrescriptionAsync(
                    _normalPatientId, me, "咳嗽5天", "急性支气管炎", "J18.9", 0, null);
                await svc.AddPrescriptionItemAsync(rx, amox().Id, 0.5m, "g", "tid", "口服", 5, 15);
                var ex = await CaptureAsync(() => svc.AddPrescriptionItemAsync(rx, cefaclor().Id, 0.25m, "g", "tid", "口服", 5, 15));
                Assert(ex is null, $"添加 Major 交互药品不应再被硬拦截（F-01 修复），实际：{ex?.Message}");
                var blockers = await svc.GetPrescriptionBlockersAsync(rx);
                Assert(blockers.Any(b => b.Contains("相互作用")),
                    $"保存前应检出 Major 交互阻断，实际：{string.Join(";", blockers)}");
                var ex2 = await CaptureAsync(() => svc.SavePrescriptionAsync(rx));
                Assert(ex2 is InvalidOperationException && ex2.Message.Contains("临床覆盖理由"),
                    $"无覆盖理由保存应被拒绝，实际：{ex2?.Message}");
                var ok = await svc.SavePrescriptionAsync(rx, "患者疑似混合感染，临床需阿莫西林联合头孢克洛，密切观察不良反应");
                Assert(ok, "带覆盖理由应保存成功");
                var r = await db.Prescriptions.FirstAsync(p => p.Id == rx);
                Assert(!string.IsNullOrWhiteSpace(r.OverrideReason), "OverrideReason 应写入");
            });

            await T("R-07", "布洛芬+阿司匹林（Major交互，反向匹配）→ 保存时阻断（F-01修复）", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var me = s.ServiceProvider.GetRequiredService<IAuthService>().CurrentUserId!.Value;
                var rx = await svc.CreatePrescriptionAsync(
                    _normalPatientId, me, "关节痛3天", "骨关节炎", "M25.5", 0, null);
                await svc.AddPrescriptionItemAsync(rx, ibuprofen().Id, 0.3m, "g", "bid", "口服", 7, 28);
                // 手工添加一个"阿司匹林肠溶片"到目录（交互表含阿司匹林）
                var repo = s.ServiceProvider.GetRequiredService<IRepository<DrugMaster>>();
                await repo.AddAsync(new DrugMaster
                {
                    GenericNameCn = "阿司匹林肠溶片", GenericNameEn = "Aspirin EC Tablets",
                    Spec = "100mg×30片", Unit = "盒", DefaultUsage = "口服 100mg qd",
                    IsAntibiotic = false, IsToxicDrug = false,
                    ContraindicationTags = "NSAIDs", CostPriceRef = 5m, RetailPriceRef = 8m
                });
                await s.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
                var aspirin = (await repo.GetAllAsync()).First(d => d.GenericNameCn == "阿司匹林肠溶片");
                var ex = await CaptureAsync(() => svc.AddPrescriptionItemAsync(rx, aspirin.Id, 0.1m, "g", "qd", "口服", 7, 7));
                Assert(ex is null, $"添加不应硬拦截（F-01 修复），实际：{ex?.Message}");
                var blockers = await svc.GetPrescriptionBlockersAsync(rx);
                Assert(blockers.Any(b => b.Contains("相互作用")),
                    $"应检出 NSAIDs 叠加 Major 交互阻断，实际：{string.Join(";", blockers)}");
            });

            // ──────────────────────────────────────────────
            // 7. 审核（药师角色 P0-2）
            // ──────────────────────────────────────────────
            await T("B-01", "未审核处方直接收费 → 被拒（处方管理办法）", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IBillingService>();
                var me = s.ServiceProvider.GetRequiredService<IAuthService>().CurrentUserId!.Value;
                var ex = await CaptureAsync(() => svc.RecordPaymentAsync(rxNormal, 0, 90m, me, null, null));
                Assert(ex is InvalidOperationException && ex.Message.Contains("药师审核"),
                    $"未审核收费应被拒，实际：{ex?.Message}");
            });

            await T("B-02", "药师登录", async () =>
            {
                using var s = sp.CreateScope();
                var auth = s.ServiceProvider.GetRequiredService<IAuthService>();
                var ok = await auth.LoginAsync("pharmacist", "admin123");
                Assert(ok, "药师应登录成功");
                var role = s.ServiceProvider.GetRequiredService<IPermissionChecker>().CurrentRole;
                Assert(role == UserRole.Pharmacist, $"角色应为药师，实际 {role}");
            });

            await T("B-03", "药师审核正常处方 → Reviewed", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var ok = await svc.ReviewPrescriptionAsync(rxNormal, "审核通过");
                Assert(ok, "审核应成功");
                var rx = await svc.GetPrescriptionByIdAsync(rxNormal);
                Assert(rx!.Status == (int)PrescriptionStatus.Reviewed, "状态应为已审核");
            });

            await T("B-04", "药师审核禁忌症覆盖处方 → Reviewed", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var ok = await svc.ReviewPrescriptionAsync(rxContra, "已核对覆盖理由，审核通过");
                Assert(ok, "审核应成功");
            });

            await T("B-05", "药师无权开方/收费（权限矩阵）", async () =>
            {
                using var s = sp.CreateScope();
                var ps = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var bs = s.ServiceProvider.GetRequiredService<IBillingService>();
                var me = s.ServiceProvider.GetRequiredService<IAuthService>().CurrentUserId!.Value;
                var ex1 = await CaptureAsync(() => ps.CreatePrescriptionAsync(_normalPatientId, me, "测试", "测试", null, 0, null));
                Assert(ex1 is UnauthorizedAccessException, $"药师开方应被拒，实际：{ex1?.GetType().Name}");
                var ex2 = await CaptureAsync(() => bs.RecordPaymentAsync(rxNormal, 0, 90m, me, null, null));
                Assert(ex2 is UnauthorizedAccessException, $"药师收费应被拒，实际：{ex2?.GetType().Name}");
            });

            // ──────────────────────────────────────────────
            // 8. 收费（医生）
            // ──────────────────────────────────────────────
            await T("B-06", "医生重新登录", async () =>
            {
                using var s = sp.CreateScope();
                var auth = s.ServiceProvider.GetRequiredService<IAuthService>();
                Assert(await auth.LoginAsync("admin", "admin123"), "医生应登录成功");
            });

            await T("B-07", "收费金额不一致 → 拒绝", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IBillingService>();
                var me = s.ServiceProvider.GetRequiredService<IAuthService>().CurrentUserId!.Value;
                var ex = await CaptureAsync(() => svc.RecordPaymentAsync(rxNormal, 0, 80m, me, null, null));
                Assert(ex is InvalidOperationException && ex.Message.Contains("不一致"),
                    $"金额不符应被拒，实际：{ex?.Message}");
            });

            await T("B-08", "正常收费（现金）→ Paid", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IBillingService>();
                var me = s.ServiceProvider.GetRequiredService<IAuthService>().CurrentUserId!.Value;
                var pid = await svc.RecordPaymentAsync(rxNormal, 0, 90m, me, null, "现金收费");
                Assert(pid > 0, "应返回收费流水ID");
                var rx = await s.ServiceProvider.GetRequiredService<IPrescriptionService>().GetPrescriptionByIdAsync(rxNormal);
                Assert(rx!.Status == (int)PrescriptionStatus.Paid, "状态应为已收费");
                rxPaid = rxNormal;
            });

            await T("B-09", "重复收费 → 被拒且提示语准确（F-03修复）", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IBillingService>();
                var me = s.ServiceProvider.GetRequiredService<IAuthService>().CurrentUserId!.Value;
                var ex = await CaptureAsync(() => svc.RecordPaymentAsync(rxNormal, 0, 90m, me, null, null));
                Assert(ex is not null, "重复收费应被拒绝");
                // F-03 修复：应提示"已收费"而非"仅已审核可收费"
                Assert(ex!.Message.Contains("已收费"),
                    $"提示语应明确为「已收费」（F-03 修复），实际：{ex.Message}");
            });

            // 第二条：禁忌症覆盖处方也走审核→收费→发药（完整链）
            await T("B-10", "覆盖处方收费（POS）→ Paid", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IBillingService>();
                var me = s.ServiceProvider.GetRequiredService<IAuthService>().CurrentUserId!.Value;
                var rx = await s.ServiceProvider.GetRequiredService<IPrescriptionService>().GetPrescriptionByIdAsync(rxContra);
                var pid = await svc.RecordPaymentAsync(rxContra, 1, rx!.TotalAmount, me, "POS0001", "POS收费");
                Assert(pid > 0, "应返回收费流水ID");
                rxPaid2 = rxContra;
            });

            await T("B-11", "构造未收费已审核处方（供发药拒绝用例）", async () =>
            {
                using var s = sp.CreateScope();
                var ps = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var me = s.ServiceProvider.GetRequiredService<IAuthService>().CurrentUserId!.Value;
                rxUnpaid = await ps.CreatePrescriptionAsync(_normalPatientId, me, "咳嗽3天", "急性支气管炎", "J18.9", 0, null);
                await ps.AddPrescriptionItemAsync(rxUnpaid, ibuprofen().Id, 0.3m, "g", "bid", "口服", 3, 12);
                await ps.SavePrescriptionAsync(rxUnpaid);
                await ps.ReviewPrescriptionAsync(rxUnpaid, "通过");
                var dto = await ps.GetPrescriptionByIdAsync(rxUnpaid);
                Assert(dto!.Status == (int)PrescriptionStatus.Reviewed, "应为已审核未收费");
            });

            // ──────────────────────────────────────────────
            // 9. 发药（P0-2 核心：发药时扣库存）
            // ──────────────────────────────────────────────
            await T("D-01", "药师登录执行发药", async () =>
            {
                using var s = sp.CreateScope();
                var auth = s.ServiceProvider.GetRequiredService<IAuthService>();
                Assert(await auth.LoginAsync("pharmacist", "admin123"), "药师应登录成功");
            });

            await T("D-02", "已审核未收费处方发药 → 拒绝", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var ex = await CaptureAsync(() => svc.DispensePrescriptionAsync(rxUnpaid));
                Assert(ex is InvalidOperationException && ex.Message.Contains("已收费"),
                    $"未收费处方发药应被拒，实际：{ex?.Message}");
            });

            await T("D-03", "正常发药（扣减库存）→ Dispensed", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var inv = s.ServiceProvider.GetRequiredService<IInventoryService>();
                var db = s.ServiceProvider.GetRequiredService<ClinicDbContext>();

                var before = await inv.GetStockQuantityAsync(paracetamol().Id);
                Assert(before == 200m, $"发药前对乙酰氨基酚库存应为200，实际 {before}");

                var ok = await svc.DispensePrescriptionAsync(rxPaid);
                Assert(ok, "发药应成功");

                var after = await inv.GetStockQuantityAsync(paracetamol().Id);
                Assert(after == 182m, $"发药后应扣减18盒（200→182），实际 {after}");

                var rx = await db.Prescriptions.FirstAsync(p => p.Id == rxPaid);
                Assert(rx.Status == PrescriptionStatus.Dispensed, "状态应为已发药");
                Assert(rx.DispensedAt is not null, "应记录发药时间");
                Assert(rx.DispensedBy is not null, "应记录发药人");
                var drugOuts = await db.DrugOuts.Where(o => o.PrescriptionId == rxPaid && !o.IsReversal).ToListAsync();
                Assert(drugOuts.Count == 1 && drugOuts[0].Qty == 18m, $"应有1条出库流水18盒，实际 {drugOuts.Count}/{drugOuts.FirstOrDefault()?.Qty}");
            });

            await T("D-04", "草稿处方发药 → 拒绝（状态机保护）", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var db = s.ServiceProvider.GetRequiredService<ClinicDbContext>();
                var draft = await db.Prescriptions.FirstAsync(p => p.Status == PrescriptionStatus.Draft);
                var ex = await CaptureAsync(() => svc.DispensePrescriptionAsync(draft.Id));
                Assert(ex is InvalidOperationException && ex.Message.Contains("已收费"),
                    $"草稿处方发药应被拒，实际：{ex?.Message}");
            });

            await T("D-05", "重复发药 → 拒绝（状态已非 Paid）", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var ex = await CaptureAsync(() => svc.DispensePrescriptionAsync(rxPaid));
                Assert(ex is InvalidOperationException && ex.Message.Contains("仅「已收费」"),
                    $"已发药处方重复发药应被拒，实际：{ex?.Message}");
            });

            await T("D-06", "覆盖处方发药（扣减伪麻黄碱10盒）→ Dispensed", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var inv = s.ServiceProvider.GetRequiredService<IInventoryService>();
                var db = s.ServiceProvider.GetRequiredService<ClinicDbContext>();

                var before = await inv.GetStockQuantityAsync(pseudo().Id);
                var ok = await svc.DispensePrescriptionAsync(rxPaid2);
                Assert(ok, "发药应成功");
                var after = await inv.GetStockQuantityAsync(pseudo().Id);
                Assert(before - after == 10m, $"应扣减10盒，实际扣 {before - after}");
                var rx = await db.Prescriptions.FirstAsync(p => p.Id == rxPaid2);
                Assert(rx.Status == PrescriptionStatus.Dispensed, "状态应为已发药");
            });

            // ──────────────────────────────────────────────
            // 10. 退费 / 作废（库存回退 + 冲正）
            // ──────────────────────────────────────────────
            await T("V-01", "医生登录", async () =>
            {
                using var s = sp.CreateScope();
                var auth = s.ServiceProvider.GetRequiredService<IAuthService>();
                Assert(await auth.LoginAsync("admin", "admin123"), "医生应登录成功");
            });

            await T("V-02", "已发药处方作废（含覆盖处方）→ 回退库存 + 退款冲正", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var inv = s.ServiceProvider.GetRequiredService<IInventoryService>();
                var db = s.ServiceProvider.GetRequiredService<ClinicDbContext>();

                var before = await inv.GetStockQuantityAsync(pseudo().Id);
                var ok = await svc.VoidPrescriptionAsync(rxPaid2, "患者退药，药品未开封");
                Assert(ok, "作废应成功");
                var after = await inv.GetStockQuantityAsync(pseudo().Id);
                Assert(after == before + 10m, $"作废应回退10盒（{before}→{after}）");

                var rx = await db.Prescriptions.FirstAsync(p => p.Id == rxPaid2);
                Assert(rx.Status == PrescriptionStatus.Voided, "状态应为已作废");
                var reversal = await db.DrugOuts.Where(o => o.PrescriptionId == rxPaid2 && o.IsReversal).ToListAsync();
                Assert(reversal.Count > 0, "应有冲正出库记录");
                var payRev = await db.PaymentLogs.Where(p => p.PrescriptionId == rxPaid2 && p.IsReversal).ToListAsync();
                Assert(payRev.Count > 0, "应有退款冲正支付记录");
            });

            await T("V-03", "草稿处方作废 → Voided", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var db = s.ServiceProvider.GetRequiredService<ClinicDbContext>();
                var draft = await db.Prescriptions.FirstAsync(p => p.Status == PrescriptionStatus.Draft);
                var ok = await svc.VoidPrescriptionAsync(draft.Id, "医生取消");
                Assert(ok, "草稿作废应成功");
                Assert((await db.Prescriptions.FirstAsync(p => p.Id == draft.Id)).Status == PrescriptionStatus.Voided, "状态应为已作废");
            });

            await T("V-04", "独立退费（Paid 处方，未发药）→ 冲正 + 状态 Voided", async () =>
            {
                using var s = sp.CreateScope();
                var bs = s.ServiceProvider.GetRequiredService<IBillingService>();
                var ps = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var inv = s.ServiceProvider.GetRequiredService<IInventoryService>();
                var db = s.ServiceProvider.GetRequiredService<ClinicDbContext>();
                var me = s.ServiceProvider.GetRequiredService<IAuthService>().CurrentUserId!.Value;

                // 构造一条 Paid 处方（收费后不立即发药 → 走独立退费）
                var rxId = await ps.CreatePrescriptionAsync(_normalPatientId, me, "头痛1天", "紧张性头痛", "R51", 0, null);
                await ps.AddPrescriptionItemAsync(rxId, paracetamol().Id, 0.5m, "g", "tid", "口服", 3, 18);
                await ps.SavePrescriptionAsync(rxId);
                await ps.ReviewPrescriptionAsync(rxId, "通过");
                var rxDto = await ps.GetPrescriptionByIdAsync(rxId);
                var payId = await bs.RecordPaymentAsync(rxId, 0, rxDto!.TotalAmount, me, null, null);
                Assert(payId > 0, "收费应成功");

                var stockBefore = await inv.GetStockQuantityAsync(paracetamol().Id);
                await bs.RefundAsync(rxId, me, "患者要求退费");
                var stockAfter = await inv.GetStockQuantityAsync(paracetamol().Id);
                Assert(stockAfter == stockBefore, $"未发药退费不应影响库存（{stockBefore}→{stockAfter}）");
                Assert((await db.Prescriptions.FirstAsync(p => p.Id == rxId)).Status == PrescriptionStatus.Voided, "状态应为已作废");
                var payRev = await db.PaymentLogs.Where(p => p.PrescriptionId == rxId && p.IsReversal).ToListAsync();
                Assert(payRev.Count == 1, "应有1条退款冲正记录");
            });

            // ──────────────────────────────────────────────
            // 11. 日结报表（冲正抵减验证）
            // ──────────────────────────────────────────────
            await T("M-01", "日结报表：今日收费含冲正抵减", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IBillingService>();
                var report = await svc.GetDailyReportAsync(DateTime.Now);
                Assert(report is not null, "应返回报表");
                Assert(report.PrescriptionCount >= 2, $"应至少2张处方收费，实际 {report.PrescriptionCount}");
                Assert(report.TotalAmount > 0m, $"今日净收入应>0，实际 {report.TotalAmount}");
                Console.WriteLine($"       → 日报：处方 {report.PrescriptionCount} 张，净额 ¥{report.TotalAmount:F2}（现金 ¥{report.CashAmount:F2} / POS ¥{report.PosAmount:F2}）");
            });

            await T("M-02", "收费流水含退费冲正标记", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IBillingService>();
                var history = await svc.GetPaymentHistoryAsync(DateTime.Now.AddDays(-1), DateTime.Now);
                Assert(history.Any(p => p.IsReversal), "流水应包含冲正记录");
            });

            // ──────────────────────────────────────────────
            // 12. AI 辅助预审 + 权限矩阵补充
            // ──────────────────────────────────────────────
            await T("M-03", "AI 辅助预审：覆盖处方应提示严重问题", async () =>
            {
                using var s = sp.CreateScope();
                var svc = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var reportText = await svc.GetAiReviewSuggestionsAsync(rxContra);
                Assert(reportText.Contains("禁忌症"), "预审报告应含禁忌症提示");
                Assert(reportText.Contains("不予审核通过"), "预审结论应建议不予通过");
            });

            await T("M-04", "只读角色：禁止一切修改操作", async () =>
            {
                using var s = sp.CreateScope();
                var auth = s.ServiceProvider.GetRequiredService<IAuthService>();
                Assert(await auth.LoginAsync("reader", "admin123"), "只读应登录成功");
                var ps = s.ServiceProvider.GetRequiredService<IPatientService>();
                var ex = await CaptureAsync(() => ps.CreatePatientAsync("只读测试", "男", null, "13900001999", null, null, null));
                Assert(ex is UnauthorizedAccessException, $"只读建档应被拒，实际：{ex?.GetType().Name}");
            });

            await T("M-05", "只读可查看（处方历史/患者列表）", async () =>
            {
                using var s = sp.CreateScope();
                var ps = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var pat = s.ServiceProvider.GetRequiredService<IPatientService>();
                var history = await ps.GetPrescriptionHistoryAsync();
                Assert(history.Count > 0, "只读应可查处方历史");
                var patients = await pat.GetAllPatientsAsync();
                Assert(patients.Count > 0, "只读应可查患者列表");
            });

            // ──────────────────────────────────────────────
            // 13. 审计日志完整性
            // ──────────────────────────────────────────────
            await T("M-06", "关键操作审计日志齐备", async () =>
            {
                using var s = sp.CreateScope();
                var db = s.ServiceProvider.GetRequiredService<ClinicDbContext>();
                var required = new[]
                {
                    "LOGIN_SUCCESS", "PATIENT_CREATE", "PRESCRIPTION_CREATE",
                    "PRESCRIPTION_SAVE", "PRESCRIPTION_SAVE_OVERRIDE",
                    "PRESCRIPTION_REVIEW", "PAYMENT_RECORD", "PRESCRIPTION_DISPENSE",
                    "PRESCRIPTION_VOID", "PAYMENT_REFUND"
                };
                var missing = required.Where(a => !db.AuditLogs.Any(x => x.Action == a)).ToList();
                Assert(missing.Count == 0, $"缺失审计动作：{string.Join(",", missing)}");
            });

            // ──────────────────────────────────────────────
            // 汇总
            // ──────────────────────────────────────────────
            Console.WriteLine("\n═══ 测试汇总 ═══");
            var passed = Report.Count(r => r.Result == "PASS");
            var failed = Report.Count(r => r.Result == "FAIL");
            Console.WriteLine($"通过：{passed} / 失败：{failed} / 发现：{Report.Count(r => r.Result == "FOUND")}");
            foreach (var r in Report)
                Console.WriteLine($"[{r.Result}] {r.Id} {r.Name}");
            foreach (var r in Report.Where(r => r.Result != "PASS"))
                Console.WriteLine($"\n{r.Id} 详情：{r.Detail}");

            return failed > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FATAL] 测试执行中断：{ex}");
            return 2;
        }
    }

    // ── 测试辅助 ──────────────────────────────────────────
    private static async Task T(string id, string name, Func<Task> action)
    {
        _stepNo++;
        try
        {
            await action();
            Report.Add((id, name, "PASS", ""));
            Console.WriteLine($"  ✓ [{id}] {name}");
        }
        catch (Exception ex)
        {
            var detail = ex;
            while (detail.InnerException is not null) detail = detail.InnerException;
            Report.Add((id, name, "FAIL", detail.Message));
            Console.WriteLine($"  ✗ [{id}] {name}\n      失败：{detail.Message}");
        }
    }

    private static void Found(string id, string detail)
    {
        Report.Add((id, "发现项", "FOUND", detail));
        Console.WriteLine($"  ◆ [{id}] 发现项：{detail}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static async Task MigrateVitalsAsync(ClinicDbContext db)
    {
        var cols = new (string Name, string Type)[]
        {
            ("Weight", "TEXT"), ("Temperature", "TEXT"),
            ("SystolicBP", "INTEGER"), ("DiastolicBP", "INTEGER"), ("HeartRate", "INTEGER")
        };
        foreach (var (name, type) in cols)
        {
            try { await db.Database.ExecuteSqlRawAsync($"ALTER TABLE Prescriptions ADD COLUMN {name} {type};"); }
            catch { }
        }
    }

    private static async Task MigrateP0P1Async(ClinicDbContext db)
    {
        var migrations = new (string Table, string Column, string Type)[]
        {
            ("DrugMasters", "ReorderLevel", "REAL"),
            ("Patients", "Tags", "TEXT"),
            ("Prescriptions", "OverrideReason", "TEXT"),
            ("Prescriptions", "DispensedAt", "TEXT"),
            ("Prescriptions", "DispensedBy", "INTEGER")
        };
        foreach (var (table, column, type) in migrations)
        {
            try { await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ADD COLUMN {column} {type};"); }
            catch { }
        }
    }
}
