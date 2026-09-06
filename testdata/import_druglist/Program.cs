// 药品库存管理 · 添加国内药品清单（导入驱动）
// ============================================================================
// 目的：模拟用户在「库存管理 → 库存概览 → 添加国内药品清单」按钮上的操作，
//       走系统业务入口（IInventoryService.ImportNationalDrugListAsync）导入正式库，
//       不直接写 SQL。数据源：本地已从 GitHub 下载的《药品清单_完整.csv》
//       （原始来源 https://raw.githubusercontent.com/lrpopeyou/MedicalInsuranceKG/master/药品信息.csv）。
// 范围：默认导入「国家目录」（scope=0，与界面默认一致）；可传参 "all" 导入全国含省增补。
// 运行：dotnet run --project testdata/import_druglist [all]
// ============================================================================
using System.Text;
using Clinic.Application;
using Clinic.Application.Interfaces;
using Clinic.Infrastructure;
using Clinic.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ImportDrugList;

public static class Program
{
    private const string DbPath = @"E:\个人诊所处方系统\clinic.db";
    private const string EncryptionKey = "JQqOAFkXsovwPnF2co89Xh3of3UXBPJ8rQxNtP3wOoo=";
    private const string Pepper = "ClinicPrescriptionPepper2026-ChangeInProduction";
    private const string LocalCsv = @"E:\个人诊所处方系统\testdata\药品清单\药品清单_完整.csv";

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var scopeAll = args.Length > 0 && args[0].Equals("all", StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"═══ 药品库存管理 · 添加国内药品清单（{(scopeAll ? "全国含省增补" : "国家目录")}）═══\n");

        if (!File.Exists(LocalCsv))
        {
            Console.WriteLine($"本地清单文件不存在：{LocalCsv}");
            return 1;
        }

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(DbPath, EncryptionKey, Pepper, llmEnabled: false);
        services.AddApplication();
        await using var sp = services.BuildServiceProvider();

        using (var scope = sp.CreateScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            var ok = await auth.LoginAsync("admin", "admin123");
            if (!ok)
            {
                Console.WriteLine("登录失败（admin/admin123），无法获得入库权限");
                return 1;
            }
            Console.WriteLine($"当前操作人：{auth.CurrentUserName}（UserId={auth.CurrentUserId}）");
        }

        // 1. 读取本地 CSV 并解析（与 GitHub 下载后的解析逻辑完全一致）
        var csvText = await File.ReadAllTextAsync(LocalCsv, Encoding.UTF8);
        var all = NationalDrugListSource.ParseCsv(csvText);
        Console.WriteLine($"本地清单解析完成：{all.Count} 条（国家 {all.Count(e => e.MedPlc == "国家")} + 各省增补 {all.Count(e => e.MedPlc != "国家")}）");

        // 2. 范围过滤（与界面 ImportScope 一致：0=国家目录，1=全国）
        var entries = scopeAll ? all : all.Where(e => e.MedPlc == "国家").ToList();
        Console.WriteLine($"本次导入范围：{(scopeAll ? "全国含省增补" : "国家目录")}（{entries.Count} 条）");

        // 3. 走业务入口导入
        using (var scope = sp.CreateScope())
        {
            var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var result = await inv.ImportNationalDrugListAsync(entries);

            Console.WriteLine("\n═══ 导入结果 ═══");
            Console.WriteLine($"  清单总条目：{result.Total}");
            Console.WriteLine($"  新增药品：{result.Imported} 种");
            Console.WriteLine($"  跳过已存在（通用名相同）：{result.SkippedExisting} 条");
            Console.WriteLine($"  跳过无效行：{result.SkippedInvalid} 条");
            Console.WriteLine($"  其中抗菌药物：{result.AntibioticCount} 种（特殊/限制使用级 {result.RestrictedCount} 种）");
            if (result.SampleErrors.Count > 0)
            {
                Console.WriteLine($"  无效行示例：{string.Join("；", result.SampleErrors.Take(5))}");
            }
            Console.WriteLine(result.Imported > 0 ? "\n[导入完成]" : "\n[无可新增药品]");
            return result.Imported > 0 ? 0 : 2;
        }
    }
}
