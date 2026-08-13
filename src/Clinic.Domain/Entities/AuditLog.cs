namespace Clinic.Domain.Entities;

public class AuditLog : Common.Entity
{
    public DateTime OccurredAt { get; set; }
    public long? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Target { get; set; }
    public string? PayloadHash { get; set; }
    public string? PrevHash { get; set; }
    public string? HashChain { get; set; }
}
