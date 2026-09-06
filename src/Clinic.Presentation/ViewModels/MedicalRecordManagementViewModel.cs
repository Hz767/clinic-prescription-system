using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Clinic.Domain.Entities;
using Clinic.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace Clinic.Presentation.ViewModels;

/// <summary>
/// 病历管理 ViewModel。
/// 浏览和检索所有患者的就诊病历记录。
/// </summary>
public partial class MedicalRecordManagementViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;

    [ObservableProperty]
    private ObservableCollection<MedicalRecordListDto> _allRecords = new();

    [ObservableProperty]
    private ObservableCollection<MedicalRecordListDto> _filteredRecords = new();

    [ObservableProperty]
    private MedicalRecordListDto? _selectedRecord;

    [ObservableProperty]
    private string _searchKeyword = string.Empty;
public int TotalCount => AllRecords.Count;
    public int FilteredCount => FilteredRecords.Count;

    public MedicalRecordManagementViewModel(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var recordRepo = scope.ServiceProvider.GetRequiredService<IRepository<MedicalRecord>>();
            var patientRepo = scope.ServiceProvider.GetRequiredService<IRepository<Patient>>();
            var userRepo = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();

            // 加载所有病历
            var records = await recordRepo.GetAllAsync();
            var patients = (await patientRepo.GetAllAsync()).ToDictionary(p => p.Id, p => p);
            var doctors = (await userRepo.GetAllAsync()).ToDictionary(u => u.Id, u => u);

            AllRecords.Clear();
            foreach (var r in records.OrderByDescending(r => r.VisitAt))
            {
                var patient = patients.TryGetValue(r.PatientId, out var p) ? p : null;
                var doctor = doctors.TryGetValue(r.DoctorId, out var d) ? d : null;

                AllRecords.Add(new MedicalRecordListDto(
                    r.Id,
                    r.PatientId,
                    patient?.Name ?? "—",
                    patient?.Gender ?? "—",
                    doctor?.DisplayName ?? "—",
                    r.VisitAt,
                    r.ChiefComplaint,
                    r.Diagnosis,
                    r.PresentIllness,
                    r.Exam,
                    r.Plan
                ));
            }

            ApplyFilter();
            StatusMessage = $"共加载 {AllRecords.Count} 条病历记录";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSearchKeywordChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredRecords.Clear();
        if (string.IsNullOrWhiteSpace(SearchKeyword))
        {
            foreach (var r in AllRecords)
                FilteredRecords.Add(r);
            return;
        }

        var kw = SearchKeyword.Trim();
        foreach (var r in AllRecords)
        {
            if ((r.PatientName?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.Diagnosis?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.ChiefComplaint?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.DoctorName?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                FilteredRecords.Add(r);
            }
        }
    }

    [RelayCommand]
    private void Refresh() => _ = LoadDataAsync();
}
