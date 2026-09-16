using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Clinic.Application.DTOs;
using Clinic.Presentation.Helpers;
using Clinic.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clinic.Presentation.ViewModels;

/// <summary>
/// 医生个性化配置（阶段三）：快捷词库、常用药品套餐、默认用药。
/// 按登录名本地 JSON 存储（<see cref="DoctorPreferencesStore"/>），多医生各自保存。
/// </summary>
public partial class PrescriptionViewModel
{
    /// <summary>医生个人登录标识（用于本地配置文件的路径隔离）</summary>
    private string LoginKey => _session.UserName ?? _session.UserId?.ToString() ?? "default";

    /// <summary>是否展开"个性化配置"面板</summary>
    [ObservableProperty]
    private bool _isPersonalizationOpen;

    // ── 常用药品套餐 ──

    public ObservableCollection<DrugPackage> Packages { get; } = new();

    [ObservableProperty]
    private DrugPackage? _selectedPackage;

    [ObservableProperty]
    private string _packageNameInput = "";

    partial void OnSelectedPackageChanged(DrugPackage? value)
    {
        ApplySelectedPackageCommand.NotifyCanExecuteChanged();
        DeleteSelectedPackageCommand.NotifyCanExecuteChanged();
    }

    // ── 快捷词库管理 ──

    [ObservableProperty]
    private string _newChiefComplaint = "";

    [ObservableProperty]
    private string _newDiagnosis = "";

    /// <summary>加载医生个人配置：用个人快捷词覆盖内置词库、加载套餐与默认用药</summary>
    private void LoadPersonalization()
    {
        var prefs = DoctorPreferencesStore.Load(LoginKey);

        // 快捷词库：仅当用户配置过才覆盖内置默认（保留内置词作为首次打开体验）
        if (prefs.CommonChiefComplaints.Count > 0)
        {
            CommonChiefComplaints.Clear();
            foreach (var s in prefs.CommonChiefComplaints)
                CommonChiefComplaints.Add(s);
        }
        if (prefs.CommonDiagnoses.Count > 0)
        {
            CommonDiagnoses.Clear();
            foreach (var s in prefs.CommonDiagnoses)
                CommonDiagnoses.Add(s);
        }

        Packages.Clear();
        foreach (var p in prefs.DrugPackages)
            Packages.Add(p);
    }

    /// <summary>持久化快引词库 + 药品套餐 + 默认用药到本地 JSON</summary>
    private void PersistPersonalization()
    {
        var prefs = new DoctorPreferences
        {
            CommonChiefComplaints = CommonChiefComplaints.ToList(),
            CommonDiagnoses = CommonDiagnoses.ToList(),
            DrugPackages = Packages.ToList(),
            DefaultFrequency = Frequency,
            DefaultRoute = Route,
            DefaultDose = Dose,
            DefaultDoseUnit = DoseUnit,
            DefaultDurationDays = DurationDays,
            DefaultQty = Qty,
        };
        DoctorPreferencesStore.Save(LoginKey, prefs);
    }

    /// <summary>新增自定义快捷词（主诉）</summary>
    [RelayCommand]
    private void AddChiefComplaintCustom()
    {
        var v = NewChiefComplaint.Trim();
        if (v.Length == 0) return;
        if (!CommonChiefComplaints.Contains(v))
            CommonChiefComplaints.Add(v);
        NewChiefComplaint = "";
        PersistPersonalization();
    }

    /// <summary>删除自定义快捷词（主诉）</summary>
    [RelayCommand]
    private void RemoveChiefComplaint(string? value)
    {
        if (value is null) return;
        CommonChiefComplaints.Remove(value);
        PersistPersonalization();
    }

    /// <summary>新增自定义快捷词（诊断）</summary>
    [RelayCommand]
    private void AddDiagnosisCustom()
    {
        var v = NewDiagnosis.Trim();
        if (v.Length == 0) return;
        if (!CommonDiagnoses.Contains(v))
            CommonDiagnoses.Add(v);
        NewDiagnosis = "";
        PersistPersonalization();
    }

