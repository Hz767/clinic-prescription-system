using Clinic.Shared.Enums;

namespace Clinic.Application.Interfaces;

/// <summary>
/// 权限检查器接口。基于当前登录用户角色校验操作权限。
/// 在 Application 服务的写操作入口处调用，未授权时抛出 UnauthorizedAccessException。
/// </summary>
public interface IPermissionChecker
{
    /// <summary>当前用户角色</summary>
    UserRole? CurrentRole { get; }

    /// <summary>检查处方开具权限（仅 Doctor）</summary>
    void RequireCanPrescribe();

    /// <summary>检查收费权限（仅 Doctor）</summary>
    void RequireCanBill();

    /// <summary>检查数据修改权限（Doctor + Nurse，如建档、入库）</summary>
    void RequireCanModify();

    /// <summary>检查指定角色权限（通用）</summary>
    void RequireRole(params UserRole[] allowedRoles);
}
