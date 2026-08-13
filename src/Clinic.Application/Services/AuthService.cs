using Clinic.Application.Interfaces;
using Clinic.Application.Validators;
using Clinic.Domain.Entities;
using Clinic.Domain.Interfaces;
using FluentValidation;

namespace Clinic.Application.Services;

/// <summary>
/// 认证服务实现。处理用户登录、登出、会话状态管理。
/// 登录失败锁定策略：连续失败 5 次锁定 10 分钟（参数化常量）。
/// 依赖：IRepository&lt;SysUser&gt;（Scoped）、IPasswordHasher（Singleton）、IUserSession（Singleton）、IUnitOfWork（Scoped）、IClock（Singleton）
/// </summary>
public class AuthService : IAuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(10);

    private readonly IRepository<SysUser> _userRepo;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUserSession _session;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IAuditService _auditService;

    public AuthService(
        IRepository<SysUser> userRepo,
        IPasswordHasher passwordHasher,
        IUserSession session,
        IUnitOfWork unitOfWork,
        IClock clock,
        IValidator<LoginRequest> loginValidator,
        IAuditService auditService)
    {
        _userRepo = userRepo;
        _passwordHasher = passwordHasher;
        _session = session;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _loginValidator = loginValidator;
        _auditService = auditService;
    }

    public bool IsAuthenticated => _session.IsAuthenticated;

    public long? CurrentUserId => _session.UserId;

    public string? CurrentUserName => _session.UserName;

    public async Task<bool> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        // 输入验证：空值/格式校验由 FluentValidation 处理
        var request = new LoginRequest(username, password);
        var validationResult = await _loginValidator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        var users = await _userRepo.FindAsync(
            u => u.Username == username && u.IsActive, ct);

        var user = users.FirstOrDefault();
        if (user is null)
            return false;

        // 检查账户锁定状态
        var now = _clock.UtcNow;
        if (user.LockedUntil is not null && user.LockedUntil > now)
        {
            var remaining = user.LockedUntil.Value - now;
            await SafeAuditAsync("LOGIN_LOCKED", $"User:{username}",
                $"账户锁定，剩余 {remaining.Minutes} 分 {remaining.Seconds} 秒", ct);
            throw new InvalidOperationException(
                $"账户已锁定，请于 {remaining.Minutes} 分 {remaining.Seconds} 秒后重试");
        }

        // 密码验证
        if (!_passwordHasher.Verify(password, user.PasswordHash))
        {
            // 记录失败次数，达到上限则锁定
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockedUntil = now + LockoutDuration;
                user.FailedLoginCount = 0; // 锁定后重置计数
            }
            _userRepo.Update(user);
            await _unitOfWork.SaveChangesAsync(ct);

            await SafeAuditAsync("LOGIN_FAILURE", $"User:{username}",
                $"失败次数：{user.FailedLoginCount}/{MaxFailedAttempts}", ct);
            return false;
        }

        // 登录成功：重置失败计数和锁定状态
        user.LastLoginAt = now;
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        _userRepo.Update(user);
        await _unitOfWork.SaveChangesAsync(ct);

        // 设置会话状态（P2：携带 MustChangePassword 标记，UI 层可据此提示改密）
        _session.SetAuthenticated(
            user.Id, user.Username, user.DisplayName, user.Role, user.MustChangePassword);

        await SafeAuditAsync("LOGIN_SUCCESS", $"User:{username}", null, ct);

        // P2：MustChangePassword 已写入 UserSession，UI 层在登录成功后检查
        // _session.MustChangePassword 并引导用户修改密码
        return true;
    }

    public void Logout()
    {
        var username = _session.UserName;
        _session.Clear();
        try { _auditService.LogAsync("LOGOUT", $"User:{username}").Wait(); }
        catch { /* 审计日志失败不影响登出 */ }
    }

    /// <summary>
    /// 安全审计日志：审计失败不影响业务操作（best-effort）。
    /// 登录场景中用户会话尚未建立或已清除，审计日志可能无法获取 userId。
    /// </summary>
    private async Task SafeAuditAsync(string action, string target, string? note, CancellationToken ct)
    {
        try
        {
            await _auditService.LogAsync(action, target, note, ct);
        }
        catch
        {
            // 审计日志失败不影响登录流程
        }
    }
}
