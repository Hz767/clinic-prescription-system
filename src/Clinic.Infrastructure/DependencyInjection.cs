using System.Security.Cryptography;
using System.Text;
using Clinic.Application.DTOs;
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
        string? secretsDir,
        bool llmEnabled = false,
        string? llmEndpoint = null,
        string? llmModel = null,
        LlamaCppSettings? llamaCppSettings = null)
    {
        // ── 解析 AES-256 加密密钥 ──
        // 解析优先级（严禁把密钥硬编码进 appsettings.json 提交版本库）：
        //   1. 环境变量 CLINIC_ENCRYPTION_KEY / 配置提供的 Base64 或字符串
        //   2. secrets 目录中的 encryption.key 文件（已 gitignore）
        //   3. 首次运行时随机生成并持久化到密钥文件
        var encryptionKey = ResolveEncryptionKey(encryptionKeyBase64, secretsDir);

        // ── 解析密码哈希 Pepper ──
        // 优先级：环境变量 CLINIC_ENCRYPTION_PEPPER > 配置字符串 > pepper.key 文件 > 生成。
        // 注意：Pepper 轮换会使已有密码哈希失效，迁移时务必保留原值。
        var resolvedPepper = ResolvePepper(pepper, secretsDir);

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
                resolvedPepper,
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

        // ── 国内药品清单数据源（Scoped：无状态，HttpClient 为进程级单例）──
        services.AddScoped<INationalDrugListSource, NationalDrugListSource>();

        // ── llama.cpp 本地推理引擎管理 ──
        // 注册 LlamaCppSettings 供 ILlamaServerManager 使用
        if (llamaCppSettings is { Enabled: true })
        {
            services.AddSingleton(llamaCppSettings);
            services.AddSingleton<ILlamaServerManager, LlamaServerProcessManager>();
        }

        // ── LLM 服务（Singleton：无状态，HTTP 客户端可复用）──
        // 优先级：llama.cpp > Ollama > NoOp
        if (llamaCppSettings is { Enabled: true })
        {
            var endpoint = $"http://{llamaCppSettings.Host}:{llamaCppSettings.Port}";
            var model = Path.GetFileNameWithoutExtension(llamaCppSettings.ModelPath);
            services.AddSingleton<ILlmService>(sp =>
            {
                var logger = sp.GetService<ILogger<LlamaCppLlmService>>();
                return new LlamaCppLlmService(endpoint, model, logger);
            });
        }
        else if (llmEnabled && !string.IsNullOrWhiteSpace(llmEndpoint) && !string.IsNullOrWhiteSpace(llmModel))
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
    /// 解析优先级（严禁把密钥硬编码进 appsettings.json 提交版本库）：
    ///   1. 环境变量 CLINIC_ENCRYPTION_KEY（Base64 32 字节）覆盖配置
    ///   2. 配置提供的 Base64 编码 32 字节密钥（直接使用）
    ///   3. 配置提供的非 Base64 字符串（SHA256 哈希后取 32 字节，向后兼容）
    ///   4. secrets 目录/基目录中的 encryption.key 文件（已 gitignore）
    ///   5. 首次运行时随机生成并持久化到密钥文件
    /// </summary>
    private static byte[] ResolveEncryptionKey(string? configuredKey, string? secretsDir)
    {
        // 1. 环境变量优先注入，避免密钥进入版本库
        var envKey = Environment.GetEnvironmentVariable("CLINIC_ENCRYPTION_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
            configuredKey = envKey;

        // 2/3. 配置提供的密钥
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

            // 向后兼容：将字符串 SHA256 哈希为 32 字节密钥
            return SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
        }

        // 4. 从外部密钥文件读取（环境变量/文件不受版本库控制）
        foreach (var dir in SecretCandidateDirs(secretsDir))
        {
            var keyPath = Path.Combine(dir, "encryption.key");
            if (!File.Exists(keyPath))
                continue;

            var existing = ReadKeyFile(keyPath);
            if (existing is not null)
                return existing;
        }

        // 5. 生成新密钥并持久化到可写目录
        var newKey = RandomNumberGenerator.GetBytes(AesKeySize);
        var persistDir = SecretCandidateDirs(secretsDir).FirstOrDefault(Directory.Exists)
                         ?? SecretCandidateDirs(secretsDir).First();
        try
        {
            Directory.CreateDirectory(persistDir);
            File.WriteAllBytes(Path.Combine(persistDir, "encryption.key"), newKey);
            Console.WriteLine(
                $"[Security] 已自动生成 AES-256 加密密钥并保存至 {Path.Combine(persistDir, "encryption.key")}。");
        }
        catch
        {
            // 无法持久化时使用内存密钥（重启后失效，仅限开发调试）
            Console.WriteLine(
                "[Security] 警告：无法持久化加密密钥文件，每次启动将使用不同密钥，" +
                "已加密的数据将无法解密。请配置 CLINIC_ENCRYPTION_KEY 环境变量或加密密钥文件。");
        }
        return newKey;
    }

    /// <summary>解析密码哈希 Pepper。优先级：环境变量 &gt; 配置字符串 &gt; pepper.key 文件 &gt; 生成。</summary>
    private static string ResolvePepper(string? configuredPepper, string? secretsDir)
    {
        var envPepper = Environment.GetEnvironmentVariable("CLINIC_ENCRYPTION_PEPPER");
        if (!string.IsNullOrWhiteSpace(envPepper))
            return envPepper;

        if (!string.IsNullOrWhiteSpace(configuredPepper))
            return configuredPepper;

        foreach (var dir in SecretCandidateDirs(secretsDir))
        {
            var path = Path.Combine(dir, "pepper.key");
            if (File.Exists(path))
            {
                var value = File.ReadAllText(path).Trim();
                if (value.Length > 0)
                    return value;
            }
        }

        // 兜底：生成随机 Pepper。注意：仅在全新安装（无既有用户哈希）时安全。
        var newPepper = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var persistDir = SecretCandidateDirs(secretsDir).FirstOrDefault(Directory.Exists)
                         ?? SecretCandidateDirs(secretsDir).First();
        try
        {
            Directory.CreateDirectory(persistDir);
            File.WriteAllText(Path.Combine(persistDir, "pepper.key"), newPepper);
            Console.WriteLine($"[Security] 已自动生成 Pepper 并保存至 {Path.Combine(persistDir, "pepper.key")}。");
        }
        catch
        {
            Console.WriteLine("[Security] 警告：无法持久化 Pepper，仅本次运行有效。");
        }
        return newPepper;
    }

    /// <summary>密钥文件候选目录：优先外部 secrets 目录，其次应用基目录。</summary>
    private static IReadOnlyList<string> SecretCandidateDirs(string? secretsDir)
    {
        var dirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(secretsDir))
            dirs.Add(secretsDir);
        if (!dirs.Contains(AppContext.BaseDirectory))
            dirs.Add(AppContext.BaseDirectory);
        return dirs;
    }

    /// <summary>读取密钥文件，兼容原始 32 字节与 Base64 文本两种格式，失败返回 null。</summary>
    private static byte[]? ReadKeyFile(string keyPath)
    {
        try
        {
            var raw = File.ReadAllBytes(keyPath);
            if (raw.Length == AesKeySize)
                return raw;

            var text = Encoding.UTF8.GetString(raw).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return null;
            var decoded = Convert.FromBase64String(text);
            if (decoded.Length == AesKeySize)
                return decoded;
        }
        catch
        {
            // 文件被占用或格式非法时忽略
        }
        return null;
    }
}
