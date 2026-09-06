using Clinic.Application.Interfaces;
using Clinic.Shared.Enums;

namespace Clinic.Application.Services;

/// <summary>
/// 权限检查器实现。基于 IUserSession 中的用户角色进行权限校验。
/// 权限矩阵：
///   Doctor      — 全部操作
///   Nurse       — 患者建档、入库、查看（不可开方、不可收费）
///   Pharmacist  — 处方审核、发药、药房入库、库存查看（不可开方、不可收费）
///   Readonly    — 仅查看（不可任何写操作）
/// </summary>
public class PermissionChecker : IPermissionChecker
{
    private readonly IUserSession _session;

    public PermissionChecker(IUserSession session)
    {
        _session = session;
    }

    public UserRole? CurrentRole => _session.Role;

    /// <summary>处方开具权限：仅 Doctor</summary>
    public void RequireCanPrescribe()
        => RequireRole(UserRole.Doctor);

    /// <summary>收费权限：仅 Doctor</summary>
    public void RequireCanBill()
        => RequireRole(UserRole.Doctor);

    /// <summary>数据修改权限：Doctor + Nurse + Pharmacist（药师负责药房入库）</summary>
    public void RequireCanModify()
        => RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Pharmacist);

    /// <summary>
    /// 通用角色检查。未登录或角色不在允许列表中时抛出 UnauthorizedAccessException。
    /// </summary>
    public void RequireRole(params UserRole[] allowedRoles)
    {
        if (!_session.IsAuthenticated || _session.Role is null)
            throw new UnauthorizedAccessException("未登录，请先登录后再操作");

        if (!allowedRoles.Contains(_session.Role.Value))
        {
            var roleNames = string.Join("、", allowedRoles.Select(RoleToText));
            throw new UnauthorizedAccessException(
                $"当前角色「{RoleToText(_session.Role.Value)}」无权执行此操作，需要{roleNames}权限");
        }
    }

    private static string RoleToText(UserRole role) => role switch
    {
        UserRole.Doctor => "医生",
        UserRole.Nurse => "护士",
        UserRole.Readonly => "只读用户",
        UserRole.Pharmacist => "药师",
        _ => role.ToString()
    };
}
