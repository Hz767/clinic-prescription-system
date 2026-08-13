namespace Clinic.Domain.Interfaces;

/// <summary>
/// 时钟抽象。用于可测试的时间注入。
/// 生产环境使用 SystemClock，测试环境使用 FixedClock。
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
    DateTime Now { get; }
}
