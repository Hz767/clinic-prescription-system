namespace Clinic.Domain.Entities;

/// <summary>医生档案（含行医资格证明与照片）</summary>
public class DoctorProfile : Common.Entity
{
    public long UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Gender { get; set; } = "男";
    public string? BirthDate { get; set; }
    public string? IdCard { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }

    /// <summary>医师资格证号</summary>
    public string? MedicalLicenseNo { get; set; }
    public string? MedicalLicenseIssueDate { get; set; }
    public string? MedicalLicenseExpiryDate { get; set; }

    /// <summary>执业医师证号</summary>
    public string? PracticeLicenseNo { get; set; }
    public string? PracticeLicenseIssueDate { get; set; }
    public string? PracticeLicenseExpiryDate { get; set; }

    public string? Specialty { get; set; }
    public string? Title { get; set; }
    public string? Department { get; set; }
    public string? Hospital { get; set; }

    /// <summary>人像照片路径</summary>
    public string? AvatarPath { get; set; }
    /// <summary>身份证正面照片路径</summary>
    public string? IdCardFrontPath { get; set; }
    /// <summary>身份证背面照片路径</summary>
    public string? IdCardBackPath { get; set; }
    /// <summary>医师资格证照片路径</summary>
    public string? MedicalLicensePhotoPath { get; set; }
    /// <summary>执业证照片路径</summary>
    public string? PracticeLicensePhotoPath { get; set; }

    /// <summary>审核状态：0=待审核, 1=已通过, 2=已驳回</summary>
    public int Status { get; set; }
    public string? Remark { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
