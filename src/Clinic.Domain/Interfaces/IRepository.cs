using System.Linq.Expressions;
using Clinic.Domain.Common;

namespace Clinic.Domain.Interfaces;

/// <summary>
/// 泛型仓储接口。定义在领域层，由基础设施层实现。
/// </summary>
public interface IRepository<T> where T : Entity
{
    Task<T?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Update(T entity);
    void SoftDelete(T entity);

    /// <summary>检查是否存在满足指定条件的实体（自动过滤软删除）</summary>
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    /// <summary>统计满足指定条件的实体数量（自动过滤软删除）。predicate 为 null 时统计全部</summary>
    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);
}
