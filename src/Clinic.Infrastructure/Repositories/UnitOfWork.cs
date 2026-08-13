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

        // 使用 EF Core API 开启事务。
        // SQLite EF Core 默认使用 DEFERRED 事务，配合 DependencyInjection 中
        // 配置的 PRAGMA busy_timeout=5000 可有效避免 writer 饥饿和死锁。
        // 如需强制 IMMEDIATE 事务，可在连接初始化时设置 PRAGMA，或在此处
        // 通过 ExecuteSqlRawAsync("BEGIN IMMEDIATE TRANSACTION") 手动开启。
        _transaction = await _context.Database.BeginTransactionAsync(ct);
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
