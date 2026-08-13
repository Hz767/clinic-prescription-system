using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clinic.Application.Interfaces;
using Clinic.Domain.Entities;
using Clinic.Domain.Interfaces;
using Microsoft.Data.Sqlite;

namespace Clinic.Infrastructure.Backup;

/// <summary>
/// 备份恢复服务实现。
/// 备份流程：WAL checkpoint → 复制数据库文件 → zip 压缩 → SHA-256 校验 → 记录 BackupManifest。
/// 恢复流程：SHA-256 校验 → 解压 → 写入 pending-restore 标记文件 → 重启后替换数据库。
/// 依赖：dbPath（数据库路径）、IRepository&lt;BackupManifest&gt;（Scoped）、IUnitOfWork（Scoped）、IClock（Singleton）、IAuditService（Scoped）
/// </summary>
public class BackupService : IBackupService
{
    private readonly string _dbPath;
    private readonly IRepository<BackupManifest> _backupRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IAuditService _auditService;

    /// <summary>待恢复标记文件名（放在数据库同目录）</summary>
    private const string PendingRestoreFlag = ".pending-restore";

    public BackupService(
        string dbPath,
        IRepository<BackupManifest> backupRepo,
        IUnitOfWork unitOfWork,
        IClock clock,
        IAuditService auditService)
    {
        _dbPath = dbPath;
        _backupRepo = backupRepo;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _auditService = auditService;
    }