    /// <summary>删除自定义快捷词（诊断）</summary>
    [RelayCommand]
    private void RemoveDiagnosisCustom(string? value)
    {
        if (value is null) return;
        CommonDiagnoses.Remove(value);
        PersistPersonalization();
    }

    // ── 常用药品套餐 ──

    /// <summary>套用所选套餐：把套餐内全部药品带入当前草稿（可继续增改）</summary>
    [RelayCommand(CanExecute = nameof(CanApplySelectedPackage))]
    private async Task ApplySelectedPackageAsync()
    {
        if (SelectedPackage is null)
            return;

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            if (!await EnsureDraftPrescriptionAsync())
                return;

            foreach (var pi in SelectedPackage.Items)
            {
                await AddItemToDraftAsync(pi.DrugId, pi.Dose, pi.DoseUnit, pi.Frequency, pi.Route,
                    pi.DurationDays, pi.Qty, pi.DrugName, pi.Spec, pi.UnitPrice, pi.PackQuantity);
            }

            StatusMessage = $"已套用套餐「{SelectedPackage.Name}」：{SelectedPackage.Items.Count} 项药品";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"套用套餐失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanApplySelectedPackage() => SelectedPackage is not null;

    /// <summary>把当前草稿的全部药品组合保存为常用套餐（纯药品组合，不含诊断）</summary>
    [RelayCommand]
    private void SaveCurrentAsPackage()
    {
        if (PrescriptionItems.Count == 0)
        {
            ErrorMessage = "当前处方没有药品明细，无法保存为套餐";
            return;
        }

        var name = PackageNameInput.Trim();
        if (name.Length == 0)
        {
            ErrorMessage = "请先输入套餐名称";
            return;
        }

        var existing = Packages.FirstOrDefault(p => p.Name == name);
        if (existing is not null)
            Packages.Remove(existing);

        Packages.Add(new DrugPackage
        {
            Name = name,
            Items = PrescriptionItems.Select(i => new DrugPackageItem
            {
                DrugId = i.DrugId,
                DrugName = i.DrugName,
                Spec = i.Spec,
                Dose = i.Dose,
                DoseUnit = i.DoseUnit,
                Frequency = i.Frequency,
                Route = i.Route,
                DurationDays = i.DurationDays,
                Qty = i.Qty,
                PackQuantity = i.PackQuantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        });

        PersistPersonalization();
        SelectedPackage = Packages.Last();
        PackageNameInput = "";
        StatusMessage = $"已保存常用套餐「{name}」";
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedPackage))]
    private void DeleteSelectedPackage()
    {
        if (SelectedPackage is null) return;

        var name = SelectedPackage.Name;
        Packages.Remove(SelectedPackage);
        SelectedPackage = null;
        PersistPersonalization();
        StatusMessage = $"已删除套餐「{name}」";
    }

    private bool CanDeleteSelectedPackage() => SelectedPackage is not null;

    // ── 默认用药（新建处方时预填） ──

    /// <summary>把当前表单作为个人默认用药，新开方时自动预填</summary>
    [RelayCommand]
    private void SaveMedicationDefaults()
    {
        PersistPersonalization();
        StatusMessage = $"已保存默认用药：每次 {Dose}{DoseUnit}，{Frequency}，{Route}，{DurationDays} 天";
    }

    /// <summary>由个人默认用药重置开方表单（NewPrescription 时调用）</summary>
    private void ResetMedicationToPersonalizedDefaults()
    {
        var prefs = DoctorPreferencesStore.Load(LoginKey);
        Dose = prefs.DefaultDose;
        DoseUnit = prefs.DefaultDoseUnit;
        Frequency = prefs.DefaultFrequency;
        Route = prefs.DefaultRoute;
        DurationDays = prefs.DefaultDurationDays;
        Qty = prefs.DefaultQty;
    }
}