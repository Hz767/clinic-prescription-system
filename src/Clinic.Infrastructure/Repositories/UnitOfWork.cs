using System.Data;
using Clinic.Domain.Interfaces;
using Clinic.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Clinic.Infrastructure.Repositories;

/// <summary>
/// 工作单元实现。管理 EF Core 事务边界。
/// 处方保存、库存扣减、日志写入打包在同一个事务内。
/// SQLite 连接已在 DependencyInjection 中配置 PRAGMA busy_timeout=5000 以避免死锁。
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly ClinicDbContext _context;
    private IDbContextTransaction? _transaction;
    private readonly object _transactionLock = new();

    public UnitOfWork(ClinicDbContext context)
    {
        _context = context;
    }

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        // 重复 BeginTransaction 保护：不支持嵌套事务
        if (_transaction is not null)
            throw new InvalidOperationException("事务已存在，不支持嵌套事务");

        // P0 修复：使用 BEGIN IMMEDIATE 事务（IsolationLevel.Serializable 在 SQLite 提供程序中
        // 映射为 BEGIN IMMEDIATE），在事务一开始就获取保留写锁。配合 DependencyInjection 中
        // 配置的 PRAGMA busy_timeout=5000，避免多个延迟事务并发升级写锁时发生死锁/写冲突。
        _transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("No active transaction to commit.");

        try
        {
            await _context.SaveChangesAsync(ct);
            await _transaction.CommitAsync(ct);
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            return;

        await _transaction.RollbackAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        GC.SuppressFinalize(this);
    }
}
