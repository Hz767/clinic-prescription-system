using Clinic.Application;
using Clinic.Application.Interfaces;
using Clinic.Application.Session;
using Clinic.Application.Validators;
using Clinic.Domain.Entities;
using Clinic.Domain.Interfaces;
using Clinic.Infrastructure.Data;
using Clinic.Infrastructure.Repositories;
using Clinic.Infrastructure.Security;
using Clinic.Infrastructure.Encryption;
using Clinic.Infrastructure.Common;
using Clinic.Infrastructure.Pdf;
using Clinic.Infrastructure.Llm;
using Clinic.Shared.Enums;
using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.IntegrationTests;

/// <summary>
/// 测试宿主：为集成测试提供完整的 DI 容器，使用临时 SQLite 文件数据库。
/// 每个测试案例创建独立的 TestHost 实例，确保测试间数据隔离。
/// </summary>
public class TestHost : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly string _dbPath;

    public long DoctorId { get; private set; }
    public long[] DrugIds { get; private set; } = [];

    /// <summary>初始化测试宿主：创建临时数据库、注册所有服务、播种测试数据</summary>
    public TestHost()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"clinic_test_{Guid.NewGuid():N}.db");
        if (File.Exists(_dbPath)) File.Delete(_dbPath);

        var services = new ServiceCollection();

        // SQLite 连接（Scoped）
        services.AddScoped(_ =>
        {
            var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; " +
                "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();
            return conn;
        });

        services.AddDbContext<ClinicDbContext>((sp, options) =>
        {
            options.UseSqlite(sp.GetRequiredService<SqliteConnection>());
        });

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // 安全服务（固定测试密钥）
        var testKey = new byte[32];
        services.AddSingleton<IEncryptionService>(_ => new AesGcmEncryptionService(testKey));
        services.AddSingleton<IPasswordHasher>(_ => new Pbkdf2PasswordHasher("test-pepper", null));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPdfService, QuestPdfService>();
        services.AddSingleton<ILlmService, NoOpLlmService>();

        services.AddApplication();

        _provider = services.BuildServiceProvider();
        Task.Run(InitializeAsync).GetAwaiter().GetResult();
    }

    public T GetService<T>() where T : notnull => _provider.GetRequiredService<T>();
    public IServiceScope CreateScope() => _provider.CreateScope();

    /// <summary>登录为测试医生</summary>
    public void LoginAsDoctor()
    {
        var session = _provider.GetRequiredService<IUserSession>();
        session.SetAuthenticated(DoctorId, "testdoctor", "测试医生", UserRole.Doctor);
    }

    /// <summary>在新的服务范围内执行操作</summary>
    public async Task ExecuteInScopeAsync(Func<IServiceProvider, Task> action)
    {
        using var scope = _provider.CreateScope();
        await action(scope.ServiceProvider);
    }

    public async Task<T> ExecuteInScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _provider.CreateScope();
        return await action(scope.ServiceProvider);
    }

    /// <summary>初始化数据库并播种测试数据</summary>
    private async Task InitializeAsync()
    {
        using var scope = _provider.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ClinicDbContext>();
        await db.Database.EnsureCreatedAsync();

        var userRepo = sp.GetRequiredService<IRepository<SysUser>>();
        var passwordHasher = sp.GetRequiredService<IPasswordHasher>();
        var uow = sp.GetRequiredService<IUnitOfWork>();

        // 创建测试医生
        var doctor = new SysUser
        {
            Username = "testdoctor", DisplayName = "测试医生",
            Role = UserRole.Doctor, PasswordHash = passwordHasher.Hash("Test@1234"),
            IsActive = true
        };
        await userRepo.AddAsync(doctor);
        await uow.SaveChangesAsync();
        DoctorId = doctor.Id;

        // 创建测试药品
        var drugRepo = sp.GetRequiredService<IRepository<DrugMaster>>();
        var stockRepo = sp.GetRequiredService<IRepository<DrugStock>>();

        var drugs = new[]
        {
            new DrugMaster { GenericNameCn = "阿莫西林胶囊", GenericNameEn = "Amoxicillin", Spec = "0.25g*24粒", Unit = "粒", IsAntibiotic = true, AntibioticLevel = AntibioticLevel.NonRestricted, RetailPriceRef = 0.8m, ContraindicationTags = "青霉素过敏" },
            new DrugMaster { GenericNameCn = "布洛芬片", GenericNameEn = "Ibuprofen", Spec = "0.2g*20片", Unit = "片", RetailPriceRef = 0.5m },
            new DrugMaster { GenericNameCn = "复方甘草片", GenericNameEn = "Glycyrrhiza", Spec = "100片", Unit = "片", RetailPriceRef = 0.15m },
            new DrugMaster { GenericNameCn = "板蓝根颗粒", GenericNameEn = "Radix Isatidis", Spec = "10g*20袋", Unit = "袋", RetailPriceRef = 1.2m },
            new DrugMaster { GenericNameCn = "蒙脱石散", GenericNameEn = "Smectite", Spec = "3g*15袋", Unit = "袋", RetailPriceRef = 2.5m },
            new DrugMaster { GenericNameCn = "头孢克肟分散片", GenericNameEn = "Cefixime", Spec = "0.1g*6片", Unit = "片", IsAntibiotic = true, AntibioticLevel = AntibioticLevel.Restricted, RetailPriceRef = 3.0m },
            new DrugMaster { GenericNameCn = "对乙酰氨基酚片", GenericNameEn = "Acetaminophen", Spec = "0.5g*10片", Unit = "片", RetailPriceRef = 0.3m }
        };

        foreach (var drug in drugs)
            await drugRepo.AddAsync(drug);
        await uow.SaveChangesAsync();
        DrugIds = drugs.Select(d => d.Id).ToArray();

        // 为药品创建库存（每药100单位）
        var now = DateTime.UtcNow;
        foreach (var drug in drugs)
        {
            await stockRepo.AddAsync(new DrugStock
            {
                DrugId = drug.Id, BatchNo = $"BATCH-{drug.Id:D3}",
                ExpiryDate = DateOnly.FromDateTime(now.AddDays(365)),
                QtyRemaining = 100, CostPrice = 0.5m, ReceivedAt = now
            });
        }
        await uow.SaveChangesAsync();

        // 药物交互数据（阿莫西林 + 布洛芬 = Moderate）
        var interactionRepo = sp.GetRequiredService<IRepository<DrugInteraction>>();
        await interactionRepo.AddAsync(new DrugInteraction
        {
            DdinterIdA = 1, DrugNameA = "阿莫西林",
            DdinterIdB = 2, DrugNameB = "布洛芬",
            Level = DrugInteractionLevel.Moderate, ImportedAt = now
        });
        await uow.SaveChangesAsync();
    }

    public void Dispose()
    {
        _provider.Dispose();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            foreach (var ext in new[] { "-wal", "-shm" })
            {
                var f = _dbPath + ext;
                if (File.Exists(f)) File.Delete(f);
            }
        }
        catch { }
    }
}
