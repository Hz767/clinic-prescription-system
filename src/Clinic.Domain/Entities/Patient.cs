namespace Clinic.Domain.Entities;

public class Patient : Common.Entity
{
    public string Name { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public DateOnly? Dob { get; set; }
    public string PhoneEncrypted { get; set; } = string.Empty;
    public string PhoneHash { get; set; } = string.Empty;
    public string? Allergies { get; set; }
    public string? History { get; set; }
    public string? ChronicTags { get; set; }

    /// <summary>自定义标签（逗号分隔，如"孕妇"、"随访人群"、"高血压随访"），用于列表筛选</summary>
    public string? Tags { get; set; }

    // ── 体征信息 ──
    public decimal? Weight { get; set; }      // 体重 kg
    public decimal? Temperature { get; set; }  // 体温 °C
    public int? SystolicBP { get; set; }       // 收缩压
    public int? DiastolicBP { get; set; }      // 舒张压
    public int? HeartRate { get; set; }        // 心率
}
