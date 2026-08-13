using System.Linq.Expressions;
using Clinic.Domain.Common;
using Clinic.Domain.Interfaces;
using Clinic.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Infrastructure.Repositories;

/// <summary>
/// 泛型仓储实现。基于 EF Core DbContext。
/// 自动过滤软删除记录（DeletedAt == null）。
/// 使用 IClock 注入时间，便于单元测试。
/// </summary>
public class Repository<T> : IRepository<T> where T : Entity
{
    private readonly ClinicDbContext _context;
    private readonly DbSet<T> _dbSet;
    private readonly IClock _clock;

    public Repository(ClinicDbContext context, IClock clock)
    {
        _context = context;
        _dbSet = context.Set<T>();
        _clock = clock;
    }

    public async Task<T?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(e => e.DeletedAt == null)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<IReadOnlyList<T>> FindAsync(
        Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(e => e.DeletedAt == null)
            .Where(predicate)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
    {
        return await _dbSet
            .Where(e => e.DeletedAt == null)
            .ToListAsync(ct);
    }

    public async Task AddAsync(T entity, CancellationToken ct = default)
    {
        entity.CreatedAt = _clock.UtcNow;
        await _dbSet.AddAsync(entity, ct);
    }

    public void Update(T entity)
    {
        _dbSet.Update(entity);
    }

    public void SoftDelete(T entity)
    {
        entity.DeletedAt = _clock.UtcNow;
        _dbSet.Update(entity);
    }

    /// <summary>
    /// 检查是否存在满足指定条件的实体（自动过滤软删除）。
    /// 使用 AnyAsync，比 FirstAsync + Count 更高效。
    /// </summary>
    public async Task<bool> ExistsAsync(
        Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        return await _dbSet
            .Where(e => e.DeletedAt == null)
            .AnyAsync(predicate, ct);
    }

    /// <summary>
    /// 统计满足指定条件的实体数量（自动过滤软删除）。
    /// predicate 为 null 时统计全部未删除记录。
    /// </summary>
    public async Task<int> CountAsync(
        Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
    {
        var query = _dbSet.Where(e => e.DeletedAt == null);
        if (predicate is not null)
            query = query.Where(predicate);
        return await query.CountAsync(ct);
    }
}
