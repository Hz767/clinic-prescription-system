using Clinic.Application.DTOs;
using Clinic.Domain.Entities;

namespace Clinic.Application.Interfaces;

/// <summary>
/// 审计日志服务接口。
/// AuditLog：业务关键操作（登录、处方、收费、入库等），使用 SHA-256 哈希链保证防篡改。
/// SystemLog：系统级事件（启动、备份、异常等），无哈希链。
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// 记录审计日志（带 SHA-256 哈希链）。
    /// 自动获取当前用户 ID 和时间戳，计算 PayloadHash 并链接到前一条记录。
    /// </summary>
    /// <param name="action">操作类型（如 "LOGIN"、"PRESCRIPTION_SAVE"、"PAYMENT_RECORD"）</param>
    /// <param name="target">操作目标（如 "Prescription:2026-00001"）</param>
    /// <param name="payload">操作载荷 JSON（可选，用于哈希计算）</param>
    /// <param name="ct">取消令牌</param>
    Task LogAsync(string action, string? target, string? payload = null, CancellationToken ct = default);

    /// <summary>
    /// 记录系统日志（无哈希链）。
    /// 用于系统级事件：应用启动、备份恢复、异常记录等。
    /// </summary>
    /// <param name="action">操作类型（如 "APP_START"、"BACKUP_CREATE"）</param>
    /// <param name="target">操作目标（可选）</param>
    /// <param name="note">备注信息</param>
    /// <param name="payloadJson">载荷 JSON（可选）</param>
    /// <param name="ct">取消令牌</param>
    Task LogSystemAsync(string action, string? target, string? note, string? payloadJson = null, CancellationToken ct = default);

    /// <summary>
    /// 验证审计日志哈希链的完整性。
    /// 逐条重算 PayloadHash 和 HashChain，与存储值比对，检测篡改。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>验证结果（是否通过、总记录数、验证通过数、首个断裂点 ID、错误信息）</returns>
    Task<HashChainVerificationResult> VerifyChainAsync(CancellationToken ct = default);
}
