using Clinic.Shared.Enums;

namespace Clinic.Domain.Entities;

public class Followup : Common.Entity
{
    public long PatientId { get; set; }
    public DateTime PlanAt { get; set; }
    public FollowupChannel Channel { get; set; }
    public FollowupStatus Status { get; set; }
    public string? Note { get; set; }
    public long DoctorId { get; set; }
}
