using Clinic.Domain.Entities;

namespace Clinic.Application.Interfaces;

/// <summary>
/// 备份恢复服务接口。
/// 备份：SQLite 数据库文件 → zip 压缩 → SHA-256 校验 → 记录 BackupManifest。
/// 恢复：SHA-256 校验 → 解压 → 替换数据库文件（需重启应用）。
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// 创建数据库备份。
    /// 将 SQLite 数据库文件（WAL checkpoint 后）压缩为 zip，计算 SHA-256，记录 BackupManifest。
    /// </summary>
    /// <param name="backupDir">备份输出目录</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>备份文件完整路径</returns>
    Task<string> CreateBackupAsync(string backupDir, CancellationToken ct = default);

    /// <summary>
    /// 从备份文件恢复数据库。
    /// 校验 SHA-256 → 解压 → 替换数据库文件。恢复后需重启应用。
    /// </summary>
    /// <param name="backupFilePath">备份 zip 文件路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>恢复是否成功</returns>
    Task<bool> RestoreBackupAsync(string backupFilePath, CancellationToken ct = default);

    /// <summary>
    /// 获取备份历史记录。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>备份记录列表（按时间降序）</returns>
    Task<IReadOnlyList<BackupManifest>> GetBackupHistoryAsync(CancellationToken ct = default);
}
