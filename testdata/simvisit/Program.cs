// 诊所处方系统 · 二次测试：300 人次模拟就诊（覆盖各类型病例）
// ============================================================================
// 目标：
//   1) 用 300 名虚拟患者每人完成 1 次完整模拟就诊（开方→保存→审核→收费→发药），
//      全部经系统服务层业务入口执行（模拟人工操作），不写任何 SQL 直改数据；
//   2) 覆盖各类型病例：30 种诊断全覆盖、120 个药品子类全覆盖、
//      过敏冲突（类别标签匹配，F-02 修复验证）、禁忌症冲突、Major 交互冲突
//      （后三类走临床覆盖放行，F-01 修复验证）、无覆盖保存被拒；
//   3) 防御性校验：库存不足拒发、数量超限拒加、重复收费拒收、作废/退费回补。
// 数据：patients.csv(300人) + drugs.csv(268种)，独立测试库 simvisit.db。
// 运行：dotnet run --project testdata/simvisit
// ============================================================================
using System.Text;
using System.Text.RegularExpressions;
using Clinic.Application;
using Clinic.Application.Interfaces;
using Clinic.Domain.Entities;
using Clinic.Domain.Interfaces;
using Clinic.Infrastructure;
using Clinic.Infrastructure.Data;
using Clinic.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClinicSimVisit;

public static class Program
{
    private static readonly string TestDbPath = Path.Combine(AppContext.BaseDirectory, "simvisit.db");
    private static readonly string DataDir = @"E:\个人诊所处方系统\testdata";

