using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Clinic.Presentation.Services;

/// <summary>药品套餐中的单条药品（默认用量快照，套用时可改）</summary>
public sealed class DrugPackageItem
{
    public long DrugId { get; set; }
    public string DrugName { get; set; } = "";
    public string Spec { get; set; } = "";
    public decimal Dose { get; set; } = 1m;
    public string DoseUnit { get; set; } = "片";
    public string Frequency { get; set; } = "每日三次";
    public string Route { get; set; } = "口服";
    public int DurationDays { get; set; } = 3;
    public decimal Qty { get; set; } = 9m;
    public decimal PackQuantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
}

/// <summary>常用药品套餐（医生个人维度，一组药品组合）</summary>
public sealed class DrugPackage
{
    public string Name { get; set; } = "";
    public List<DrugPackageItem> Items { get; set; } = new();
}

/// <summary>医生个性化偏好（快捷词库 / 套餐库 / 默认用药，按登录名隔离）</summary>
public sealed class DoctorPreferences
{
    public List<string> CommonChiefComplaints { get; set; } = new();
    public List<string> CommonDiagnoses { get; set; } = new();
    public List<DrugPackage> DrugPackages { get; set; } = new();

    public string DefaultFrequency { get; set; } = "每日三次";
    public string DefaultRoute { get; set; } = "口服";
    public decimal DefaultDose { get; set; } = 1m;
    public string DefaultDoseUnit { get; set; } = "片";
    public int DefaultDurationDays { get; set; } = 3;
    public decimal DefaultQty { get; set; } = 9m;
}

/// <summary>
/// 医生个性化配置的本地存储（快捷词库 / 常用药品套餐 / 默认用药）。
/// 与处方模板一致：属于医生个人界面偏好，按登录名保存为独立 JSON 文件，
/// 不入数据库、不参与业务事务。
/// </summary>
public static class DoctorPreferencesStore
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    private static string FolderPath() => Path.Combine(AppContext.BaseDirectory, "usertemplates");

    private static string FilePathFor(string loginName) =>
        Path.Combine(FolderPath(), $"{Sanitize(loginName)}_preferences.json");

    private static string Sanitize(string name) =>
        new(name.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray());

    /// <summary>加载个人偏好；文件不存在或解析失败时返回默认偏好</summary>
    public static DoctorPreferences Load(string loginName)
    {
        var defaults = new DoctorPreferences();
        var path = FilePathFor(loginName);
        if (!File.Exists(path))
            return defaults;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<DoctorPreferences>(json, _jsonOpts) ?? defaults;
        }
        catch (JsonException)
        {
            return defaults;
        }
    }

    public static void Save(string loginName, DoctorPreferences preferences)
    {
        Directory.CreateDirectory(FolderPath());
        File.WriteAllText(FilePathFor(loginName), JsonSerializer.Serialize(preferences, _jsonOpts));
    }
}