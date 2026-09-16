using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Clinic.Domain.Entities;
using Clinic.Domain.Interfaces;

namespace Clinic.Application.Services;

/// <summary>
/// 病历管理服务实现。
/// 在 Application 层完成对 MedicalRecord/Patient/SysUser 的查询与组装，
/// 表现层通过本服务访问病历数据，避免直达仓储/持久化层。
/// </summary>
public class MedicalRecordService : IMedicalRecordService
{
    private readonly IRepository<MedicalRecord> _recordRepo;
    private readonly IRepository<Patient> _patientRepo;
    private readonly IRepository<SysUser> _userRepo;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IAuditService _auditService;

    public MedicalRecordService(
        IRepository<MedicalRecord> recordRepo,
        IRepository<Patient> patientRepo,
        IRepository<SysUser> userRepo,
        IPermissionChecker permissionChecker,
        IAuditService auditService)
    {
        _recordRepo = recordRepo;
        _patientRepo = patientRepo;
        _userRepo = userRepo;
        _permissionChecker = permissionChecker;
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<MedicalRecordListDto>> GetAllAsync(CancellationToken ct = default)
    {
        RequireAuthenticated();

        var records = await _recordRepo.GetAllAsync(ct);
        var patients = (await _patientRepo.GetAllAsync(ct)).ToDictionary(p => p.Id);
        var doctors = (await _userRepo.GetAllAsync(ct)).ToDictionary(u => u.Id);

        var result = records
            .OrderByDescending(r => r.VisitAt)
            .Select(r => new MedicalRecordListDto(
                r.Id,
                r.PatientId,
                patients.TryGetValue(r.PatientId, out var p) ? p.Name : "—",
                patients.TryGetValue(r.PatientId, out var p2) ? p2.Gender : "—",
                doctors.TryGetValue(r.DoctorId, out var d) ? d.DisplayName : "—",
                r.VisitAt,
                r.ChiefComplaint,
                r.Diagnosis,
                r.PresentIllness,
                r.Exam,
                r.Plan))
            .ToList();

        try
        {
            await _auditService.LogAsync("MEDICAL_RECORD_LIST", null, $"Count:{result.Count}", ct);
        }
        catch
        {
            // 审计失败不影响查询结果
        }

        return result;
    }

    public async Task<MedicalRecordDetailDto?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        RequireAuthenticated();

        var record = await _recordRepo.GetByIdAsync(id, ct);
        if (record is null)
            return null;

        var patient = await _patientRepo.GetByIdAsync(record.PatientId, ct);
        var doctor = await _userRepo.GetByIdAsync(record.DoctorId, ct);

        try
        {
            await _auditService.LogAsync("MEDICAL_RECORD_VIEW", $"MedicalRecord:{id}", null, ct);
        }
        catch
        {
            // 审计失败不影响查看
        }

        return new MedicalRecordDetailDto(
            record.Id,
            record.PatientId,
            patient?.Name ?? "—",
            patient?.Gender ?? "—",
            record.DoctorId,
            doctor?.DisplayName ?? "—",
            record.VisitAt,
            record.ChiefComplaint,
            record.PresentIllness,
            record.Exam,
            record.ExamNa,
            record.AuxiliaryExam,
            record.AuxiliaryNa,
            record.Diagnosis,
            record.Plan,
            record.UpdatedAt);
    }

    /// <summary>读操作守卫：仅允许已登录用户（任何角色均可查看，只读角色无写权限由写服务单独约束）。</summary>
    private void RequireAuthenticated()
    {
        if (_permissionChecker.CurrentRole is null)
            throw new UnauthorizedAccessException("请先登录后再访问病历数据");
    }
}