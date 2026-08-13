using Clinic.Domain.Interfaces;

namespace Clinic.Infrastructure.Common;

/// <summary>
/// 系统时钟实现。返回真实时间。
/// 测试时可替换为 FixedClock 返回固定时间。
/// </summary>
public class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime Now => DateTime.Now;
}
