using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace Clinic.Presentation.ViewModels;

/// <summary>
/// 病历管理 ViewModel。
/// 浏览和检索所有患者的就诊病历记录。
/// 通过 Application 层 IMedicalRecordService 访问数据，不直连持久化层。
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
            var service = scope.ServiceProvider.GetRequiredService<IMedicalRecordService>();

            var records = await service.GetAllAsync();

            AllRecords.Clear();
            foreach (var r in records)
                AllRecords.Add(r);

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
