using Clinic.Application.Interfaces;
using Clinic.Application.Validators;
using Clinic.Domain.Entities;
using Clinic.Domain.Interfaces;
using Clinic.Shared.Enums;
using FluentValidation;

namespace Clinic.Application.Services;

/// <summary>
/// 患者档案服务实现。
/// 手机号采用「密文存储 + 哈希索引」双字段策略：
///   - PhoneEncrypted: AES-GCM 加密，需解密后显示
///   - PhoneHash: HMAC-SHA256 哈希（带服务器端密钥），用于唯一约束和快速查找
/// </summary>
public class PatientService : IPatientService
{
    private readonly IRepository<Patient> _patientRepo;
    private readonly IEncryptionService _encryption;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<CreatePatientRequest> _createPatientValidator;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IAuditService _auditService;
    private readonly IUserSession _session;

    public PatientService(
        IRepository<Patient> patientRepo,
        IEncryptionService encryption,
        IUnitOfWork unitOfWork,
        IValidator<CreatePatientRequest> createPatientValidator,
        IPermissionChecker permissionChecker,
        IAuditService auditService,
        IUserSession session)
    {
        _patientRepo = patientRepo;
        _encryption = encryption;
        _unitOfWork = unitOfWork;
        _createPatientValidator = createPatientValidator;
        _permissionChecker = permissionChecker;
        _auditService = auditService;
        _session = session;
    }

