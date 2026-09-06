using System.IO;
using System.Windows;
using System.Windows.Input;
using Clinic.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation.ViewModels;

/// <summary>医生注册窗口 ViewModel</summary>
public partial class DoctorRegistrationViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;

    public DoctorRegistrationViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    // ── 账号信息 ──
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _confirmPassword = string.Empty;

    // ── 基本信息 ──
    [ObservableProperty] private string _fullName = string.Empty;
    [ObservableProperty] private string _gender = "男";
    [ObservableProperty] private string? _birthDate;
    [ObservableProperty] private string? _idCard;
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _address;

    // ── 行医资格 ──
    [ObservableProperty] private string? _medicalLicenseNo;
    [ObservableProperty] private string? _medicalLicenseIssueDate;
    [ObservableProperty] private string? _medicalLicenseExpiryDate;
    [ObservableProperty] private string? _practiceLicenseNo;
    [ObservableProperty] private string? _practiceLicenseIssueDate;
    [ObservableProperty] private string? _practiceLicenseExpiryDate;
    [ObservableProperty] private string? _specialty;
    [ObservableProperty] private string? _title;
    [ObservableProperty] private string? _department;
    [ObservableProperty] private string? _hospital;

    // ── 照片路径 ──
    [ObservableProperty] private string? _avatarPath;
    [ObservableProperty] private string? _idCardFrontPath;
    [ObservableProperty] private string? _idCardBackPath;
    [ObservableProperty] private string? _medicalLicensePhotoPath;
    [ObservableProperty] private string? _practiceLicensePhotoPath;

    /// <summary>是否展开可选信息区域（行医资格证明、照片上传），默认折叠</summary>
    [ObservableProperty] private bool _showOptionalSections;

    // ── 状态 ──
    [ObservableProperty] private string? _successMessage;

    /// <summary>照片存储目录</summary>
    private static string PhotoDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClinicSystem", "doctor_photos");

    [RelayCommand]
    private async Task RegisterAsync()
    {
        ErrorMessage = null;
        SuccessMessage = null;

        // 验证
        if (string.IsNullOrWhiteSpace(Username))
        { ErrorMessage = "请输入用户名"; return; }
        if (string.IsNullOrWhiteSpace(Password) || Password.Length < 6)
        { ErrorMessage = "密码至少6位"; return; }
        if (Password != ConfirmPassword)
        { ErrorMessage = "两次密码不一致"; return; }
        if (string.IsNullOrWhiteSpace(FullName))
        { ErrorMessage = "请输入姓名"; return; }

        IsBusy = true;
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IDoctorProfileService>();

            var userId = await service.RegisterDoctorAsync(
                Username, Password, FullName, Gender, Phone, Email,
                MedicalLicenseNo, PracticeLicenseNo, Specialty, Title, Department);

            // 更新档案（含照片等详细信息）
            var update = new DoctorProfileUpdateDto(
                FullName, Gender, BirthDate, IdCard, Phone, Email, Address,
                MedicalLicenseNo, MedicalLicenseIssueDate, MedicalLicenseExpiryDate,
                PracticeLicenseNo, PracticeLicenseIssueDate, PracticeLicenseExpiryDate,
                Specialty, Title, Department, Hospital,
                AvatarPath, IdCardFrontPath, IdCardBackPath,
                MedicalLicensePhotoPath, PracticeLicensePhotoPath, null);

            await service.UpdateProfileAsync(userId, update);

            SuccessMessage = $"注册成功！用户名：{Username}，请使用该账号登录。";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void UploadPhoto(string photoType)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp|所有文件|*.*",
            Title = "选择照片"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            Directory.CreateDirectory(PhotoDir);
            var ext = Path.GetExtension(dialog.FileName);
            var fileName = $"{photoType}_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}{ext}";
            var destPath = Path.Combine(PhotoDir, fileName);
            File.Copy(dialog.FileName, destPath, true);

            switch (photoType)
            {
                case "avatar": AvatarPath = destPath; break;
                case "idcard_front": IdCardFrontPath = destPath; break;
                case "idcard_back": IdCardBackPath = destPath; break;
                case "medical_license": MedicalLicensePhotoPath = destPath; break;
                case "practice_license": PracticeLicensePhotoPath = destPath; break;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"照片上传失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void CloseWindow(Window? window) => window?.Close();
}
