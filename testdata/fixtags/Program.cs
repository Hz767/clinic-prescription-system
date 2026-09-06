// 诊所处方系统 · 正式库药品类别标签修复（F-02 配套数据修复）
// ============================================================================
// 背景：F-02 修复后，过敏检查会使用药品 ContraindicationTags 类别标签做类别匹配。
//       正式库 clinic.db 中由 DbSeeder 创建的 5 种种子药品缺少标签，导致正式库
//       中类别型过敏（青霉素类/头孢类/NSAIDs）仍无法被拦截。
// 本脚本：按 drugs.csv（合规药品目录）中同名药品的标签，更新正式库对应药品的
//         ContraindicationTags。仅修改标签列，不动其他数据。
// 运行：dotnet run --project testdata/fixtags
// ============================================================================
using System.Text;
using Clinic.Application;
using Clinic.Domain.Entities;
using Clinic.Domain.Interfaces;
using Clinic.Infrastructure;
using Clinic.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClinicFixTags;

public static class Program
{
    private static readonly string DbPath = @"E:\个人诊所处方系统\clinic.db";
    private static readonly string DataDir = @"E:\个人诊所处方系统\testdata";

    public static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("═══ 正式库药品类别标签修复 ═══\n");

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(
            DbPath,
            "JQqOAFkXsovwPnF2co89Xh3of3UXBPJ8rQxNtP3wOoo=",
            "ClinicPrescriptionPepper2026-ChangeInProduction",
            llmEnabled: false);
        services.AddApplication();
        await using var sp = services.BuildServiceProvider();

        // 读取合规目录（drugs.csv）中的药品标签映射（中文名 → 标签）
        var csvTags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(Path.Combine(DataDir, "drugs.csv"), Encoding.UTF8).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var f = ParseLine(line);
            if (f.Count < 10) continue;
            var cn = f[0].Trim();
            var tag = f[8].Trim(); // contraindication_tags 列
            if (cn.Length > 0 && tag.Length > 0)
                csvTags[cn] = tag;
        }
        Console.WriteLine($"drugs.csv 中有标签的药品：{csvTags.Count} 种");

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
            // 正式库为旧 schema：先补齐 P0-P1 阶段新增列（幂等，已存在则忽略）
            await db.Database.EnsureCreatedAsync();
            await MigrateVitalsAsync(db);
            await MigrateP0P1Async(db);

            var repo = scope.ServiceProvider.GetRequiredService<IRepository<DrugMaster>>();
            var drugs = (await repo.GetAllAsync()).ToList();

            int updated = 0, already = 0, noCsv = 0;
            foreach (var d in drugs.OrderBy(d => d.Id))
            {
                var before = d.ContraindicationTags;
                var hasBefore = !string.IsNullOrWhiteSpace(before);

                if (csvTags.TryGetValue(d.GenericNameCn, out var tag))
                {
                    if (!hasBefore)
                    {
                        d.ContraindicationTags = tag;
                        repo.Update(d);
                        updated++;
                        Console.WriteLine($"  [更新] {d.GenericNameCn}: (空) → {tag}");
                    }
                    else if (!string.Equals(before!.Trim(), tag, StringComparison.OrdinalIgnoreCase))
                    {
                        d.ContraindicationTags = tag;
                        repo.Update(d);
                        updated++;
                        Console.WriteLine($"  [覆盖] {d.GenericNameCn}: {before} → {tag}");
                    }
                    else
                    {
                        already++;
                    }
                }
                else
                {
                    noCsv++;
                    Console.WriteLine($"  [跳过] {d.GenericNameCn}: drugs.csv 无此药品标签（保留原值 {(hasBefore ? before : "空")}）");
                }
            }

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            Console.WriteLine($"\n汇总：更新 {updated} / 已正确 {already} / CSV 无对应 {noCsv} / 目录共 {drugs.Count} 种");

            // 回读验证
            var after = (await repo.GetAllAsync()).ToList();
            int stillMissing = after.Count(d => string.IsNullOrWhiteSpace(d.ContraindicationTags));
            Console.WriteLine($"验证：仍有 {stillMissing} 种药品缺标签");
            foreach (var d in after.OrderBy(d => d.Id))
                Console.WriteLine($"  {d.GenericNameCn} → {(string.IsNullOrWhiteSpace(d.ContraindicationTags) ? "（空）" : d.ContraindicationTags)}");
        }

        Console.WriteLine("\n[完成] 正式库药品标签修复结束");
        return 0;
    }

    private static async Task MigrateVitalsAsync(ClinicDbContext db)
    {
        // 实际表名/列名均为小写下划线（EF snake_case 命名约定）
        var cols = new (string Name, string Type)[]
        {
            ("weight", "TEXT"), ("temperature", "TEXT"),
            ("systolic_bp", "INTEGER"), ("diastolic_bp", "INTEGER"), ("heart_rate", "INTEGER")
        };
        foreach (var (name, type) in cols)
        {
            try { await db.Database.ExecuteSqlRawAsync($"ALTER TABLE prescription ADD COLUMN {name} {type};"); }
            catch { }
        }
    }

    private static async Task MigrateP0P1Async(ClinicDbContext db)
    {
        var migrations = new (string Table, string Column, string Type)[]
        {
            ("drug_master", "reorder_level", "REAL"),
            ("patient", "tags", "TEXT"),
            ("prescription", "override_reason", "TEXT"),
            ("prescription", "dispensed_at", "TEXT"),
            ("prescription", "dispensed_by", "INTEGER")
        };
        foreach (var (table, column, type) in migrations)
        {
            try { await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ADD COLUMN {column} {type};"); }
            catch { }
        }
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
}
