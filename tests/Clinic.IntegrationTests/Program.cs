using Clinic.Application;
using Clinic.Application.Interfaces;
using Clinic.Domain.Entities;
using Clinic.Domain.Interfaces;
using Clinic.Infrastructure.Common;
using Clinic.Infrastructure.Data;
using Clinic.Infrastructure.Encryption;
using Clinic.Infrastructure.Llm;
using Clinic.Infrastructure.Pdf;
using Clinic.Infrastructure.Repositories;
using Clinic.Infrastructure.Security;
using Clinic.Shared.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.IntegrationTests;

/// <summary>
/// 诊所处方系统集成测试运行器。
/// 9 个测试案例覆盖不同业务逻辑场景，验证核心业务规则、合规要求和事务完整性。
/// </summary>
public static class Program
{
    private static int _passed;
    private static int _failed;
    private static readonly List<string> _failureDetails = new();

    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length > 0 && args[0] == "--reproduce")
        {
            var prescriptionId = args.Length > 1 && long.TryParse(args[1], out var pid) ? pid : 483;
            await ReproduceDispenseFailureAsync(prescriptionId);
            return;
        }

        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine("  个人诊所处方系统 — 集成测试运行器");
        Console.WriteLine("  覆盖 9 个业务场景，验证核心逻辑、合规规则、事务完整性");
        Console.WriteLine("═══════════════════════════════════════════════════════════\n");

        await RunTest("TC01", "完整普通处方流程（主诉+体征+AI审核+PDF）", TC01_CompleteNormalWorkflow);
        await RunTest("TC02", "过敏史拦截（青霉素过敏→阿莫西林）", TC02_AllergyBlock);
        await RunTest("TC03", "库存不足拦截+事务回滚验证", TC03_InsufficientInventory);
        await RunTest("TC04", "急诊处方天数超限（5天>3天上限）", TC04_EmergencyMaxDays);
        await RunTest("TC05", "处方作废+退款+库存恢复+日报表", TC05_VoidAndRefund);
        await RunTest("TC06", "儿科处方完整流程（儿童剂量+体征）", TC06_PediatricWorkflow);
        await RunTest("TC07", "重复药品拦截（同一药品添加两次）", TC07_DuplicateDrug);
        await RunTest("TC08", "药品品种上限（第6种药品拦截）", TC08_MaxDrugItems);
        await RunTest("TC09", "内联编辑持久化+删除明细+空处方拦截", TC09_InlineEditAndDelete);
        await RunTest("TC10", "发药全流程（保存→审核→收费→发药→FIFO扣库存→出库流水）", TC10_DispenseWorkflow);

        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine($"  测试结果：{_passed + _failed} 项，通过 {_passed} 项，失败 {_failed} 项");
        if (_failed > 0)
        {
            Console.WriteLine("\n  失败详情：");
            foreach (var detail in _failureDetails)
                Console.WriteLine($"    ✗ {detail}");
        }
        Console.WriteLine("═══════════════════════════════════════════════════════════\n");

        Environment.Exit(_failed > 0 ? 1 : 0);
    }

    /// <summary>运行单个测试并捕获结果</summary>
    private static async Task RunTest(string id, string name, Func<Task> test)
    {
        Console.WriteLine($"┌─ {id}: {name}");
        try
        {
            await test();
            Console.WriteLine($"└─ {id}: ✓ 通过\n");
            _passed++;
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            Console.WriteLine($"└─ {id}: ✗ 失败 — {msg}\n");
            _failed++;
            _failureDetails.Add($"{id} {name}: {msg}");
        }
    }

    // ════════════════════════════════════════════════════════════════
    // TC01: 完整普通处方流程
    // ════════════════════════════════════════════════════════════════
    private static async Task TC01_CompleteNormalWorkflow()
    {
        using var host = new TestHost();
        host.LoginAsDoctor();

        // 1. 创建患者（含体征）
        var patientId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPatientService>();
            return await svc.CreatePatientAsync(
                "张三", "男", new DateOnly(1990, 5, 15), "13800001111",
                null, null, null, weight: 70.5m, temperature: 36.8m,
                systolicBP: 120, diastolicBP: 80, heartRate: 75);
        });
        Assert(patientId > 0, "患者创建应返回有效 ID");

        // 2. 创建处方（含主诉）
        var prescriptionId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.CreatePrescriptionAsync(
                patientId, host.DoctorId, "发热3天，咳嗽伴咽痛",
                "急性上呼吸道感染", null, (int)PrescriptionType.Normal, null);
        });
        Assert(prescriptionId > 0, "处方创建应返回有效 ID");

        // 3. 添加药品
        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            await svc.AddPrescriptionItemAsync(prescriptionId, host.DrugIds[0],
                2m, "粒", "每日三次", "口服", 7, 21);
            await svc.AddPrescriptionItemAsync(prescriptionId, host.DrugIds[1],
                1m, "片", "每日三次", "口服", 5, 15);
        });

        // 4. 保存
        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            Assert(await svc.SavePrescriptionAsync(prescriptionId), "保存应返回 true");
        });

        // 5. 审核
        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            Assert(await svc.ReviewPrescriptionAsync(prescriptionId, "审核通过"), "审核应返回 true");
        });

        // 6. AI 预审
        var aiReport = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.GetAiReviewSuggestionsAsync(prescriptionId);
        });
        Assert(aiReport.Contains("AI 辅助预审报告"), "AI 预审报告应包含标题");

        // 7. 收费
        var dto = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.GetPrescriptionByIdAsync(prescriptionId);
        });
        var amount = dto!.TotalAmount;

        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IBillingService>();
            var pid = await svc.RecordPaymentAsync(prescriptionId,
                (int)PaymentMethod.Cash, amount, host.DoctorId, null, "测试收费");
            Assert(pid > 0, "收费应返回有效流水 ID");
        });

        // 8. 生成 PDF
        var pdfPath = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            var dir = Path.Combine(Path.GetTempPath(), "clinic_test_pdf");
            return await svc.GeneratePrescriptionPdfAsync(prescriptionId, dir);
        });
        Assert(File.Exists(pdfPath), "PDF 文件应存在");

        // 9. 验证处方详情（收费后、发药前）
        var final = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.GetPrescriptionByIdAsync(prescriptionId);
        });
        Assert(final!.Status == (int)PrescriptionStatus.Paid, "最终状态应为 Paid");
        Assert(final.ChiefComplaint == "发热3天，咳嗽伴咽痛", "主诉应正确");
        Assert(final.Items.Count == 2, "应有 2 种药品");

        // 9.5 保存/审核/收费阶段不扣库存，发药时才扣
        var stockBeforeDispense = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IInventoryService>();
            return await svc.GetStockQuantityAsync(host.DrugIds[0]);
        });
        Assert(stockBeforeDispense == 100m, $"发药前库存应为 100（收费不扣减），实际 {stockBeforeDispense}");

        // 9.6 发药（FIFO 扣减库存）
        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            Assert(await svc.DispensePrescriptionAsync(prescriptionId), "发药应返回 true");
        });

        // 10. 验证库存扣减
        var stock = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IInventoryService>();
            return await svc.GetStockQuantityAsync(host.DrugIds[0]);
        });
        Assert(stock == 79m, $"阿莫西林库存应为 79（100-21），实际 {stock}");

        var finalDispensed = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.GetPrescriptionByIdAsync(prescriptionId);
        });
        Assert(finalDispensed!.Status == (int)PrescriptionStatus.Dispensed, "发药后状态应为 Dispensed");

        // 11. 验证体征保存
        var patient = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPatientService>();
            return await svc.GetPatientByIdAsync(patientId);
        });
        Assert(patient!.Weight == 70.5m, "体重应正确保存");
        Assert(patient.Temperature == 36.8m, "体温应正确保存");
        Assert(patient.SystolicBP == 120, "收缩压应正确保存");

        Console.WriteLine("    ✓ 患者→处方→药品→保存→审核→AI预审→收费→PDF→库存→体征 全流程验证通过");
    }

    // ════════════════════════════════════════════════════════════════
    // TC02: 过敏史拦截
    // ════════════════════════════════════════════════════════════════
    private static async Task TC02_AllergyBlock()
    {
        using var host = new TestHost();
        host.LoginAsDoctor();

        var patientId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPatientService>();
            return await svc.CreatePatientAsync(
                "李四", "女", new DateOnly(1985, 3, 20),
                "13800002222", "阿莫西林过敏", null, null);
        });

        var prescriptionId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.CreatePrescriptionAsync(
                patientId, host.DoctorId, null, "扁桃体炎",
                null, (int)PrescriptionType.Normal, null);
        });

        // 添加阿莫西林（F-01 修复：添加阶段不硬拦截，由保存前统一检查）
        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            await svc.AddPrescriptionItemAsync(prescriptionId, host.DrugIds[0],
                2m, "粒", "每日三次", "口服", 7, 21);
        });

        // 添加非过敏药品 → 应成功
        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            await svc.AddPrescriptionItemAsync(prescriptionId, host.DrugIds[1],
                1m, "片", "每日三次", "口服", 5, 15);
        });

        // 保存处方 → 应被过敏史拦截（无临床覆盖理由时硬拦截）
        await AssertThrowsAsync<InvalidOperationException>(async () =>
        {
            await host.ExecuteInScopeAsync(async sp =>
            {
                var svc = sp.GetRequiredService<IPrescriptionService>();
                await svc.SavePrescriptionAsync(prescriptionId);
            });
        }, "过敏史拦截");

        // 医生填写临床覆盖理由后可保存（F-01：专业判断优先）
        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            Assert(await svc.SavePrescriptionAsync(prescriptionId, "皮试阴性，临床评估可用"),
                "填写临床覆盖理由后保存应返回 true");
        });

        Console.WriteLine("    ✓ 青霉素过敏→保存拦截→覆盖理由放行→布洛芬添加成功");
    }

    // ════════════════════════════════════════════════════════════════
    // TC03: 库存不足 + 事务回滚
    // ════════════════════════════════════════════════════════════════
    private static async Task TC03_InsufficientInventory()
    {
        using var host = new TestHost();
        host.LoginAsDoctor();

        var patientId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPatientService>();
            return await svc.CreatePatientAsync(
                "王五", "男", new DateOnly(2000, 1, 1), "13800003333", null, null, null);
        });

        var prescriptionId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            var id = await svc.CreatePrescriptionAsync(
                patientId, host.DoctorId, "头痛1天", "偏头痛",
                null, (int)PrescriptionType.Normal, null);
            // Qty=200 超过库存 100
            await svc.AddPrescriptionItemAsync(id, host.DrugIds[0],
                2m, "粒", "每日三次", "口服", 7, 200);
            return id;
        });

        // 保存 → 库存不足应抛异常
        await AssertThrowsAsync<InvalidOperationException>(async () =>
        {
            await host.ExecuteInScopeAsync(async sp =>
            {
                var svc = sp.GetRequiredService<IPrescriptionService>();
                await svc.SavePrescriptionAsync(prescriptionId);
            });
        }, "库存不足");

        // 处方状态应为 Draft（事务回滚）
        var dto = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.GetPrescriptionByIdAsync(prescriptionId);
        });
        Assert(dto!.Status == (int)PrescriptionStatus.Draft, "事务回滚后状态应为 Draft");

        // 库存未扣减
        var stock = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IInventoryService>();
            return await svc.GetStockQuantityAsync(host.DrugIds[0]);
        });
        Assert(stock == 100m, $"库存应未扣减（100），实际 {stock}");

        Console.WriteLine("    ✓ 库存不足→保存被拦截→事务回滚→状态保持Draft→库存未扣减");
    }

    // ════════════════════════════════════════════════════════════════
    // TC04: 急诊处方天数超限
    // ════════════════════════════════════════════════════════════════
    private static async Task TC04_EmergencyMaxDays()
    {
        using var host = new TestHost();
        host.LoginAsDoctor();

        var patientId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPatientService>();
            return await svc.CreatePatientAsync(
                "赵六", "男", new DateOnly(1995, 7, 10), "13800004444", null, null, null);
        });

        var prescriptionId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.CreatePrescriptionAsync(
                patientId, host.DoctorId, "胸痛2小时", "急性胃肠炎",
                null, (int)PrescriptionType.Emergency, null);
        });

        // 5 天 > 3 天上限 → 应被拦截
        await AssertThrowsAsync<InvalidOperationException>(async () =>
        {
            await host.ExecuteInScopeAsync(async sp =>
            {
                var svc = sp.GetRequiredService<IPrescriptionService>();
                await svc.AddPrescriptionItemAsync(prescriptionId, host.DrugIds[1],
                    1m, "片", "每日三次", "口服", 5, 15);
            });
        }, "急诊");

        // 3 天可以正常添加
        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            await svc.AddPrescriptionItemAsync(prescriptionId, host.DrugIds[1],
                1m, "片", "每日三次", "口服", 3, 9);
        });

        Console.WriteLine("    ✓ 急诊处方5天被拦截（上限3天）→ 3天添加成功");
    }

    // ════════════════════════════════════════════════════════════════
    // TC05: 处方作废 + 退款 + 库存恢复
    // ════════════════════════════════════════════════════════════════
    private static async Task TC05_VoidAndRefund()
    {
        using var host = new TestHost();
        host.LoginAsDoctor();

        var patientId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPatientService>();
            return await svc.CreatePatientAsync(
                "钱七", "女", new DateOnly(1988, 12, 5), "13800005555", null, null, null);
        });

        // 完整流程到收费
        var prescriptionId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            var id = await svc.CreatePrescriptionAsync(
                patientId, host.DoctorId, "腹痛1天", "急性肠炎",
                null, (int)PrescriptionType.Normal, null);
            await svc.AddPrescriptionItemAsync(id, host.DrugIds[4],
                1m, "袋", "每日三次", "口服", 3, 9);
            await svc.SavePrescriptionAsync(id);
            await svc.ReviewPrescriptionAsync(id, "审核通过");
            return id;
        });

        var amount = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return (await svc.GetPrescriptionByIdAsync(prescriptionId))!.TotalAmount;
        });

        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IBillingService>();
            await svc.RecordPaymentAsync(prescriptionId,
                (int)PaymentMethod.Cash, amount, host.DoctorId, null, "测试收费");
        });

        // 发药（发药时按 FIFO 扣减库存）
        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            Assert(await svc.DispensePrescriptionAsync(prescriptionId), "发药应返回 true");
        });

        // 作废前库存（发药已扣减 9）
        var stockBefore = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IInventoryService>();
            return await svc.GetStockQuantityAsync(host.DrugIds[4]);
        });
        Assert(stockBefore == 91m, $"作废前库存应为 91（100-9），实际 {stockBefore}");

        // 作废
        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            Assert(await svc.VoidPrescriptionAsync(prescriptionId, "用药错误"),
                "作废应返回 true");
        });

        // 状态验证
        var dto = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.GetPrescriptionByIdAsync(prescriptionId);
        });
        Assert(dto!.Status == (int)PrescriptionStatus.Voided, "状态应为 Voided");

        // 库存恢复
        var stockAfter = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IInventoryService>();
            return await svc.GetStockQuantityAsync(host.DrugIds[4]);
        });
        Assert(stockAfter == 100m, $"库存应恢复至 100，实际 {stockAfter}");

        // 冲正记录验证
        var hasReversal = await host.ExecuteInScopeAsync(async sp =>
        {
            var repo = sp.GetRequiredService<IRepository<PaymentLog>>();
            var all = await repo.GetAllAsync();
            return all.Any(p => p.PrescriptionId == prescriptionId && p.IsReversal);
        });
        Assert(hasReversal, "应存在 IsReversal=true 的冲正记录");

        // 日报表验证（收入 = 正常 - 冲正 = 0）
        var report = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IBillingService>();
            return await svc.GetDailyReportAsync(DateTime.Today);
        });
        Assert(report!.TotalAmount == 0m, $"日报表收入应为 0（正常-冲正），实际 {report.TotalAmount}");

        Console.WriteLine("    ✓ 收费→作废→冲正记录→库存恢复→日报表收入=0 全链路验证通过");
    }

    // ════════════════════════════════════════════════════════════════
    // TC06: 儿科处方完整流程
    // ════════════════════════════════════════════════════════════════
    private static async Task TC06_PediatricWorkflow()
    {
        using var host = new TestHost();
        host.LoginAsDoctor();

        var patientId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPatientService>();
            return await svc.CreatePatientAsync(
                "小明", "男", new DateOnly(2020, 6, 15), "13800006666",
                null, null, null, weight: 15.0m, temperature: 38.5m, heartRate: 110);
        });

        var prescriptionId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.CreatePrescriptionAsync(
                patientId, host.DoctorId, "发热2天，食欲不振",
                "小儿急性扁桃体炎", null, (int)PrescriptionType.Pediatric, null);
        });

        // 验证类型
        var dto = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.GetPrescriptionByIdAsync(prescriptionId);
        });
        Assert(dto!.Type == (int)PrescriptionType.Pediatric, "类型应为 Pediatric (2)");

        // 添加药品（儿童剂量）
        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            await svc.AddPrescriptionItemAsync(prescriptionId, host.DrugIds[6],
                0.25m, "片", "每日三次", "口服", 5, 4);
        });

        // 保存 + 审核 + 收费
        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            await svc.SavePrescriptionAsync(prescriptionId);
            await svc.ReviewPrescriptionAsync(prescriptionId, "儿科审核通过");
        });

        var amount = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return (await svc.GetPrescriptionByIdAsync(prescriptionId))!.TotalAmount;
        });

        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IBillingService>();
            await svc.RecordPaymentAsync(prescriptionId,
                (int)PaymentMethod.Cash, amount, host.DoctorId, null, "儿科收费");
        });

        // 最终状态
        var final = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.GetPrescriptionByIdAsync(prescriptionId);
        });
        Assert(final!.Status == (int)PrescriptionStatus.Paid, "状态应为 Paid");
        Assert(final.ChiefComplaint == "发热2天，食欲不振", "主诉应正确");

        // PDF
        var pdfPath = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            var dir = Path.Combine(Path.GetTempPath(), "clinic_test_pdf");
            return await svc.GeneratePrescriptionPdfAsync(prescriptionId, dir);
        });
        Assert(File.Exists(pdfPath), "儿科处方 PDF 应存在");

        Console.WriteLine($"    ✓ 儿科处方→儿童剂量→保存→审核→收费→PDF 金额=¥{amount:F2} 全流程通过");
    }

    // ════════════════════════════════════════════════════════════════
    // TC07: 重复药品拦截
    // ════════════════════════════════════════════════════════════════
    private static async Task TC07_DuplicateDrug()
    {
        using var host = new TestHost();
        host.LoginAsDoctor();

        var patientId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPatientService>();
            return await svc.CreatePatientAsync(
                "孙八", "男", new DateOnly(1992, 4, 8), "13800007777", null, null, null);
        });

        var prescriptionId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.CreatePrescriptionAsync(
                patientId, host.DoctorId, "咳嗽3天", "急性支气管炎",
                null, (int)PrescriptionType.Normal, null);
        });

        // 第一次添加 → 成功
        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            await svc.AddPrescriptionItemAsync(prescriptionId, host.DrugIds[0],
                2m, "粒", "每日三次", "口服", 7, 42);
        });

        // 第二次同一药品 → 应被拦截
        await AssertThrowsAsync<InvalidOperationException>(async () =>
        {
            await host.ExecuteInScopeAsync(async sp =>
            {
                var svc = sp.GetRequiredService<IPrescriptionService>();
                await svc.AddPrescriptionItemAsync(prescriptionId, host.DrugIds[0],
                    1m, "粒", "每日两次", "口服", 5, 10);
            });
        }, "重复添加");

        Console.WriteLine("    ✓ 同一药品第二次添加被拦截");
    }

    // ════════════════════════════════════════════════════════════════
    // TC08: 药品品种上限（>5种）
    // ════════════════════════════════════════════════════════════════
    private static async Task TC08_MaxDrugItems()
    {
        using var host = new TestHost();
        host.LoginAsDoctor();

        var patientId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPatientService>();
            return await svc.CreatePatientAsync(
                "周九", "女", new DateOnly(1993, 9, 9), "13800008888", null, null, null);
        });

        var prescriptionId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.CreatePrescriptionAsync(
                patientId, host.DoctorId, "全身不适", "多症状综合",
                null, (int)PrescriptionType.Normal, null);
        });

        // 添加 5 种 → 全部成功
        for (var i = 0; i < 5; i++)
        {
            await host.ExecuteInScopeAsync(async sp =>
            {
                var svc = sp.GetRequiredService<IPrescriptionService>();
                await svc.AddPrescriptionItemAsync(prescriptionId, host.DrugIds[i],
                    1m, "片", "每日三次", "口服", 7, 21);
            });
        }

        // 第 6 种 → 应被拦截
        await AssertThrowsAsync<InvalidOperationException>(async () =>
        {
            await host.ExecuteInScopeAsync(async sp =>
            {
                var svc = sp.GetRequiredService<IPrescriptionService>();
                await svc.AddPrescriptionItemAsync(prescriptionId, host.DrugIds[5],
                    1m, "片", "每日三次", "口服", 7, 21);
            });
        }, "不得超过");

        Console.WriteLine("    ✓ 5种药品成功 → 第6种被拦截（上限5种）");
    }

    // ════════════════════════════════════════════════════════════════
    // TC09: 内联编辑持久化 + 删除明细
    // ════════════════════════════════════════════════════════════════
    private static async Task TC09_InlineEditAndDelete()
    {
        using var host = new TestHost();
        host.LoginAsDoctor();

        var patientId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPatientService>();
            return await svc.CreatePatientAsync(
                "吴十", "男", new DateOnly(1991, 11, 11), "13800009999", null, null, null);
        });

        var prescriptionId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.CreatePrescriptionAsync(
                patientId, host.DoctorId, "感冒2天", "普通感冒",
                null, (int)PrescriptionType.Normal, null);
        });

        // 添加药品
        long itemId = 0;
        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            await svc.AddPrescriptionItemAsync(prescriptionId, host.DrugIds[0],
                2m, "粒", "每日三次", "口服", 7, 42);
            var dto = await svc.GetPrescriptionByIdAsync(prescriptionId);
            itemId = dto!.Items.First().Id;
        });
        Assert(itemId > 0, "明细 ID 应有效");

        // 内联编辑：剂量1粒，频次每日两次，疗程3天，Qty=6
        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            Assert(await svc.UpdatePrescriptionItemAsync(
                prescriptionId, itemId, 1m, "粒", "每日两次", "口服", 3, 6),
                "内联编辑应返回 true");
        });

        // 验证持久化
        var dto = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.GetPrescriptionByIdAsync(prescriptionId);
        });
        var item = dto!.Items.First();
        Assert(item.Dose == 1m, $"剂量应为 1，实际 {item.Dose}");
        Assert(item.Frequency == "每日两次", $"频次应为每日两次，实际 {item.Frequency}");
        Assert(item.DurationDays == 3, $"疗程应为 3，实际 {item.DurationDays}");
        Assert(item.Qty == 6m, $"数量应为 6，实际 {item.Qty}");
        // 整盒计价：规格"0.25g*24粒"→包装数量24，ceil(6/24)=1盒 × 单价0.8 = 0.8
        Assert(item.Subtotal == 0.8m, $"小计应为 0.8（ceil(6/24)盒×0.8），实际 {item.Subtotal}");

        // 删除明细
        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            await svc.RemovePrescriptionItemAsync(prescriptionId, itemId);
        });

        // 验证空
        var dtoAfter = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.GetPrescriptionByIdAsync(prescriptionId);
        });
        Assert(dtoAfter!.Items.Count == 0, "删除后明细应为空");

        // 空处方保存被拦截
        await AssertThrowsAsync<InvalidOperationException>(async () =>
        {
            await host.ExecuteInScopeAsync(async sp =>
            {
                var svc = sp.GetRequiredService<IPrescriptionService>();
                await svc.SavePrescriptionAsync(prescriptionId);
            });
        }, "没有明细");

        Console.WriteLine("    ✓ 添加→内联编辑（剂量/频次/疗程/数量/小计）→删除→空处方保存拦截 全通过");
    }

    // ── 断言辅助方法 ──

    // ════════════════════════════════════════════════════════════════
    // TC10: 发药全流程（保存→审核→收费→发药→FIFO扣库存→出库流水）
    // ════════════════════════════════════════════════════════════════
    private static async Task TC10_DispenseWorkflow()
    {
        using var host = new TestHost();
        host.LoginAsDoctor();

        var patientId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPatientService>();
            return await svc.CreatePatientAsync(
                "王五", "男", new DateOnly(1980, 8, 1), "13800005555", null, null, null);
        });
        Assert(patientId > 0, "患者创建应返回有效 ID");

        var prescriptionId = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.CreatePrescriptionAsync(
                patientId, host.DoctorId, "咳嗽三天", "急性支气管炎",
                null, (int)PrescriptionType.Normal, null);
        });

        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            await svc.AddPrescriptionItemAsync(prescriptionId, host.DrugIds[0],
                2m, "粒", "每日三次", "口服", 7, 21);
        });

        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            Assert(await svc.SavePrescriptionAsync(prescriptionId), "保存应返回 true");
        });

        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            Assert(await svc.ReviewPrescriptionAsync(prescriptionId, "审核通过"), "审核应返回 true");
        });

        var amount = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return (await svc.GetPrescriptionByIdAsync(prescriptionId))!.TotalAmount;
        });

        await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IBillingService>();
            var pid = await svc.RecordPaymentAsync(prescriptionId,
                (int)PaymentMethod.Cash, amount, host.DoctorId, null, "测试收费");
            Assert(pid > 0, "收费应返回有效流水 ID");
        });

        // 发药前库存应为 100（保存/收费不扣减，发药时才扣）
        var stockBefore = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IInventoryService>();
            return await svc.GetStockQuantityAsync(host.DrugIds[0]);
        });
        Assert(stockBefore == 100m, $"发药前库存应为 100，实际 {stockBefore}");

        // 发药（核心：重现用户的 DbUpdateException）
        var dispensed = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.DispensePrescriptionAsync(prescriptionId);
        });
        Assert(dispensed, "发药应返回 true");

        // 验证库存 FIFO 扣减
        var stockAfter = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IInventoryService>();
            return await svc.GetStockQuantityAsync(host.DrugIds[0]);
        });
        Assert(stockAfter == 79m, $"发药后库存应为 79（100-21），实际 {stockAfter}");

        // 验证出库流水已生成
        var outCount = await host.ExecuteInScopeAsync(async sp =>
        {
            var repo = sp.GetRequiredService<IRepository<DrugOut>>();
            var outs = await repo.FindAsync(o => o.PrescriptionId == prescriptionId && !o.IsReversal);
            return outs.Count;
        });
        Assert(outCount > 0, $"应生成出库流水，实际 {outCount} 条");

        // 验证处方状态为已发药
        var final = await host.ExecuteInScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<IPrescriptionService>();
            return await svc.GetPrescriptionByIdAsync(prescriptionId);
        });
        Assert(final!.Status == (int)PrescriptionStatus.Dispensed, "最终状态应为 Dispensed");

        Console.WriteLine("    ✓ 保存→审核→收费→发药→FIFO扣库存→出库流水 全流程验证通过");
    }

    /// <summary>
    /// 复现生产环境发药失败：复制 clinic.db 到临时文件，对指定处方执行发药。
    /// 用法：dotnet run -- --reproduce [处方ID]
    /// </summary>
    private static async Task ReproduceDispenseFailureAsync(long prescriptionId)
    {
        var srcDb = @"E:\个人诊所处方系统\clinic.db";
        var dbPath = Path.Combine(Path.GetTempPath(), $"clinic_repro_{Guid.NewGuid():N}.db");
        File.Copy(srcDb, dbPath, true);
        Console.WriteLine($"[Repro] 已复制生产数据库 → {dbPath}");
        Console.WriteLine($"[Repro] 目标处方 ID：{prescriptionId}\n");

        var services = new ServiceCollection();
        services.AddScoped(_ =>
        {
            var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; " +
                "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();
            return conn;
        });
        services.AddDbContext<ClinicDbContext>((sp, options) =>
            options.UseSqlite(sp.GetRequiredService<SqliteConnection>()));
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IEncryptionService>(_ => new AesGcmEncryptionService(new byte[32]));
        services.AddSingleton<IPasswordHasher>(_ => new Pbkdf2PasswordHasher("test-pepper", null));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPdfService, QuestPdfService>();
        services.AddSingleton<ILlmService, NoOpLlmService>();
        services.AddApplication();
        await using var provider = services.BuildServiceProvider();

        // 查询 admin 用户并以其身份登录（Doctor 角色具备发药权限）
        long adminId;
        using (var scope = provider.CreateScope())
        {
            var userRepo = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
            var admin = (await userRepo.FindAsync(u => u.Username == "admin")).FirstOrDefault();
            adminId = admin?.Id ?? 1;
        }
        var session = provider.GetRequiredService<IUserSession>();
        session.SetAuthenticated(adminId, "admin", "管理员", UserRole.Doctor);
        Console.WriteLine($"[Repro] 已以 admin（ID={adminId}）Doctor 角色登录\n");

        // 发药前状态
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
            var rx = await db.Prescriptions.FindAsync(prescriptionId);
            if (rx is null)
            {
                Console.WriteLine($"[Repro] 处方 {prescriptionId} 不存在，退出");
                return;
            }
            Console.WriteLine($"[Repro] 发药前状态：{(PrescriptionStatus)rx.Status}");
            var items = await db.PrescriptionItems.Where(i => i.PrescriptionId == prescriptionId).ToListAsync();
            foreach (var item in items)
                Console.WriteLine($"[Repro]   明细：{item.DrugName} Qty={item.Qty} PackQty={item.PackQuantity}");
            var stocks = await db.DrugStocks.Where(s => items.Select(i => i.DrugId).Contains(s.DrugId)).ToListAsync();
            foreach (var s in stocks)
                Console.WriteLine($"[Repro]   库存：DrugId={s.DrugId} Batch={s.BatchNo} 到期={s.ExpiryDate} 余量={s.QtyRemaining}");
        }

        // 执行发药
        Console.WriteLine("\n[Repro] 开始发药……");
        try
        {
            using var scope = provider.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();
            var success = await svc.DispensePrescriptionAsync(prescriptionId);
            Console.WriteLine($"[Repro] DispensePrescriptionAsync 返回：{success}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[Repro] ✗ 发药抛出异常：");
            for (var e = (Exception?)ex; e is not null; e = e.InnerException)
                Console.WriteLine($"[Repro]   {e.GetType().Name}: {e.Message}");
        }

        // 发药后状态
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
            var rx = await db.Prescriptions.FindAsync(prescriptionId);
            Console.WriteLine($"\n[Repro] 发药后状态：{(PrescriptionStatus)rx!.Status} DispensedAt={rx.DispensedAt}");
            var outs = await db.DrugOuts.Where(o => o.PrescriptionId == prescriptionId).ToListAsync();
            Console.WriteLine($"[Repro] 出库流水：{outs.Count} 条");
            foreach (var o in outs)
                Console.WriteLine($"[Repro]   DrugId={o.DrugId} Batch={o.BatchNo} Qty={o.Qty} IsReversal={o.IsReversal}");
            var stocks = await db.DrugStocks.Where(s => s.QtyRemaining > 0 &&
                db.PrescriptionItems.Where(i => i.PrescriptionId == prescriptionId)
                    .Select(i => i.DrugId).Contains(s.DrugId)).ToListAsync();
            foreach (var s in stocks)
                Console.WriteLine($"[Repro]   扣减后库存：DrugId={s.DrugId} Batch={s.BatchNo} 余量={s.QtyRemaining}");
        }

        provider.Dispose();
        try { File.Delete(dbPath); } catch { }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"断言失败: {message}");
    }

    private static async Task AssertThrowsAsync<T>(Func<Task> action, string expectedMessageContains)
        where T : Exception
    {
        try
        {
            await action();
        }
        catch (T ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            if (!msg.Contains(expectedMessageContains))
                throw new Exception(
                    $"异常类型正确但消息不匹配: 期望包含「{expectedMessageContains}」，实际「{msg}」");
            return;
        }
        throw new Exception($"期望抛出 {typeof(T).Name}（包含「{expectedMessageContains}」），但未抛出异常");
    }
}
