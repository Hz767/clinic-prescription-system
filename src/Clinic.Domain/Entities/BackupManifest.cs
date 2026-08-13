namespace Clinic.Domain.Entities;

public class BackupManifest : Common.Entity
{
    public DateTime TakenAt { get; set; }
    public string SourceDir { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string FileListJson { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}
