using System.Security.Cryptography;
using System.Text;
using Clinic.Application.Interfaces;
using Clinic.Domain.Entities;
using Clinic.Domain.Interfaces;
using Clinic.Infrastructure.Backup;
using Clinic.Infrastructure.Common;
using Clinic.Infrastructure.Encryption;
using Clinic.Infrastructure.Llm;
using Clinic.Infrastructure.Pdf;
using Clinic.Infrastructure.Repositories;
using Clinic.Infrastructure.Security;
using Clinic.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clinic.Infrastructure;

/// <summary>
/// 基础设施层 DI 注册扩展方法。
/// 在 App.xaml.cs 中通过 services.AddInfrastructure(dbPath, encryptionKey, pepper) 调用。
/// </summary>
public static class DependencyInjection
{
    private const int AesKeySize = 32; // AES-256

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string dbPath,
        string? encryptionKeyBase64,
        string? pepper,
        bool llmEnabled = false,
        string? llmEndpoint = null,
        string? llmModel = null)
    {
        // ── 解析 AES-256 加密密钥 ──
        // 优先从配置读取 Base64 编码的 32 字节密钥；
        // 若配置缺失则从 encryption.key 文件读取，文件不存在时随机生成并持久化。
        var encryptionKey = ResolveEncryptionKey(encryptionKeyBase64);

        // ── SQLite 连接（Scoped，每个操作范围共享一个连接）──
        // 设置 WAL 模式 + synchronous=NORMAL + 外键约束 + busy_timeout
        // busy_timeout=5000：写入冲突时等待 5 秒再报 SQLITE_BUSY，避免死锁
        services.AddScoped(sp =>
        {
            var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "PRAGMA journal_mode=WAL; " +
                "PRAGMA synchronous=NORMAL; " +
                "PRAGMA foreign_keys=ON; " +
                "PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();
            return conn;
        });

        // ── EF Core DbContext ──
        // 共享同一个 Scoped SqliteConnection，确保 PRAGMA 生效
        services.AddDbContext<ClinicDbContext>((sp, options) =>
        {
            var conn = sp.GetRequiredService<SqliteConnection>();
            options.UseSqlite(conn);
        });

        // ── 泛型仓储 ──
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        // ── 工作单元（事务管理）──
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // ── 安全服务（Singleton：无状态）──
        services.AddSingleton<IEncryptionService>(
            _ => new AesGcmEncryptionService(encryptionKey));
        services.AddSingleton<IPasswordHasher>(sp =>
            new Pbkdf2PasswordHasher(
                pepper,
                sp.GetService<ILogger<Pbkdf2PasswordHasher>>()));

        // ── 系统时钟 ──
        services.AddSingleton<IClock, SystemClock>();

        // ── PDF 生成服务（Singleton：无状态，QuestPDF 内部线程安全）──
        services.AddSingleton<IPdfService, QuestPdfService>();

        // ── 备份恢复服务（Scoped：依赖 IRepository + IUnitOfWork）──
        services.AddScoped<IBackupService>(sp => new BackupService(
            dbPath,
            sp.GetRequiredService<IRepository<BackupManifest>>(),
            sp.GetRequiredService<IUnitOfWork>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IAuditService>()));

        // ── LLM 服务（Singleton：无状态，HTTP 客户端可复用）──
        if (llmEnabled && !string.IsNullOrWhiteSpace(llmEndpoint) && !string.IsNullOrWhiteSpace(llmModel))
        {
            services.AddSingleton<ILlmService>(sp =>
            {
                var logger = sp.GetService<ILogger<OllamaLlmService>>();
                return new OllamaLlmService(llmEndpoint, llmModel, logger);
            });
        }
        else
        {
            services.AddSingleton<ILlmService, NoOpLlmService>();
        }

        return services;
    }

    /// <summary>
    /// 解析 AES-256 加密密钥。
    /// 解析优先级：
    ///   1. 配置提供的 Base64 编码 32 字节密钥（直接使用）
    ///   2. 配置提供的非 Base64 字符串（SHA256 哈希后取 32 字节，向后兼容）
    ///   3. encryption.key 文件（首次运行时随机生成并持久化）
    /// </summary>
    private static byte[] ResolveEncryptionKey(string? configuredKey)
    {
        // 1. 尝试从配置读取 Base64 密钥
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            try
            {
                var key = Convert.FromBase64String(configuredKey);
                if (key.Length == AesKeySize)
                    return key;
            }
            catch
            {
                // 非 Base64 格式，走 SHA256 派生
            }

            // 2. 向后兼容：将字符串 SHA256 哈希为 32 字节密钥
            return SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
        }

        // 3. 配置缺失：从密钥文件读取，或随机生成并持久化
        var keyPath = Path.Combine(AppContext.BaseDirectory, "encryption.key");
        if (File.Exists(keyPath))
        {
            var existing = File.ReadAllBytes(keyPath);
            if (existing.Length == AesKeySize)
                return existing;
        }

        var newKey = RandomNumberGenerator.GetBytes(AesKeySize);
        try
        {
            File.WriteAllBytes(keyPath, newKey);
            Console.WriteLine(
                $"[Security] 已自动生成 AES-256 加密密钥并保存至 {keyPath}。" +
                "生产环境请通过 appsettings.json 的 Encryption:EncryptionKey 配置固定密钥。");
        }
        catch
        {
            // 无法持久化时使用内存密钥（重启后失效，仅限开发调试）
            Console.WriteLine(
                "[Security] 警告：无法持久化加密密钥文件，每次启动将使用不同密钥，" +
                "已加密的数据将无法解密。请配置 Encryption:EncryptionKey。");
        }
        return newKey;
    }
}