    public async Task<long> CreatePatientAsync(
        string name, string gender, DateOnly? dob,
        string phone, string? allergies, string? history,
        string? chronicTags,
        decimal? weight = null, decimal? temperature = null,
        int? systolicBP = null, int? diastolicBP = null, int? heartRate = null,
        CancellationToken ct = default)
    {
        // 权限检查：建档需要 Doctor 或 Nurse 权限
        _permissionChecker.RequireCanModify();

        // 输入验证：姓名、性别、手机号格式等由 FluentValidation 处理
        var request = new CreatePatientRequest(name, gender, dob, phone, allergies, history, chronicTags);
        var validationResult = await _createPatientValidator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        // 检查手机号是否已存在（通过哈希查找）
        var phoneHash = _encryption.HashPhone(phone);
        var existing = await _patientRepo.FindAsync(p => p.PhoneHash == phoneHash, ct);
        if (existing.Count > 0)
            throw new InvalidOperationException("该手机号已登记患者档案");

        var patient = new Patient
        {
            Name = name.Trim(),
            Gender = gender,
            Dob = dob,
            PhoneEncrypted = _encryption.Encrypt(phone.Trim()),
            PhoneHash = phoneHash,
            Allergies = allergies?.Trim(),
            History = history?.Trim(),
            ChronicTags = chronicTags?.Trim(),
            Weight = weight,
            Temperature = temperature,
            SystolicBP = systolicBP,
            DiastolicBP = diastolicBP,
            HeartRate = heartRate
        };

        await _patientRepo.AddAsync(patient, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        try
        {
            await _auditService.LogAsync("PATIENT_CREATE",
                $"Patient:{patient.Id}", $"Name:{name.Trim()}", ct);
        }
        catch
        {
            // 审计日志失败不影响建档操作
        }

        return patient.Id;
    }

    public async Task<PatientDto?> GetPatientByIdAsync(long id, CancellationToken ct = default)
    {
        // P1：权限检查——读取患者档案需 Doctor/Nurse/Readonly 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly);

        var patient = await _patientRepo.GetByIdAsync(id, ct);
        return patient is null ? null : ToDto(patient);
    }

    public async Task<PatientDto?> FindByPhoneAsync(string phone, CancellationToken ct = default)
    {
        // P1：权限检查——读取患者档案需 Doctor/Nurse/Readonly 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly);

        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var phoneHash = _encryption.HashPhone(phone);
        var patients = await _patientRepo.FindAsync(p => p.PhoneHash == phoneHash, ct);
        var patient = patients.FirstOrDefault();
        return patient is null ? null : ToDto(patient);
    }

    public async Task<IReadOnlyList<PatientDto>> SearchByNameAsync(
        string nameKeyword, CancellationToken ct = default)
    {
        // P1：权限检查——读取患者档案需 Doctor/Nurse/Readonly 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly);

        if (string.IsNullOrWhiteSpace(nameKeyword))
            return [];

        var patients = await _patientRepo.FindAsync(
            p => p.Name.Contains(nameKeyword), ct);

        return patients.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<PatientDto>> GetAllPatientsAsync(CancellationToken ct = default)
    {
        // P1：权限检查——读取患者档案需 Doctor/Nurse/Readonly 角色
        _permissionChecker.RequireRole(UserRole.Doctor, UserRole.Nurse, UserRole.Readonly);

        var patients = await _patientRepo.GetAllAsync(ct);
        return patients.Select(ToDto).ToList();
    }

    /// <summary>
    /// 更新患者档案信息（P2预备）。
    /// 可更新姓名、性别、出生日期、手机号、过敏史、病史、慢病标签。
    /// 如果手机号变更，需重新加密和哈希。
    /// </summary>
    public async Task<bool> UpdatePatientAsync(
        long id, string name, string gender, DateOnly? dob,
        string phone, string? allergies, string? history,
        string? chronicTags, CancellationToken ct = default)
    {
        // 权限检查：修改患者档案需要 Doctor 或 Nurse 权限
        _permissionChecker.RequireCanModify();

        var patient = await _patientRepo.GetByIdAsync(id, ct);
        if (patient is null)
            return false;

        // 基本字段更新
        patient.Name = name.Trim();
        patient.Gender = gender;
        patient.Dob = dob;
        patient.Allergies = allergies?.Trim();
        patient.History = history?.Trim();
        patient.ChronicTags = chronicTags?.Trim();

        // 手机号变更时重新加密和哈希
        var newPhoneHash = _encryption.HashPhone(phone);
        if (patient.PhoneHash != newPhoneHash)
        {
            // 检查新手机号是否已被其他患者使用
            var existing = await _patientRepo.FindAsync(
                p => p.PhoneHash == newPhoneHash && p.Id != id, ct);
            if (existing.Count > 0)
                throw new InvalidOperationException("该手机号已被其他患者档案使用");

            patient.PhoneEncrypted = _encryption.Encrypt(phone.Trim());
            patient.PhoneHash = newPhoneHash;
        }

        _patientRepo.Update(patient);
        await _unitOfWork.SaveChangesAsync(ct);

        try
        {
            await _auditService.LogAsync("PATIENT_UPDATE",
                $"Patient:{patient.Id}", $"Name:{name.Trim()}", ct);
        }
        catch
        {
            // 审计日志失败不影响更新操作
        }

        return true;
    }

    /// <summary>
    /// 更新患者体征信息（体重、体温、血压、心率）。
    /// 仅更新体征字段，不涉及档案基本信息。
    /// </summary>
    public async Task<bool> UpdateVitalsAsync(
        long id, decimal? weight, decimal? temperature,
        int? systolicBP, int? diastolicBP, int? heartRate,
        CancellationToken ct = default)
    {
        // 权限检查：修改体征需要 Doctor 或 Nurse 权限
        _permissionChecker.RequireCanModify();

        var patient = await _patientRepo.GetByIdAsync(id, ct);
        if (patient is null)
            return false;

        patient.Weight = weight;
        patient.Temperature = temperature;
        patient.SystolicBP = systolicBP;
        patient.DiastolicBP = diastolicBP;
        patient.HeartRate = heartRate;

        _patientRepo.Update(patient);
        await _unitOfWork.SaveChangesAsync(ct);

        try
        {
            await _auditService.LogAsync("PATIENT_VITALS_UPDATE",
                $"Patient:{patient.Id}",
                $"W:{weight}/T:{temperature}/BP:{systolicBP}/{diastolicBP}/HR:{heartRate}", ct);
        }
        catch
        {
            // 审计日志失败不影响体征更新操作
        }

        return true;
    }

    private PatientDto ToDto(Patient p)
    {
        var phone = _encryption.Decrypt(p.PhoneEncrypted);
        return new PatientDto(
            p.Id, p.Name, p.Gender, p.Dob,
            phone, p.Allergies, p.History, p.ChronicTags,
            p.Weight, p.Temperature, p.SystolicBP, p.DiastolicBP, p.HeartRate);
    }
}
