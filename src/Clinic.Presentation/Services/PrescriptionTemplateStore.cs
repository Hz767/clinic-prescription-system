using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Clinic.Presentation.Services;

/// <summary>处方模板中的单条药品明细（来自当前处方明细的快照）</summary>
public sealed class PrescriptionTemplateItem
{
    public long DrugId { get; set; }
    public string DrugName { get; set; } = "";
    public string Spec { get; set; } = "";
    public decimal Dose { get; set; }
    public string DoseUnit { get; set; } = "";
    public string Frequency { get; set; } = "";
    public string Route { get; set; } = "";
    public int DurationDays { get; set; }
    public decimal Qty { get; set; }
    public decimal PackQuantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
}

/// <summary>常用处方模板（医生个人维度，按登录名分文件存储）</summary>
public sealed class PrescriptionTemplate
{
    public string Name { get; set; } = "";
    public string DiagnosisText { get; set; } = "";
    public int PrescriptionType { get; set; }
    public List<PrescriptionTemplateItem> Items { get; set; } = new();
}

/// <summary>
/// 常用处方模板的本地存储。
/// 属于医生个人的界面偏好配置（类似快捷词库），按登录名保存为独立 JSON 文件，
/// 不入数据库、不参与业务事务，仅用于减少重复开方操作。
/// </summary>
public static class PrescriptionTemplateStore
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    private static string FolderPath() => Path.Combine(AppContext.BaseDirectory, "usertemplates");

    private static string FilePathFor(string loginName) =>
        Path.Combine(FolderPath(), $"{Sanitize(loginName)}_templates.json");

    private static string Sanitize(string name) =>
        new(name.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray());

    public static List<PrescriptionTemplate> Load(string loginName)
    {
        var path = FilePathFor(loginName);
        if (!File.Exists(path))
            return new List<PrescriptionTemplate>();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<PrescriptionTemplate>>(json, _jsonOpts)
                   ?? new List<PrescriptionTemplate>();
        }
        catch (JsonException)
        {
            return new List<PrescriptionTemplate>();
        }
    }

    public static void Save(string loginName, IReadOnlyList<PrescriptionTemplate> templates)
    {
        Directory.CreateDirectory(FolderPath());
        File.WriteAllText(FilePathFor(loginName), JsonSerializer.Serialize(templates, _jsonOpts));
    }
}