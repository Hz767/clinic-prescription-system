using Clinic.Application.Interfaces;
using Clinic.Shared.Enums;

namespace Clinic.Application.Session;

/// <summary>
/// 用户会话状态实现。Singleton 生命周期。
/// 在 WPF 单用户桌面应用中，整个应用共享一个会话。
/// </summary>
public sealed class UserSession : IUserSession
{
    private long? _userId;
    private string? _userName;
    private string? _displayName;
    private UserRole? _role;
    private bool _mustChangePassword;

    public bool IsAuthenticated => _userId.HasValue;

    public long? UserId => _userId;

    public string? UserName => _userName;

    public string? DisplayName => _displayName;

    public UserRole? Role => _role;

    /// <summary>当前用户是否需要强制修改密码</summary>
    public bool MustChangePassword => _mustChangePassword;

    public void SetAuthenticated(long userId, string username, string displayName, UserRole role, bool mustChangePassword = false)
    {
        _userId = userId;
        _userName = username;
        _displayName = displayName;
        _role = role;
        _mustChangePassword = mustChangePassword;
    }

    public void Clear()
    {
        _userId = null;
        _userName = null;
        _displayName = null;
        _role = null;
        _mustChangePassword = false;
    }
}