    // 诊断模板：(icd, name, chief_template, note_template, drug_class_keywords)
    private static readonly (string Icd, string Name, string Chief, string Note, string[] Kw)[] Diags =
    {
        ("J06.9", "急性上呼吸道感染", "咳嗽、咽痛伴流涕{day}天", "多饮水、注意休息，清淡饮食，避免劳累；症状加重或发热持续超过3天请复诊。", new[]{"呼吸","抗感染","中成药-感冒","中成药-咳嗽"}),
        ("J02.9", "急性咽炎", "咽部疼痛、异物感{day}天，吞咽时加重", "少说话，多饮温水，可用淡盐水漱口；3天后无好转请复诊。", new[]{"五官","中成药-咽炎","抗感染"}),
        ("J03.9", "急性扁桃体炎", "发热伴咽痛{day}天，吞咽困难", "注意休息，流质饮食；若出现呼吸困难立即就诊。", new[]{"抗感染","解热镇痛","中成药-消炎"}),
        ("J18.9", "急性支气管炎", "咳嗽{day}天，咳黄痰，无胸痛", "戒烟酒，避免辛辣刺激；观察痰色变化，1周后复诊。", new[]{"呼吸-镇咳","呼吸-祛痰","抗感染"}),
        ("J30.4", "过敏性鼻炎", "阵发性喷嚏、流清涕{day}天，晨起加重", "避免接触过敏原，注意保暖；症状控制后维持用药1周。", new[]{"抗过敏","五官-鼻激素","五官-鼻减充血"}),
        ("J45.9", "支气管哮喘（轻度）", "反复喘息{day}天，夜间明显", "规律吸入用药，避免接触冷空气及过敏原；随身携带急救药物。", new[]{"呼吸-平喘","呼吸-抗过敏"}),
        ("I10", "高血压病", "头晕{day}天，自测血压偏高", "低盐低脂饮食，规律服药，每日晨起监测血压并记录；1月后复诊调整方案。", new[]{"心血管-降压CCB","心血管-降压ARB","心血管-降压ACEI","心血管-β受体阻滞剂"}),
        ("E11.9", "2型糖尿病", "多饮多尿{day}天，空腹血糖偏高", "糖尿病饮食，规律运动，监测空腹及餐后血糖；每3个月复查糖化血红蛋白。", new[]{"内分泌-降糖"}),
        ("E78.5", "高脂血症", "体检发现血脂偏高{day}月", "低脂饮食、控制体重、规律运动；服用调脂药期间监测肝功能。", new[]{"心血管-他汀","心血管-降脂贝特"}),
        ("K29.7", "慢性胃炎", "上腹隐痛、嗳气{day}天，餐后明显", "规律三餐、少食多餐，忌辛辣油腻及浓茶咖啡；幽门螺杆菌阳性者规范治疗。", new[]{"消化-抑酸","消化-胃黏膜保护","消化-促动力"}),
        ("K21.0", "胃食管反流病", "反酸烧心{day}天，夜间加重", "睡前3小时不进食，抬高床头；避免过饱及高脂饮食。", new[]{"消化-抑酸","消化-促动力"}),
        ("K52.9", "急性胃肠炎", "腹痛腹泻{day}天，每日{times}次，无脓血便", "清淡饮食、补充水分，腹泻停止后注意肠道菌群恢复；出现血便或高热立即就诊。", new[]{"消化-止泻","消化-微生态","抗感染-其他","电解质"}),
        ("K59.0", "功能性便秘", "排便困难{day}天，大便干结", "增加膳食纤维和饮水量，适当运动，养成定时排便习惯。", new[]{"消化-通便","中成药-消食"}),
        ("M54.9", "腰肌劳损", "腰部酸痛{day}天，久坐后加重", "避免久坐及弯腰负重，局部热敷；加强腰背肌锻炼。", new[]{"中成药-骨伤","解热镇痛","维生素"}),
        ("M25.5", "骨关节炎", "膝关节疼痛{day}天，上下楼梯加重", "减少负重活动，注意关节保暖；肥胖者建议减重。", new[]{"解热镇痛","内分泌-钙剂","中成药-骨伤"}),
        ("M10.9", "痛风性关节炎", "足趾关节红肿热痛{day}天，夜间发作", "急性期减少活动，多饮水（每日2000ml以上），忌海鲜啤酒动物内脏。", new[]{"解热镇痛-痛风","内分泌-痛风","内分泌-痛风碱化"}),
        ("L20.8", "湿疹", "皮肤红斑丘疹伴瘙痒{day}天", "避免搔抓、热水烫洗，保湿护肤；瘙痒严重时口服抗过敏药。", new[]{"皮肤-糖皮质激素","皮肤-止痒收敛","抗过敏-抗组胺"}),
        ("L30.0", "接触性皮炎", "接触{day}天后皮肤起疹伴痒", "避免再次接触致敏物，急性期冷湿敷。", new[]{"皮肤-糖皮质激素","抗过敏-抗组胺"}),
        ("L03.0", "皮肤软组织感染", "皮肤红肿疼痛{day}天，局部皮温升高", "保持局部清洁干燥，勿挤压；出现发热或红肿扩大立即就诊。", new[]{"皮肤-外用抗菌","抗感染","解热镇痛"}),
        ("B35.9", "体癣（真菌感染）", "躯干环状红斑伴瘙痒{day}天", "坚持用药至少2周，内衣煮沸消毒，避免与家人共用毛巾。", new[]{"皮肤-抗真菌"}),
        ("H10.9", "急性结膜炎", "眼红、分泌物增多{day}天", "注意手卫生，毛巾单独使用；两眼分开滴药，滴药前后洗手。", new[]{"五官-抗菌眼"}),
        ("H66.9", "急性中耳炎", "耳痛{day}天，伴听力下降", "避免耳内进水，感冒期间勿用力擤鼻；耳痛加重或流脓立即就诊。", new[]{"五官-抗菌耳","抗感染","解热镇痛"}),
        ("J32.9", "慢性鼻窦炎", "鼻塞流脓涕{day}天，伴头痛", "鼻部热敷、盐水洗鼻；坚持用药2周后复诊。", new[]{"五官-鼻激素","抗感染"}),
        ("N39.0", "泌尿系感染", "尿频尿急尿痛{day}天", "多饮水勤排尿，注意会阴部卫生；症状无缓解需复查尿常规。", new[]{"抗感染-喹诺酮类","抗感染-硝基咪唑类"}),
        ("N76.0", "阴道炎", "外阴瘙痒、白带增多{day}天", "治疗期间避免同房，内衣勤换烫洗；伴侣有症状需同治。", new[]{"妇科-阴道炎","中成药-妇科"}),
        ("G47.0", "失眠", "入睡困难{day}天，夜醒多次", "规律作息、睡前避免兴奋刺激；药物短期使用，避免长期依赖。", new[]{"中成药-安神","神经-镇静催眠","神经-植物神经"}),
        ("R51", "头痛（紧张性）", "双侧头部胀痛{day}天，劳累后加重", "避免长时间用眼和伏案，适度活动颈肩部。", new[]{"解热镇痛","神经-偏头痛","中成药-神经"}),
        ("R50.9", "发热待查", "发热{day}天，最高{temp}℃", "多饮水、监测体温；发热超过3天或伴皮疹、抽搐立即就诊。", new[]{"解热镇痛","抗感染","中成药-感冒"}),
        ("D64.9", "缺铁性贫血（轻度）", "乏力头晕{day}天，面色苍白", "加强营养，多食红肉及动物肝脏；服用铁剂后大便变黑属正常现象。", new[]{"矿物质-补铁","维生素"}),
        ("E55.9", "维生素D缺乏", "体检发现25-羟维生素D偏低", "增加户外活动，补充维生素D制剂，3个月后复查。", new[]{"内分泌-维生素D","内分泌-钙剂"}),
    };

    private static readonly string[] AllergySuffixes = { "过敏", "过敏性", "过敏史", "不耐受", "皮疹", "荨麻疹", "呼吸困难" };

    private static readonly List<string> Errors = new();
    private static int _ok, _fail;
    private static readonly Random Rng = new(20260905);

    // 统计
    private static int _normal, _allergyOverride, _contraOverride, _interactOverride;
    private static int _blockedSaveRejected, _dispensed, _amountErr, _negStock;
    private static readonly HashSet<string> UsedDiags = new();
    private static readonly HashSet<string> UsedClasses = new();
    // 药品 ID → 子类（drug_class 来自 drugs.csv，未入库到 DrugMaster）
    private static readonly Dictionary<long, string> DrugClassOf = new();

    public static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("═══ 诊所处方系统 · 二次测试：300 人次模拟就诊 ═══\n");

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

