namespace Clinic.Domain.Entities;

public class SystemLog : Common.Entity
{
    public DateTime OccurredAt { get; set; }
    public long? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Target { get; set; }
    public string? Note { get; set; }
    public string? PayloadJson { get; set; }
}
