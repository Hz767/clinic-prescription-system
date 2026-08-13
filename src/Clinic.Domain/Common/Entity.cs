namespace Clinic.Domain.Common;

/// <summary>
/// 实体基类。所有领域实体继承此类。
/// 主键使用 long（SQLite rowid 自增）。
/// </summary>
public abstract class Entity
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