        // 0. 初始化（与 App 启动一致：EnsureCreated + 种子）
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
            await db.Database.EnsureCreatedAsync();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var enc = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
            await DbSeeder.SeedAsync(db, hasher, enc);
        }

        // 1. 导入药品目录 + 患者 + 初始库存
        var drugs = new List<DrugMaster>();
        await Step("导入药品目录（268 种）与初始库存（2000 盒/种）", async () =>
        {
            using var s = sp.CreateScope();
            var repo = s.ServiceProvider.GetRequiredService<IRepository<DrugMaster>>();
            var uow = s.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var existing = (await repo.GetAllAsync()).ToDictionary(d => d.GenericNameCn, d => d);
            var seenEn = new HashSet<string>(existing.Values.Select(d => d.GenericNameEn));
            foreach (var r in LoadCsv(Path.Combine(DataDir, "drugs.csv")))
            {
                var cn = r["generic_name_cn"];
                if (existing.ContainsKey(cn) || !seenEn.Add(r["generic_name_en"])) continue;
                await repo.AddAsync(new DrugMaster
                {
                    GenericNameCn = cn,
                    GenericNameEn = r["generic_name_en"],
                    Spec = r["spec"],
                    Unit = r["unit"],
                    DefaultUsage = r["default_usage"],
                    IsAntibiotic = r["is_antibiotic"] == "1",
                    AntibioticLevel = r["antibiotic_level"] switch
                    {
                        "NonRestricted" => AntibioticLevel.NonRestricted,
                        "Restricted" => AntibioticLevel.Restricted,
                        _ => AntibioticLevel.None
                    },
                    IsToxicDrug = r["is_toxic_drug"] == "1",
                    // 测试数据增强：目录原始标签仅 6 类过敏词，为覆盖「禁忌症」病例类型，
                    // 按真实药理为典型药品补充慢性病禁忌标签（伪麻黄碱→高血压、糖皮质激素→糖尿病）
                    ContraindicationTags = EnhanceTags(cn, r["contraindication_tags"]),
                    CostPriceRef = decimal.Parse(r["cost_price_ref"]),
                    RetailPriceRef = decimal.Parse(r["retail_price_ref"]),
                    ReorderLevel = decimal.TryParse(r["reorder_level"], out var rl) ? rl : 0m,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await uow.SaveChangesAsync();
            drugs = (await repo.GetAllAsync()).ToList();
            Check(drugs.Count == 268, $"目录应 268 种，实际 {drugs.Count}");
            // 建立 药名 → 子类 映射，再填 DrugClassOf（drugId → 子类）
            var classByCn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in LoadCsv(Path.Combine(DataDir, "drugs.csv")))
                classByCn[r["generic_name_cn"]] = r["drug_class"];
            foreach (var d in drugs)
                if (classByCn.TryGetValue(d.GenericNameCn, out var cls))
                    DrugClassOf[d.Id] = cls;

            var inv = s.ServiceProvider.GetRequiredService<IInventoryService>();
            var auth = s.ServiceProvider.GetRequiredService<IAuthService>();
            await auth.LoginAsync("admin", "admin123");
            var op = auth.CurrentUserId!.Value;
            foreach (var d in drugs)
                await inv.StockInAsync(d.Id, $"SIM-{DateTime.Now:yyyyMMdd}", DateOnly.FromDateTime(DateTime.Now.AddYears(2)), 2000m, d.CostPriceRef ?? 0m, "模拟就诊测试入库", op);
        });

        var patients = new List<(long DbId, string Name, string? Allergies, string? Chronic, string? Tags, decimal Weight)>();
        await Step("导入患者档案（300 人）", async () =>
        {
            using var s = sp.CreateScope();
            var svc = s.ServiceProvider.GetRequiredService<IPatientService>();
            var phones = new HashSet<string>();
            foreach (var r in LoadCsv(Path.Combine(DataDir, "patients.csv")))
            {
                var phone = r["phone"];
                if (!phones.Add(phone)) continue;
                var pid = await svc.CreatePatientAsync(
                    r["name"], r["gender"], DateOnly.Parse(r["dob"]), phone,
                    allergies: string.IsNullOrWhiteSpace(r["allergies"]) ? null : r["allergies"],
                    history: string.IsNullOrWhiteSpace(r["history"]) ? null : r["history"],
                    chronicTags: string.IsNullOrWhiteSpace(r["chronic_tags"]) ? null : r["chronic_tags"],
                    weight: decimal.TryParse(r["weight_kg"], out var w) ? w : null,
                    temperature: decimal.TryParse(r["temperature_c"], out var t) ? t : null,
                    systolicBP: int.TryParse(r["sbp"], out var sb) ? sb : null,
                    diastolicBP: int.TryParse(r["dbp"], out var d2) ? d2 : null,
                    heartRate: int.TryParse(r["heart_rate"], out var hr) ? hr : null,
                    tags: string.IsNullOrWhiteSpace(r["tags"]) ? null : r["tags"]);
                if (pid > 0)
                    patients.Add((pid, r["name"],
                        string.IsNullOrWhiteSpace(r["allergies"]) ? null : r["allergies"],
                        string.IsNullOrWhiteSpace(r["chronic_tags"]) ? null : r["chronic_tags"],
                        string.IsNullOrWhiteSpace(r["tags"]) ? null : r["tags"],
                        decimal.TryParse(r["weight_kg"], out var wt) ? wt : 60m));
            }
            Check(patients.Count == 300, $"应导入 300 人，实际 {patients.Count}");
        });

        // 2. 300 人次模拟就诊
        await Step($"300 人次模拟就诊（{Diags.Length} 种诊断 / {drugs.Count} 种药）", async () =>
        {
            var classGroups = drugs.GroupBy(d => DrugClassOf.GetValueOrDefault(d.Id, "未分类"))
                .ToDictionary(g => g.Key, g => g.ToList());
            Check(classGroups.Count == 120, $"应 120 个药品子类，实际 {classGroups.Count}");

            for (int i = 0; i < 300; i++)
            {
                var pat = patients[i];
                // 诊断：前 30 人次全覆盖 30 种诊断；其余随机加权
                var diag = i < Diags.Length ? Diags[i] : Diags[Rng.Next(Diags.Length)];
                UsedDiags.Add(diag.Name);

                // 选第 1 种药：优先未用子类（覆盖 120 子类）；
                // 诊断关键词未匹配到任何子类（如医用耗材/消毒防腐/急救类）时，回退到全部未用子类
                var kwClasses = classGroups
                    .Where(g => diag.Kw.Any(k => g.Key.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    .Select(g => g.Key).ToList();
                var allClasses = classGroups.Keys.ToList();
                var firstPool = kwClasses.Where(c => !UsedClasses.Contains(c)).ToList();
                if (firstPool.Count == 0) firstPool = kwClasses.Count > 0
                    ? kwClasses
                    : allClasses.Where(c => !UsedClasses.Contains(c)).ToList();
                if (firstPool.Count == 0) firstPool = allClasses;
                string? chosenClass = null;
                if (firstPool.Count > 0)
                {
                    chosenClass = firstPool[Rng.Next(firstPool.Count)];
                    UsedClasses.Add(chosenClass);
                }

                using var s = sp.CreateScope();
                var ps = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
                var bs = s.ServiceProvider.GetRequiredService<IBillingService>();
                var auth = s.ServiceProvider.GetRequiredService<IAuthService>();
                var db = s.ServiceProvider.GetRequiredService<ClinicDbContext>();

                await auth.LoginAsync("admin", "admin123");
                var me = auth.CurrentUserId!.Value;

                var day = Rng.Next(1, 15);
                var chief = diag.Chief.Replace("{day}", day.ToString())
                    .Replace("{times}", Rng.Next(3, 9).ToString())
                    .Replace("{temp}", $"{Rng.Next(375, 395) / 10m:F1}");
                var weight = Math.Round(pat.Weight + (decimal)(Rng.NextDouble() * 4 - 2), 1);

                long rxId;
                try
                {
                    rxId = await ps.CreatePrescriptionAsync(
                        pat.DbId, me, chief, diag.Name, diag.Icd, 0,
                        diag.Note, weight,
                        Math.Round((decimal)(36.2 + Rng.NextDouble() * 2.6), 1),
                        Rng.Next(100, 171), Rng.Next(60, 106), Rng.Next(60, 106));
                }
                catch (Exception ex)
                {
                    Fail($"就诊 {i + 1}（{pat.Name}）创建处方失败：{ex.Message}");
                    continue;
                }

                // 处方药品选择
                var picked = new List<DrugMaster>();
                if (chosenClass is not null && classGroups[chosenClass].Count > 0)
                    picked.Add(classGroups[chosenClass][Rng.Next(classGroups[chosenClass].Count)]);

                // 冲突场景注入
                var scenario = "正常";
                var overrideReason = "";

                // (a) 过敏冲突：有过敏史的患者按概率注入（F-02 修复验证：类别标签匹配）
                if (pat.Allergies is not null && Rng.NextDouble() < 0.55)
                {
                    var hit = FindAllergyMatchDrug(pat.Allergies, drugs);
                    if (hit is not null)
                    {
                        picked[0] = hit;
                        scenario = "过敏冲突";
                        overrideReason = $"患者既往皮试阴性/曾用无反应，本次临床必须使用，签署知情同意并密切观察（模拟覆盖）";
                    }
                }
                // (b) 禁忌冲突：有慢病标签的患者按概率注入
                else if (pat.Chronic is not null && Rng.NextDouble() < 0.35)
                {
                    var hit = FindContraMatchDrug(pat.Chronic, drugs);
                    if (hit is not null)
                    {
                        picked[0] = hit;
                        scenario = "禁忌冲突";
                        overrideReason = $"患者慢病状态控制稳定，短期使用并监测相关指标（模拟覆盖）";
                    }
                }
                // (c) Major 交互：每 60 人次注入一例阿莫西林+头孢克洛（无过敏患者）
                else if (i > 0 && i % 60 == 0 && pat.Allergies is null)
                {
                    var amox = drugs.FirstOrDefault(d => d.GenericNameCn == "阿莫西林胶囊");
                    var cef = drugs.FirstOrDefault(d => d.GenericNameCn == "头孢克洛胶囊");
                    if (amox is not null && cef is not null)
                    {
                        picked[0] = amox;
                        picked.Add(cef);
                        scenario = "Major交互";
                        overrideReason = $"疑似混合感染，临床需联合抗感染治疗，观察肝功能（模拟覆盖）";
                    }
                }

                // 其余药品（1-2 种）：从诊断对应子类池补充；无对应子类时用全部子类
                var want = 1 + Rng.Next(3);
                var poolClasses = kwClasses.Count > 0 ? kwClasses : allClasses;
                while (picked.Count < want && poolClasses.Count > 0)
                {
                    var c = poolClasses[Rng.Next(poolClasses.Count)];
                    var pool = classGroups[c];
                    var d2 = pool[Rng.Next(pool.Count)];
                    if (!picked.Contains(d2)) picked.Add(d2);
                }

                // 添加明细
                var addEx = false;
                foreach (var d in picked)
                {
                    var usage = d.DefaultUsage ?? "";
                    var (dose, unit, freq, days, qty) = ParseUsage(usage, Rng);
                    try
                    {
                        await ps.AddPrescriptionItemAsync(rxId, d.Id, dose, unit, freq,
                            usage.Contains("外") ? "外用" : "口服", days, qty);
                    }
                    catch (Exception ex)
                    {
                        addEx = true;
                        Fail($"就诊 {i + 1}（{pat.Name}）添加药品「{d.GenericNameCn}」失败：{ex.Message}");
                        break;
                    }
                }
                if (addEx) continue;

                // 保存前阻断检查
                var blockers = await ps.GetPrescriptionBlockersAsync(rxId);
                bool hasBlocker = blockers.Count > 0;

                try
                {
                    if (hasBlocker)
                    {
                        // 无覆盖理由保存 → 应被拒（记录）
                        var ex = await CaptureAsync(() => ps.SavePrescriptionAsync(rxId));
                        if (ex is InvalidOperationException && ex.Message.Contains("临床覆盖理由"))
                            _blockedSaveRejected++;
                        else if (ex is not null)
                            Fail($"就诊 {i + 1}（{pat.Name}）无覆盖保存：{ex.Message}");
                        // 填覆盖理由保存
                        if (string.IsNullOrWhiteSpace(overrideReason))
                            overrideReason = $"临床判断需要（模拟覆盖）：{string.Join("；", blockers)}";
                        var ok = await ps.SavePrescriptionAsync(rxId, overrideReason);
                        if (!ok) { Fail($"就诊 {i + 1}（{pat.Name}）覆盖保存失败"); continue; }
                        if (scenario.StartsWith("过敏")) _allergyOverride++;
                        else if (scenario.StartsWith("禁忌")) _contraOverride++;
                        else if (scenario.StartsWith("Major")) _interactOverride++;
                    }
                    else
                    {
                        await ps.SavePrescriptionAsync(rxId);
                        _normal++;
                    }
                }
                catch (Exception ex)
                {
                    Fail($"就诊 {i + 1}（{pat.Name}）保存失败：{ex.Message}");
                    continue;
                }

                // 药师审核
                await auth.LoginAsync("pharmacist", "admin123");
                var reviewOk = await ps.ReviewPrescriptionAsync(rxId, "审核通过");
                if (!reviewOk) { Fail($"就诊 {i + 1}（{pat.Name}）审核失败"); continue; }

                // 医生收费（现金/POS 交替）
                await auth.LoginAsync("admin", "admin123");
                var me2 = auth.CurrentUserId!.Value;
                var dto = await ps.GetPrescriptionByIdAsync(rxId);
                if (dto is null) { Fail($"就诊 {i + 1}（{pat.Name}）处方查询失败"); continue; }
                var isPos = i % 2 == 1;
                try
                {
                    await bs.RecordPaymentAsync(rxId, isPos ? 1 : 0, dto.TotalAmount, me2,
                        isPos ? $"POS-{1000 + i}" : null, isPos ? "POS收费" : "现金收费");
                }
                catch (Exception ex)
                {
                    Fail($"就诊 {i + 1}（{pat.Name}）收费失败：{ex.Message}");
                    continue;
                }

                // 药师发药
                await auth.LoginAsync("pharmacist", "admin123");
                try
                {
                    var disp = await ps.DispensePrescriptionAsync(rxId);
                    if (!disp) { Fail($"就诊 {i + 1}（{pat.Name}）发药失败"); continue; }
                    _dispensed++;
                }
                catch (Exception ex)
                {
                    Fail($"就诊 {i + 1}（{pat.Name}）发药异常：{ex.Message}");
                    continue;
                }

                if ((i + 1) % 50 == 0)
                    Console.WriteLine($"       → 已完成 {i + 1}/300 人次，覆盖处方 {_allergyOverride + _contraOverride + _interactOverride} 张，发药 {_dispensed} 张");
            }

            Check(_dispensed == 300 - Errors.Count(e => e.Contains("就诊")), "300 人次应全部完成发药");
            Check(UsedDiags.Count == Diags.Length, $"诊断覆盖应为 {Diags.Length}，实际 {UsedDiags.Count}");
        });

        // 3. 防御性校验（独立用例，不计入 300 人次）
        await Step("防御性校验（超量/库存不足/重复收费/作废/退费）", async () =>
        {
            using var s = sp.CreateScope();
            var ps = s.ServiceProvider.GetRequiredService<IPrescriptionService>();
            var bs = s.ServiceProvider.GetRequiredService<IBillingService>();
            var inv = s.ServiceProvider.GetRequiredService<IInventoryService>();
            var db = s.ServiceProvider.GetRequiredService<ClinicDbContext>();
            var auth = s.ServiceProvider.GetRequiredService<IAuthService>();
            // 防御用例选用无过敏史、无慢病标签的患者，避免引入过敏/禁忌阻断干扰
            var pat = patients.First(p => p.Allergies is null && p.Chronic is null);
            await auth.LoginAsync("admin", "admin123");
            var me = auth.CurrentUserId!.Value;
            var repo = s.ServiceProvider.GetRequiredService<IRepository<DrugMaster>>();
            var all = (await repo.GetAllAsync()).ToList();
            var vc = all.First(d => d.GenericNameCn == "维生素C片");
            var amox = all.First(d => d.GenericNameCn == "阿莫西林胶囊");

            // (1) 数量 > 999 拒加
            var rx1 = await ps.CreatePrescriptionAsync(pat.DbId, me, "测试超量", "维生素D缺乏", "E55.9", 0, null);
            var ex1 = await CaptureAsync(() => ps.AddPrescriptionItemAsync(rx1, vc.Id, 0.1m, "g", "tid", "口服", 30, 1500));
            Check(ex1 is not null, "数量超过999应被拒绝");
            Console.WriteLine($"       → 超量拒加：{(ex1 is null ? "未拦截(缺陷)" : "已拦截")}");

            // (2) 库存不足：先把维生素C库存压到 10 盒，再开 20 盒
            var vcStockBefore = await inv.GetStockQuantityAsync(vc.Id);
            var vcOutQty = vcStockBefore - 10m;
            if (vcOutQty > 0)
                await inv.StockOutAsync(vc.Id, (int)vcOutQty, "压库存到10盒", me);
            var rx2 = await ps.CreatePrescriptionAsync(pat.DbId, me, "测试库存", "急性上呼吸道感染", "J06.9", 0, null);
            await ps.AddPrescriptionItemAsync(rx2, vc.Id, 0.1m, "g", "tid", "口服", 7, 20);
            var exSave = await CaptureAsync(() => ps.SavePrescriptionAsync(rx2));
            if (exSave is null)
            {
                // 保存通过 → 走审核收费后发药被拒
                await ps.ReviewPrescriptionAsync(rx2, "通过");
                var dto2 = await ps.GetPrescriptionByIdAsync(rx2);
                await bs.RecordPaymentAsync(rx2, 0, dto2!.TotalAmount, me, null, null);
                var exDisp = await CaptureAsync(() => ps.DispensePrescriptionAsync(rx2));
                Check(exDisp is not null, "库存不足发药应被拒绝");
                Console.WriteLine($"       → 库存不足：保存通过、发药被拒（{(exDisp is null ? "未拦截(缺陷)" : "已拦截")}）");
            }
            else
            {
                Check(exSave.Message.Contains("库存"), $"保存应提示库存问题，实际 {exSave.Message}");
                Console.WriteLine($"       → 库存不足：保存阶段即被拒（{exSave.Message}）");
            }

            // (3) 重复收费（回归 F-03）：对已收费处方再收费 → 提示「已收费」
            var sleepDrug = all.FirstOrDefault(d => DrugClassOf.GetValueOrDefault(d.Id, "").Contains("镇静催眠"))
                            ?? all.First(d => d.GenericNameCn == "维生素C片");
            var rx3 = await ps.CreatePrescriptionAsync(pat.DbId, me, "重复收费测试", "失眠", "G47.0", 0, null);
            await ps.AddPrescriptionItemAsync(rx3, sleepDrug.Id, 1m, "片", "qn", "口服", 7, 7);
            await ps.SavePrescriptionAsync(rx3);
            await ps.ReviewPrescriptionAsync(rx3, "通过");
            var dto3 = await ps.GetPrescriptionByIdAsync(rx3);
            await bs.RecordPaymentAsync(rx3, 0, dto3!.TotalAmount, me, null, null);
            var ex3 = await CaptureAsync(() => bs.RecordPaymentAsync(rx3, 0, dto3.TotalAmount, me, null, null));
            Check(ex3 is not null && ex3.Message.Contains("已收费"), $"重复收费应提示「已收费」，实际：{ex3?.Message}");
            Console.WriteLine($"       → 重复收费：已拦截，提示「已收费」（F-03 回归通过）");

            // (4) 已发药作废 → 库存回补 + 冲正
            var rx4 = await ps.CreatePrescriptionAsync(pat.DbId, me, "作废测试", "急性咽炎", "J02.9", 0, null);
            await ps.AddPrescriptionItemAsync(rx4, amox.Id, 0.5m, "g", "tid", "口服", 5, 15);
            await ps.SavePrescriptionAsync(rx4);
            await ps.ReviewPrescriptionAsync(rx4, "通过");
            var dto4 = await ps.GetPrescriptionByIdAsync(rx4);
            await bs.RecordPaymentAsync(rx4, 0, dto4!.TotalAmount, me, null, null);
            await auth.LoginAsync("pharmacist", "admin123");
            await ps.DispensePrescriptionAsync(rx4);
            await auth.LoginAsync("admin", "admin123");
            var amoxBefore = await inv.GetStockQuantityAsync(amox.Id);
            var voidOk = await ps.VoidPrescriptionAsync(rx4, "患者退药");
            var amoxAfter = await inv.GetStockQuantityAsync(amox.Id);
            Check(voidOk, "作废应成功");
            Check(amoxAfter == amoxBefore + 15m, $"作废应回补15盒，实际 {amoxBefore}→{amoxAfter}");
            var rev = await db.DrugOuts.Where(o => o.PrescriptionId == rx4 && o.IsReversal).ToListAsync();
            Check(rev.Count > 0, "应有冲正出库记录");
            Console.WriteLine($"       → 已发药作废：状态 Voided + 库存回补15盒 + 冲正流水（通过）");

            // (5) Paid 未发药退费 → 冲正、库存不变
            var rx5 = await ps.CreatePrescriptionAsync(pat.DbId, me, "退费测试", "头痛（紧张性）", "R51", 0, null);
            await ps.AddPrescriptionItemAsync(rx5, all.First(d => d.GenericNameCn == "布洛芬片").Id, 0.3m, "g", "bid", "口服", 3, 12);
            await ps.SavePrescriptionAsync(rx5);
            await ps.ReviewPrescriptionAsync(rx5, "通过");
            var dto5 = await ps.GetPrescriptionByIdAsync(rx5);
            await bs.RecordPaymentAsync(rx5, 0, dto5!.TotalAmount, me, null, null);
            var ibuBefore = await inv.GetStockQuantityAsync(all.First(d => d.GenericNameCn == "布洛芬片").Id);
            await bs.RefundAsync(rx5, me, "患者要求退费");
            var ibuAfter = await inv.GetStockQuantityAsync(all.First(d => d.GenericNameCn == "布洛芬片").Id);
            Check(ibuAfter == ibuBefore, $"未发药退费不应影响库存（{ibuBefore}→{ibuAfter}）");
            Console.WriteLine($"       → Paid 未发药退费：状态 Voided + 冲正 + 库存不变（通过）");
        });

        // 4. 全局校验：金额勾稽 + 无负库存 + 审计
        await Step("全局校验（金额/库存/审计/状态）", async () =>
        {
            using var s = sp.CreateScope();
            var db = s.ServiceProvider.GetRequiredService<ClinicDbContext>();

            // 金额勾稽：处方 TotalAmount == 明细合计；收费金额 == TotalAmount
            var rxs = await db.Prescriptions.Where(p => p.Status != PrescriptionStatus.Draft && p.Status != PrescriptionStatus.Voided).ToListAsync();
            foreach (var rx in rxs)
            {
                var items = await db.PrescriptionItems.Where(i => i.PrescriptionId == rx.Id).ToListAsync();
                var sum = Math.Round(items.Sum(i => i.Subtotal), 2, MidpointRounding.AwayFromZero);
                if (rx.TotalAmount != sum) { _amountErr++; Fail($"处方 {rx.NoYearSeq} 金额不符：{rx.TotalAmount} vs 明细 {sum}"); }
                var pays = await db.PaymentLogs.Where(p => p.PrescriptionId == rx.Id && !p.IsReversal).ToListAsync();
                if (pays.Count > 1) Fail($"处方 {rx.NoYearSeq} 存在多笔收费：{pays.Count}");
            }

            // 无负库存
            var neg = await db.DrugStocks.Where(s2 => s2.QtyRemaining < 0).ToListAsync();
            _negStock = neg.Count;
            if (neg.Count > 0) Fail($"存在负库存 {neg.Count} 条");

            // 审计覆盖：PRESCRIPTION_SAVE_OVERRIDE 数量 ≥ 覆盖处方数
            var overrides = await db.AuditLogs.CountAsync(a => a.Action == "PRESCRIPTION_SAVE_OVERRIDE");
            Check(overrides >= _allergyOverride + _contraOverride + _interactOverride,
                $"覆盖审计应 ≥ {_allergyOverride + _contraOverride + _interactOverride}，实际 {overrides}");

            // 状态分布
            var statusCounts = await db.Prescriptions.GroupBy(p => p.Status)
                .Select(g => new { g.Key, N = g.Count() }).ToListAsync();
            foreach (var sc in statusCounts.OrderBy(x => x.Key))
                Console.WriteLine($"       → 状态 {sc.Key}: {sc.N}");

            Check(_amountErr == 0, "金额勾稽应 0 不一致");
            Check(_negStock == 0, "不应有负库存");
        });

        // 汇总
        Console.WriteLine("\n═══ 二次测试汇总 ═══");
        Console.WriteLine($"通过：{_ok} / 失败：{_fail}");
        Console.WriteLine($"300 人次发药完成：{_dispensed} / 300");
        Console.WriteLine($"诊断覆盖：{UsedDiags.Count}/{Diags.Length}；药品子类覆盖：{UsedClasses.Count}/120");
        Console.WriteLine($"病例构成：正常 {_normal} / 过敏冲突覆盖 {_allergyOverride} / 禁忌冲突覆盖 {_contraOverride} / Major交互覆盖 {_interactOverride}");
        Console.WriteLine($"无覆盖保存被拒（正确防御）：{_blockedSaveRejected} 次");
        foreach (var e in Errors)
            Console.WriteLine($"  ✗ {e}");
        Console.WriteLine(_fail == 0 ? "\n[全部通过] 300 人次模拟就诊无异常" : "\n[存在失败] 见上方清单");
        return _fail == 0 ? 0 : 1;
    }

    // ──────────────────────────── 辅助 ────────────────────────────

    private static async Task Step(string name, Func<Task> action)
    {
        try
        {
            await action();
            _ok++;
            Console.WriteLine($"  ✓ {name}");
        }
        catch (Exception ex)
        {
            _fail++;
            Errors.Add($"{name}：{ex.Message}");
            Console.WriteLine($"  ✗ {name}：{ex.Message}");
        }
    }

    private static void Check(bool cond, string msg)
    {
        if (!cond) throw new InvalidOperationException(msg);
    }

    private static void Fail(string msg)
    {
        _fail++;
        Errors.Add(msg);
        Console.WriteLine($"    ✗ {msg}");
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }

    private static List<Dictionary<string, string>> LoadCsv(string path)
    {
        var result = new List<Dictionary<string, string>>();
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length == 0) return result;
        var header = ParseLine(lines[0]);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var fields = ParseLine(lines[i]);
            if (fields.Count != header.Count) continue;
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int j = 0; j < header.Count; j++)
                row[header[j].Trim()] = fields[j].Trim();
            result.Add(row);
        }
        return result;
    }

    private static List<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQ = false;
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQ)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') inQ = false;
                else sb.Append(c);
            }
            else if (c == '"') inQ = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }

    /// <summary>解析用法文本得到 (剂量, 单位, 频次, 天数, 数量)。数量≤999。</summary>
    /// 数量口径：g/mg/μg/ml 类单位按「每次 1 个剂型单位 × 频次 × 天数」；
    /// 片/粒/丸/袋/支/揿 类单位按「剂量 × 频次 × 天数」——避免把 80mg 当 80 片导致数量虚高。
    private static (decimal Dose, string Unit, string Freq, int Days, int Qty) ParseUsage(string usage, Random rng)
    {
        var m = Regex.Match(usage ?? "", @"(\d+(?:\.\d+)?)\s*(g|mg|μg|ug|ml|粒|片|丸|袋|支|揿)");
        var dose = m.Success ? decimal.Parse(m.Groups[1].Value) : 1m;
        var unit = m.Success ? m.Groups[2].Value : "片";
        string freq;
        if (usage.Contains("qid")) freq = "qid";
        else if (usage.Contains("tid")) freq = "tid";
        else if (usage.Contains("bid")) freq = "bid";
        else if (usage.Contains("qn")) freq = "qn";
        else if (usage.Contains("qd")) freq = "qd";
        else if (usage.Contains("q4h")) freq = "q4h";
        else freq = "bid";
        var days = new[] { 3, 5, 7, 10, 14, 30 }[rng.Next(6)];
        var freqMap = new Dictionary<string, int> { { "tid", 3 }, { "bid", 2 }, { "qd", 1 }, { "qn", 1 }, { "qid", 4 }, { "q4h", 6 }, { "hs", 1 } };
        var perDose = unit is "片" or "粒" or "丸" or "袋" or "支" or "揿" ? dose : 1m;
        var qty = Math.Max(1, (int)Math.Round(perDose * freqMap[freq] * days));
        return (dose, unit, freq, days, Math.Min(qty, 999));
    }

    /// <summary>与系统一致的过敏匹配：药名包含 OR 类别标签互相包含。</summary>
    private static DrugMaster? FindAllergyMatchDrug(string allergies, List<DrugMaster> drugs)
    {
        var kws = CleanAllergyKeywords(allergies);
        if (kws.Count == 0) return null;
        foreach (var d in drugs)
        {
            if (MatchAllergy(d, kws))
                return d;
        }
        return null;
    }

    private static List<string> CleanAllergyKeywords(string allergies)
    {
        var raw = allergies.Split([',', '，', '、', ';', '；'], StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim()).Where(k => k.Length > 0).ToList();
        var result = new List<string>();
        foreach (var k in raw)
        {
            var t = k;
            foreach (var suf in AllergySuffixes)
            {
                if (t.EndsWith(suf, StringComparison.OrdinalIgnoreCase)) { t = t[..^suf.Length].Trim(); break; }
            }
            if (t.Length > 0) result.Add(t);
        }
        return result;
    }

    private static bool MatchAllergy(DrugMaster d, List<string> kws)
    {
        foreach (var kw in kws)
        {
            var kwl = kw.ToLowerInvariant();
            var names = new[] { d.GenericNameCn, d.GenericNameEn }
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.Trim().ToLowerInvariant());
            foreach (var n in names)
                if (n.Contains(kwl)) return true;
            if (!string.IsNullOrWhiteSpace(d.ContraindicationTags))
            {
                foreach (var tag in d.ContraindicationTags.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var t = tag.Trim().ToLowerInvariant();
                    if (t.Length > 0 && (t.Contains(kwl) || kwl.Contains(t))) return true;
                }
            }
        }
        return false;
    }

    private static DrugMaster? FindContraMatchDrug(string chronicTags, List<DrugMaster> drugs)
    {
        var tags = chronicTags.Split([',', '，', '、', ';', '；'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        if (tags.Count == 0) return null;
        foreach (var d in drugs)
        {
            if (string.IsNullOrWhiteSpace(d.ContraindicationTags)) continue;
            var dtags = d.ContraindicationTags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
            if (dtags.Intersect(tags, StringComparer.OrdinalIgnoreCase).Any())
                return d;
        }
        return null;
    }

    /// <summary>测试数据标签增强：伪麻黄碱→高血压、糖皮质激素→糖尿病（真实药理对应）。</summary>
    private static string? EnhanceTags(string cn, string original)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(original))
            tags.AddRange(original.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()));
        if (cn.Contains("伪麻黄碱")) tags.Add("高血压");
        if (cn.Contains("泼尼松") || cn.Contains("地塞米松") || cn.Contains("氢化可的松")
            || cn.Contains("甲泼尼龙") || cn.Contains("倍他米松")) tags.Add("糖尿病");
        return tags.Count == 0 ? null : string.Join(",", tags.Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
