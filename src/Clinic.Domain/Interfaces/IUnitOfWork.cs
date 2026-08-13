namespace Clinic.Domain.Interfaces;

/// <summary>
/// 工作单元接口。管理事务边界——处方保存、库存扣减、日志写入
/// 必须在同一个事务内完成，避免半成功状态。
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>开启一个显式事务（BEGIN IMMEDIATE）</summary>
    Task BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>提交事务</summary>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>回滚事务</summary>
    Task RollbackAsync(CancellationToken ct = default);

    /// <summary>保存未显式提交的更改</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
