using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Clinic.Presentation.Helpers;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Presentation.ViewModels;

// ── 药品模块：药品目录加载、搜索过滤、快速添加 ──
// 字段定义见 PrescriptionViewModel.cs 主文件
public partial class PrescriptionViewModel
{
    /// <summary>分类筛选变化时重新过滤药品列表</summary>
    partial void OnDrugCategoryFilterChanged(int value)
    {
        ApplyDrugFilter();
    }

    /// <summary>页面被导航到时加载药品目录</summary>
    [RelayCommand]
    private async Task LoadDrugsAsync()
    {
        if (_allDrugs.Count > 0) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var drugs = await inventoryService.GetAllDrugsAsync();

            _allDrugs = drugs.ToList();
            ApplyDrugFilter();
        }
        catch (Exception ex)
        {
            // 写入详细异常日志用于调试
            try
            {
                var logPath = System.IO.Path.Combine(AppContext.BaseDirectory, "drug_load_error.log");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 加载药品目录失败");
                sb.AppendLine($"Exception: {ex.GetType().FullName}: {ex.Message}");
                var inner = ex.InnerException;
                int depth = 0;
                while (inner is not null && depth < 5)
                {
                    sb.AppendLine($"  Inner[{depth}]: {inner.GetType().FullName}: {inner.Message}");
                    sb.AppendLine($"  Stack[{depth}]: {inner.StackTrace}");
                    inner = inner.InnerException;
                    depth++;
                }
                sb.AppendLine($"StackTrace: {ex.StackTrace}");
                System.IO.File.AppendAllText(logPath, sb.ToString());
            }
            catch
            {
                // 写错误日志失败时静默处理，避免二次异常掩盖原始错误
            }
            ErrorMessage = $"加载药品目录失败：{ExceptionFormatter.GetMessage(ex)}";
        }
    }

    /// <summary>药品搜索关键词变化时过滤药品列表</summary>
    partial void OnDrugFilterChanged(string value)
    {
        ApplyDrugFilter();
    }

    /// <summary>根据搜索关键词和分类筛选过滤药品列表</summary>
    private void ApplyDrugFilter()
    {
        Drugs.Clear();
        var keyword = DrugFilter?.Trim() ?? string.Empty;

        var filtered = _allDrugs.AsEnumerable();

        // 关键词过滤（名称/英文名/规格）
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filtered = filtered.Where(d =>
                d.GenericNameCn.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                d.GenericNameEn.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                d.Spec.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        // 分类过滤
        filtered = DrugCategoryFilter switch
        {
            1 => filtered.Where(d => d.IsAntibiotic),
            2 => filtered.Where(d => d.IsToxicDrug || d.AntibioticLevel >= 2),
            3 => filtered.Where(d => d.IsLowStock),
            _ => filtered
        };

        foreach (var d in filtered)
            Drugs.Add(d);
    }

    /// <summary>
    /// 快速添加药品到处方明细。
    /// 如果处方尚未创建，自动创建（需要已选患者+诊断）。
    /// 默认用法用量根据药品特性自动推断，医生可在明细中直接修改。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanQuickAddDrug))]
    private async Task QuickAddDrugAsync(DrugDto? drug)
    {
        if (drug is null)
            return;

        // 自动创建处方（如果尚未创建）
        if (_draftPrescriptionId is null)
        {
            if (!await EnsureDraftPrescriptionAsync())
                return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var prescriptionService = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();

            // 根据药品单位推断剂量单位
            var dose = 1m;
            var doseUnit = drug.Unit switch
            {
                "盒" => "粒",
                "瓶" => "片",
                "袋" => "袋",
                "支" => "支",
                _ => "片"
            };
            var frequency = "每日三次";
            var route = "口服";
            var durationDays = 3;
            var timesPerDay = 3;
            var qty = dose * timesPerDay * durationDays;

            var itemId = await prescriptionService.AddPrescriptionItemAsync(
                _draftPrescriptionId!.Value,
                drug.Id,
                dose,
                doseUnit,
                frequency,
                route,
                durationDays,
                qty);

            if (itemId > 0)
            {
                var unitPrice = drug.RetailPriceRef ?? 0m;
                var packQty = PrescriptionItemDto.ParsePackQuantity(drug.Spec);
                var item = new PrescriptionItemDto
                {
                    Id = itemId,
                    DrugId = drug.Id,
                    DrugName = drug.GenericNameCn,
                    Spec = drug.Spec,
                    Dose = dose,
                    DoseUnit = doseUnit,
                    Frequency = frequency,
                    Route = route,
                    DurationDays = durationDays,
                    Qty = qty,
                    UnitPrice = unitPrice,
                    PackQuantity = packQty
                };
                item.RecalculateSubtotalOnly();
                PrescriptionItems.Add(item);

                StatusMessage = $"已添加：{drug.GenericNameCn} × {qty}{doseUnit}（可在下方明细中修改用法用量）";

                // 添加药品后自动运行AI药师审核
                _ = RunAiPharmacistReviewAsync();
            }
            else
            {
                ErrorMessage = "添加明细失败，处方可能已保存或作废";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"添加明细失败：{ExceptionFormatter.GetMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanQuickAddDrug(DrugDto? drug)
        => !IsBusy && drug is not null && SelectedPatient is not null && !string.IsNullOrWhiteSpace(DiagnosisText);
}
