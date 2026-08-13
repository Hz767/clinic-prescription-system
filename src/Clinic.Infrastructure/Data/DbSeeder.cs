using Clinic.Domain.Entities;
using Clinic.Domain.Interfaces;
using Clinic.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Infrastructure.Data;

/// <summary>
/// 数据库种子数据初始化器。
/// 首次启动时创建默认管理员账户和基础药品目录。
/// 幂等：已存在数据时跳过。
/// </summary>
public static class DbSeeder
{
    /// <summary>
    /// 执行种子数据初始化。在 App.OnStartup 中 EnsureCreatedAsync 之后调用。
    /// </summary>
    public static async Task SeedAsync(
        ClinicDbContext db,
        IPasswordHasher passwordHasher,
        IEncryptionService encryptionService,
        CancellationToken ct = default)
    {
        await SeedUsersAsync(db, passwordHasher, ct);
        await SeedSamplePatientsAsync(db, encryptionService, ct);
        await SeedSampleDrugsAsync(db, ct);
        // 先保存药品，确保 DrugMaster.Id 已分配，再创建库存批次
        await db.SaveChangesAsync(ct);
        await SeedDrugStocksAsync(db, ct);
        await SeedDrugInteractionsAsync(db, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 创建默认用户账户。
    /// admin（医生）/ nurse（护士）/ reader（只读），密码均为 admin123。
    /// 所有默认账户标记 MustChangePassword = true，首次登录后应立即修改密码。
    /// </summary>
    private static async Task SeedUsersAsync(
        ClinicDbContext db,
        IPasswordHasher passwordHasher,
        CancellationToken ct)
    {
        if (await db.SysUsers.AnyAsync(ct))
            return;

        var now = DateTime.UtcNow;
        var defaultPassword = passwordHasher.Hash("admin123");

        db.SysUsers.AddRange(
            new SysUser
            {
                Username = "admin",
                DisplayName = "陈医生",
                Role = UserRole.Doctor,
                PasswordHash = defaultPassword,
                IsActive = true,
                MustChangePassword = true,
                CreatedAt = now
            },
            new SysUser
            {
                Username = "nurse",
                DisplayName = "护士",
                Role = UserRole.Nurse,
                PasswordHash = defaultPassword,
                IsActive = true,
                MustChangePassword = true,
                CreatedAt = now
            },
            new SysUser
            {
                Username = "reader",
                DisplayName = "只读用户",
                Role = UserRole.Readonly,
                PasswordHash = defaultPassword,
                IsActive = true,
                MustChangePassword = true,
                CreatedAt = now
            }
        );
    }

    /// <summary>
    /// 创建样本患者数据，便于测试处方开具流程。
    /// </summary>
    private static async Task SeedSamplePatientsAsync(
        ClinicDbContext db, IEncryptionService encryption, CancellationToken ct)
    {
        if (await db.Patients.AnyAsync(ct))
            return;

        var now = DateTime.UtcNow;
        var patients = new[]
        {
            (Name: "张三", Gender: "男", Phone: "13800001111", Allergies: "青霉素", History: "高血压病史3年", ChronicTags: "高血压",
             Weight: 75.0m, Temperature: 36.5m, SystolicBP: 145, DiastolicBP: 92, HeartRate: 78),
            (Name: "李四", Gender: "女", Phone: "13800002222", Allergies: (string?)null, History: (string?)null, ChronicTags: (string?)null,
             Weight: 52.0m, Temperature: 36.8m, SystolicBP: 110, DiastolicBP: 70, HeartRate: 72),
            (Name: "王五", Gender: "男", Phone: "13900003333", Allergies: "磺胺类", History: "糖尿病史5年", ChronicTags: "糖尿病",
             Weight: 80.5m, Temperature: 37.2m, SystolicBP: 130, DiastolicBP: 85, HeartRate: 82),
            (Name: "张丽", Gender: "女", Phone: "13600004444", Allergies: (string?)null, History: "过敏性鼻炎", ChronicTags: (string?)null,
             Weight: 48.0m, Temperature: 36.6m, SystolicBP: 105, DiastolicBP: 65, HeartRate: 68),
            (Name: "赵六", Gender: "男", Phone: "13700005555", Allergies: "头孢类", History: (string?)null, ChronicTags: (string?)null,
             Weight: 68.0m, Temperature: 36.7m, SystolicBP: 120, DiastolicBP: 80, HeartRate: 75),
        };

        foreach (var (name, gender, phone, allergies, history, chronicTags, weight, temperature, systolicBP, diastolicBP, heartRate) in patients)
        {
            var phoneHash = encryption.HashPhone(phone);

            db.Patients.Add(new Patient
            {
                Name = name,
                Gender = gender,
                PhoneEncrypted = encryption.Encrypt(phone),
                PhoneHash = phoneHash,
                Allergies = allergies,
                History = history,
                ChronicTags = chronicTags,
                Weight = weight,
                Temperature = temperature,
                SystolicBP = systolicBP,
                DiastolicBP = diastolicBP,
                HeartRate = heartRate,
                CreatedAt = now
            });
        }
    }

    /// <summary>
    /// 创建基础药品目录样本（P0 阶段便于测试，生产环境应通过入库功能添加）。
    /// </summary>
    private static async Task SeedSampleDrugsAsync(ClinicDbContext db, CancellationToken ct)
    {
        if (await db.DrugMasters.AnyAsync(ct))
            return;

        var samples = new[]
        {
            new DrugMaster
            {
                GenericNameCn = "阿莫西林胶囊",
                GenericNameEn = "Amoxicillin Capsules",
                Spec = "0.25g×24粒",
                Unit = "盒",
                IsAntibiotic = true,
                AntibioticLevel = AntibioticLevel.NonRestricted,
                RetailPriceRef = 12.50m,
                CreatedAt = DateTime.UtcNow
            },
            new DrugMaster
            {
                GenericNameCn = "布洛芬片",
                GenericNameEn = "Ibuprofen Tablets",
                Spec = "0.2g×100片",
                Unit = "瓶",
                IsAntibiotic = false,
                AntibioticLevel = AntibioticLevel.None,
                RetailPriceRef = 8.00m,
                CreatedAt = DateTime.UtcNow
            },
            new DrugMaster
            {
                GenericNameCn = "复方甘草片",
                GenericNameEn = "Compound Licorice Tablets",
                Spec = "100片",
                Unit = "瓶",
                IsAntibiotic = false,
                AntibioticLevel = AntibioticLevel.None,
                RetailPriceRef = 5.50m,
                CreatedAt = DateTime.UtcNow
            },
            new DrugMaster
            {
                GenericNameCn = "头孢克洛胶囊",
                GenericNameEn = "Cefaclor Capsules",
                Spec = "0.25g×12粒",
                Unit = "盒",
                IsAntibiotic = true,
                AntibioticLevel = AntibioticLevel.Restricted,
                RetailPriceRef = 25.00m,
                CreatedAt = DateTime.UtcNow
            },
            new DrugMaster
            {
                GenericNameCn = "奥美拉唑肠溶胶囊",
                GenericNameEn = "Omeprazole Enteric Capsules",
                Spec = "20mg×14粒",
                Unit = "盒",
                IsAntibiotic = false,
                AntibioticLevel = AntibioticLevel.None,
                RetailPriceRef = 18.00m,
                CreatedAt = DateTime.UtcNow
            }
        };

        db.DrugMasters.AddRange(samples);
    }

    /// <summary>
    /// 创建药品库存种子数据。
    /// 为每种基础药品创建一个库存批次，确保首次开具处方时不会因库存不足报错。
    /// </summary>
    private static async Task SeedDrugStocksAsync(ClinicDbContext db, CancellationToken ct)
    {
        if (await db.DrugStocks.AnyAsync(ct))
            return;

        var drugs = await db.DrugMasters.ToListAsync(ct);
        if (drugs.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var expiry = DateOnly.FromDateTime(now.AddDays(365));

        foreach (var drug in drugs)
        {
            db.DrugStocks.Add(new DrugStock
            {
                DrugId = drug.Id,
                BatchNo = $"SEED-{now:yyyyMMdd}",
                ExpiryDate = expiry,
                QtyRemaining = 100m,
                CostPrice = drug.CostPriceRef ?? 0m,
                Supplier = "种子数据",
                ReceivedAt = now
            });
        }
    }

    /// <summary>
    /// 创建药品交互种子数据。
    /// 基于 DDInter 数据库格式的示例交互记录，用于演示 Major 级交互阻断功能。
    /// 实际数据应从 DDInter 2.0 数据库导入。
    /// </summary>
    private static async Task SeedDrugInteractionsAsync(ClinicDbContext db, CancellationToken ct)
    {
        if (await db.DrugInteractions.AnyAsync(ct))
            return;

        var now = DateTime.UtcNow;

        var interactions = new[]
        {
            // 阿莫西林 ↔ 头孢克洛：β-内酰胺类交叉过敏（Major）
            new DrugInteraction
            {
                DdinterIdA = 1,
                DrugNameA = "阿莫西林",
                DdinterIdB = 2,
                DrugNameB = "头孢克洛",
                Level = DrugInteractionLevel.Major,
                SourceAtcCode = "J01CA04/J01DC02",
                ImportedAt = now
            },
            // 布洛芬 ↔ 阿司匹林：NSAIDs 叠加出血风险（Major）— 阿司匹林未在目录中但保留数据
            new DrugInteraction
            {
                DdinterIdA = 3,
                DrugNameA = "布洛芬",
                DdinterIdB = 4,
                DrugNameB = "阿司匹林",
                Level = DrugInteractionLevel.Major,
                SourceAtcCode = "M01AE01/N02BA01",
                ImportedAt = now
            },
            // 奥美拉唑 ↔ 头孢克洛： Moderate（PPI 影响抗菌药物吸收）
            new DrugInteraction
            {
                DdinterIdA = 5,
                DrugNameA = "奥美拉唑",
                DdinterIdB = 2,
                DrugNameB = "头孢克洛",
                Level = DrugInteractionLevel.Moderate,
                SourceAtcCode = "A02BC01/J01DC02",
                ImportedAt = now
            },
            // 布洛芬 ↔ 复方甘草片：Minor（轻度胃部不适风险）
            new DrugInteraction
            {
                DdinterIdA = 3,
                DrugNameA = "布洛芬",
                DdinterIdB = 6,
                DrugNameB = "复方甘草片",
                Level = DrugInteractionLevel.Minor,
                SourceAtcCode = "M01AE01/R05DA",
                ImportedAt = now
            }
        };

        db.DrugInteractions.AddRange(interactions);
    }
}
