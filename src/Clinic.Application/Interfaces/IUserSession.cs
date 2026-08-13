using Clinic.Shared.Enums;

namespace Clinic.Application.Interfaces;

/// <summary>
/// 用户会话状态接口。Singleton 生命周期，跨操作范围保持登录状态。
/// AuthService 写入，其他服务和 ViewModel 读取。
/// </summary>
public interface IUserSession
{
    bool IsAuthenticated { get; }
    long? UserId { get; }
    string? UserName { get; }
    string? DisplayName { get; }
    UserRole? Role { get; }

    /// <summary>当前用户是否需要强制修改密码（登录时从 SysUser.MustChangePassword 加载）</summary>
    bool MustChangePassword { get; }

    void SetAuthenticated(long userId, string username, string displayName, UserRole role, bool mustChangePassword = false);
    void Clear();
}