    public async Task<string> CreateBackupAsync(string backupDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(backupDir);

        var now = _clock.Now;
        var timestamp = now.ToString("yyyyMMdd_HHmmss");
        var zipPath = Path.Combine(backupDir, $"backup_{timestamp}.zip");

        // 1. WAL checkpoint：将 WAL 日志写入主数据库文件
        await WalCheckpointAsync(ct);

        // 2. 收集要备份的文件（数据库主文件 + WAL/SHM 如果存在）
        var dbDir = Path.GetDirectoryName(_dbPath) ?? ".";
        var dbFileName = Path.GetFileName(_dbPath);
        var filesToBackup = new List<string> { dbFileName };

        var walPath = _dbPath + "-wal";
        var shmPath = _dbPath + "-shm";
        if (File.Exists(walPath)) filesToBackup.Add(dbFileName + "-wal");
        if (File.Exists(shmPath)) filesToBackup.Add(dbFileName + "-shm");

        // 3. 创建 zip 文件
        using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var fileName in filesToBackup)
            {
                var sourcePath = Path.Combine(dbDir, fileName);
                if (File.Exists(sourcePath))
                {
                    var entry = zip.CreateEntry(fileName, CompressionLevel.Optimal);
                    entry.LastWriteTime = now;
                    using var entryStream = entry.Open();
                    using var fileStream = File.OpenRead(sourcePath);
                    await fileStream.CopyToAsync(entryStream, ct);
                }
            }
        }

        // 4. 计算 SHA-256
        var sha256 = await ComputeSha256Async(zipPath, ct);

        // 5. 记录 BackupManifest
        var fileListJson = JsonSerializer.Serialize(filesToBackup);
        var fileInfo = new FileInfo(zipPath);

        var manifest = new BackupManifest
        {
            TakenAt = now,
            SourceDir = backupDir,
            Sha256 = sha256,
            FileListJson = fileListJson,
            SizeBytes = fileInfo.Length
        };

        await _backupRepo.AddAsync(manifest, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        // 6. 审计日志
        try
        {
            await _auditService.LogSystemAsync("BACKUP_CREATE",
                zipPath, $"SHA256:{sha256} Size:{fileInfo.Length}", fileListJson, ct);
        }
        catch
        {
            // 审计失败不影响备份
        }

        return zipPath;
    }

    public async Task<bool> RestoreBackupAsync(string backupFilePath, CancellationToken ct = default)
    {
        if (!File.Exists(backupFilePath))
            throw new InvalidOperationException("备份文件不存在");

        // 1. 校验 SHA-256
        var actualSha256 = await ComputeSha256Async(backupFilePath, ct);

        // 查找对应的 BackupManifest 记录
        var manifests = await _backupRepo.GetAllAsync(ct);
        var manifest = manifests
            .Where(m => m.Sha256 == actualSha256)
            .OrderByDescending(m => m.TakenAt)
            .FirstOrDefault();

        if (manifest is null)
            throw new InvalidOperationException(
                $"备份文件 SHA-256 校验失败，未找到匹配的备份记录（SHA-256: {actualSha256}）");

        // 2. 解压到临时目录
        var tempDir = Path.Combine(Path.GetTempPath(), $"clinic_restore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using (var fs = File.OpenRead(backupFilePath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                zip.ExtractToDirectory(tempDir, true);
            }

            // 3. 验证解压后的文件列表
            var expectedFiles = JsonSerializer.Deserialize<List<string>>(manifest.FileListJson);
            if (expectedFiles is not null)
            {
                foreach (var fileName in expectedFiles)
                {
                    if (!File.Exists(Path.Combine(tempDir, fileName)))
                        throw new InvalidOperationException($"备份文件缺失：{fileName}");
                }
            }

            // 4. 写入 pending-restore 标记文件
            var dbDir = Path.GetDirectoryName(_dbPath) ?? ".";
            var flagPath = Path.Combine(dbDir, PendingRestoreFlag);
            await File.WriteAllTextAsync(flagPath,
                $"{tempDir}|{Path.GetFileName(_dbPath)}", Encoding.UTF8, ct);

            // 5. 审计日志
            try
            {
                await _auditService.LogSystemAsync("BACKUP_RESTORE",
                    backupFilePath, $"SHA256:{actualSha256}", null, ct);
            }
            catch
            {
                // 审计失败不影响恢复
            }

            return true;
        }
        catch
        {
            // 清理临时目录
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            throw;
        }
    }

    public async Task<IReadOnlyList<BackupManifest>> GetBackupHistoryAsync(CancellationToken ct = default)
    {
        var all = await _backupRepo.GetAllAsync(ct);
        return all.OrderByDescending(b => b.TakenAt).ToList();
    }

    /// <summary>
    /// 执行 WAL checkpoint，将 WAL 日志写入主数据库文件。
    /// 使用独立连接执行，不影响当前操作范围的事务。
    /// </summary>
    private async Task WalCheckpointAsync(CancellationToken ct)
    {
        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>计算文件的 SHA-256（Hex 格式，64 字符）</summary>
    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        using var stream = File.OpenRead(filePath);
        var bytes = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// 检查并执行待恢复操作（应用启动时调用）。
    /// 如果存在 pending-restore 标记文件，将备份的数据库文件替换当前数据库。
    /// </summary>
    public static bool TryExecutePendingRestore(string dbPath)
    {
        var dbDir = Path.GetDirectoryName(dbPath) ?? ".";
        var flagPath = Path.Combine(dbDir, PendingRestoreFlag);

        if (!File.Exists(flagPath))
            return false;

        try
        {
            var content = File.ReadAllText(flagPath);
            var parts = content.Split('|');
            if (parts.Length < 2)
            {
                File.Delete(flagPath);
                return false;
            }

            var tempDir = parts[0];
            var dbFileName = parts[1];

            // 替换数据库文件
            var sourceDbPath = Path.Combine(tempDir, dbFileName);
            if (!File.Exists(sourceDbPath))
            {
                File.Delete(flagPath);
                return false;
            }

            // 删除 WAL/SHM 文件（恢复时需要干净的数据库）
            var walPath = dbPath + "-wal";
            var shmPath = dbPath + "-shm";
            if (File.Exists(walPath)) File.Delete(walPath);
            if (File.Exists(shmPath)) File.Delete(shmPath);

            // 复制数据库文件
            File.Copy(sourceDbPath, dbPath, overwrite: true);

            // 清理
            File.Delete(flagPath);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
