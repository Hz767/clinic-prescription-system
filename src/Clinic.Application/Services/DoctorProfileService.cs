using Clinic.Application.Interfaces;
using Clinic.Domain.Entities;
using Clinic.Domain.Interfaces;
using Clinic.Shared.Enums;

namespace Clinic.Application.Services;

/// <summary>医生档案服务实现</summary>
public class DoctorProfileService : IDoctorProfileService
{
    private readonly IRepository<SysUser> _userRepo;
    private readonly IRepository<DoctorProfile> _profileRepo;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUnitOfWork _unitOfWork;

    public DoctorProfileService(
        IRepository<SysUser> userRepo,
        IRepository<DoctorProfile> profileRepo,
        IPasswordHasher passwordHasher,
        IUnitOfWork unitOfWork)
    {
        _userRepo = userRepo;
        _profileRepo = profileRepo;
        _passwordHasher = passwordHasher;
        _unitOfWork = unitOfWork;
    }

    public async Task<long> RegisterDoctorAsync(
        string username, string password, string fullName,
        string gender, string? phone, string? email,
        string? medicalLicenseNo, string? practiceLicenseNo,
        string? specialty, string? title, string? department,
        CancellationToken ct = default)
    {
        // 检查用户名是否已存在
        var existing = await _userRepo.FindAsync(u => u.Username == username, ct);
        if (existing.Count > 0)
            throw new InvalidOperationException($"用户名「{username}」已存在");

        // 创建用户（role=Doctor）
        var user = new SysUser
        {
            Username = username,
            DisplayName = fullName,
            Role = UserRole.Doctor,
            PasswordHash = _passwordHasher.Hash(password),
            IsActive = true,
            MustChangePassword = false,
            FailedLoginCount = 0,
            CreatedAt = DateTime.Now
        };
        await _userRepo.AddAsync(user, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        // 创建医生档案
        var profile = new DoctorProfile
        {
            UserId = user.Id,
            FullName = fullName,
            Gender = gender,
            Phone = phone,
            Email = email,
            MedicalLicenseNo = medicalLicenseNo,
            PracticeLicenseNo = practiceLicenseNo,
            Specialty = specialty,
            Title = title,
            Department = department,
            Status = 0,
            CreatedAt = DateTime.Now
        };
        await _profileRepo.AddAsync(profile, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return user.Id;
    }

    public async Task<DoctorProfileDto?> GetByUserIdAsync(long userId, CancellationToken ct = default)
    {
        var profiles = await _profileRepo.FindAsync(p => p.UserId == userId, ct);
        var profile = profiles.FirstOrDefault();
        return profile is null ? null : ToDto(profile);
    }

    public async Task<bool> UpdateProfileAsync(long userId, DoctorProfileUpdateDto update, CancellationToken ct = default)
    {
        var profiles = await _profileRepo.FindAsync(p => p.UserId == userId, ct);
        var profile = profiles.FirstOrDefault();
        if (profile is null)
            return false;

        profile.FullName = update.FullName;
        profile.Gender = update.Gender;
        profile.BirthDate = update.BirthDate;
        profile.IdCard = update.IdCard;
        profile.Phone = update.Phone;
        profile.Email = update.Email;
        profile.Address = update.Address;
        profile.MedicalLicenseNo = update.MedicalLicenseNo;
        profile.MedicalLicenseIssueDate = update.MedicalLicenseIssueDate;
        profile.MedicalLicenseExpiryDate = update.MedicalLicenseExpiryDate;
        profile.PracticeLicenseNo = update.PracticeLicenseNo;
        profile.PracticeLicenseIssueDate = update.PracticeLicenseIssueDate;
        profile.PracticeLicenseExpiryDate = update.PracticeLicenseExpiryDate;
        profile.Specialty = update.Specialty;
        profile.Title = update.Title;
        profile.Department = update.Department;
        profile.Hospital = update.Hospital;
        profile.AvatarPath = update.AvatarPath;
        profile.IdCardFrontPath = update.IdCardFrontPath;
        profile.IdCardBackPath = update.IdCardBackPath;
        profile.MedicalLicensePhotoPath = update.MedicalLicensePhotoPath;
        profile.PracticeLicensePhotoPath = update.PracticeLicensePhotoPath;
        profile.Remark = update.Remark;
        profile.UpdatedAt = DateTime.Now;

        _profileRepo.Update(profile);
        await _unitOfWork.SaveChangesAsync(ct);

        // 同步更新用户显示名
        var user = await _userRepo.GetByIdAsync(userId, ct);
        if (user is not null && user.DisplayName != update.FullName)
        {
            user.DisplayName = update.FullName;
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return true;
    }

    public async Task<IReadOnlyList<DoctorProfileDto>> GetAllDoctorsAsync(CancellationToken ct = default)
    {
        var profiles = await _profileRepo.GetAllAsync(ct);
        return profiles.Select(ToDto).ToList();
    }

    private static DoctorProfileDto ToDto(DoctorProfile p) => new(
        p.Id, p.UserId, p.FullName, p.Gender,
        p.BirthDate, p.IdCard, p.Phone, p.Email, p.Address,
        p.MedicalLicenseNo, p.MedicalLicenseIssueDate, p.MedicalLicenseExpiryDate,
        p.PracticeLicenseNo, p.PracticeLicenseIssueDate, p.PracticeLicenseExpiryDate,
        p.Specialty, p.Title, p.Department, p.Hospital,
        p.AvatarPath, p.IdCardFrontPath, p.IdCardBackPath,
        p.MedicalLicensePhotoPath, p.PracticeLicensePhotoPath,
        p.Status, p.Remark, p.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
        p.UpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss"));
}
