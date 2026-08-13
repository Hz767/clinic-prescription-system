using System.Text.RegularExpressions;
using Clinic.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Infrastructure.Data;

/// <summary>
/// 诊所处方管理系统主数据库上下文。
/// 对应 PRD §10.1 中的 17 张表。
/// 使用 SQLite + WAL 模式 + 字段级 AES-GCM 加密。
/// </summary>
public class ClinicDbContext : DbContext
{
    public ClinicDbContext(DbContextOptions<ClinicDbContext> options) : base(options)
    {
    }

    // ── 用户权限 ──
    public DbSet<SysUser> SysUsers => Set<SysUser>();

    // ── 患者与病历 ──
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();

    // ── 处方 ──
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();

    // ── 药品 ──
    public DbSet<DrugMaster> DrugMasters => Set<DrugMaster>();
    public DbSet<DrugInteraction> DrugInteractions => Set<DrugInteraction>();
    public DbSet<DrugStock> DrugStocks => Set<DrugStock>();
    public DbSet<DrugIn> DrugIns => Set<DrugIn>();
    public DbSet<DrugOut> DrugOuts => Set<DrugOut>();
    public DbSet<StockCheck> StockChecks => Set<StockCheck>();

    // ── 收费 ──
    public DbSet<PaymentLog> PaymentLogs => Set<PaymentLog>();

    // ── 随访 ──
    public DbSet<Followup> Followups => Set<Followup>();

    // ── 日志审计 ──
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemLog> SystemLogs => Set<SystemLog>();

    // ── 系统 ──
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<BackupManifest> BackupManifests => Set<BackupManifest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── 全局 snake_case 命名约定 ──
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.SetTableName(ToSnakeCase(entity.DisplayName()));

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }

        // ── SysUser ──
        modelBuilder.Entity<SysUser>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).HasMaxLength(50).IsRequired();
            e.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
            e.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
        });

        // ── Patient ──
        modelBuilder.Entity<Patient>(e =>
        {
            e.HasIndex(p => p.PhoneHash).IsUnique();
            e.Property(p => p.Name).HasMaxLength(50).IsRequired();
            e.Property(p => p.Gender).HasMaxLength(10).IsRequired();
            e.Property(p => p.PhoneEncrypted).HasMaxLength(512).IsRequired();
            e.Property(p => p.PhoneHash).HasMaxLength(64).IsRequired();
            e.Property(p => p.Allergies).HasMaxLength(1000);
            e.Property(p => p.History).HasMaxLength(2000);
            e.Property(p => p.ChronicTags).HasMaxLength(500);

            // ── 体征信息精度配置 ──
            e.Property(p => p.Weight).HasPrecision(5, 2);       // 体重 kg，如 75.50
            e.Property(p => p.Temperature).HasPrecision(4, 1);   // 体温 °C，如 36.5
        });

        // ── MedicalRecord ──
        modelBuilder.Entity<MedicalRecord>(e =>
        {
            e.HasIndex(m => m.PatientId);
            e.HasIndex(m => m.VisitAt);
            e.Property(m => m.ChiefComplaint).HasMaxLength(500).IsRequired();
            e.Property(m => m.Diagnosis).HasMaxLength(500).IsRequired();
            e.Property(m => m.PresentIllness).HasMaxLength(2000);
            e.Property(m => m.Exam).HasMaxLength(1000);
            e.Property(m => m.AuxiliaryExam).HasMaxLength(1000);
            e.Property(m => m.Plan).HasMaxLength(2000);
        });

        // ── Prescription ──
        modelBuilder.Entity<Prescription>(e =>
        {
            e.HasIndex(p => p.NoYearSeq).IsUnique();
            e.HasIndex(p => p.PatientId);
            e.HasIndex(p => p.DoctorId);
            e.Property(p => p.NoYearSeq).HasMaxLength(20).IsRequired();
            e.Property(p => p.DiagnosisText).HasMaxLength(500).IsRequired();
            e.Property(p => p.DiagnosisCode).HasMaxLength(100);
            e.Property(p => p.ExtendedReason).HasMaxLength(500);
            e.Property(p => p.PdfPath).HasMaxLength(500);
            e.Property(p => p.VoidReason).HasMaxLength(500);
            e.Property(p => p.TotalAmount).HasColumnType("DECIMAL(10,2)");

            // ── 体征字段配置（本次就诊实时数据） ──
            e.Property(p => p.Weight).HasPrecision(5, 2);       // 体重 kg，如 75.50
            e.Property(p => p.Temperature).HasPrecision(4, 1);   // 体温 °C，如 36.5
            // SystolicBP, DiastolicBP, HeartRate 为 int?，EF Core 自动映射为 INTEGER NULL
        });

        // ── PrescriptionItem ──
        modelBuilder.Entity<PrescriptionItem>(e =>
        {
            e.HasIndex(i => i.PrescriptionId);
            e.HasIndex(i => i.DrugId);
            e.Property(i => i.DrugName).HasMaxLength(100).IsRequired();
            e.Property(i => i.Spec).HasMaxLength(50).IsRequired();
            e.Property(i => i.DoseUnit).HasMaxLength(20).IsRequired();
            e.Property(i => i.Frequency).HasMaxLength(50).IsRequired();
            e.Property(i => i.Route).HasMaxLength(20).IsRequired();
            e.Property(i => i.UnitPrice).HasColumnType("DECIMAL(10,2)");
            e.Property(i => i.Subtotal).HasColumnType("DECIMAL(10,2)");
        });

        // ── DrugMaster ──
        modelBuilder.Entity<DrugMaster>(e =>
        {
            e.HasIndex(d => d.GenericNameEn).IsUnique();
            e.Property(d => d.GenericNameCn).HasMaxLength(100).IsRequired();
            e.Property(d => d.GenericNameEn).HasMaxLength(100).IsRequired();
            e.Property(d => d.Spec).HasMaxLength(50).IsRequired();
            e.Property(d => d.Unit).HasMaxLength(20).IsRequired();
            e.Property(d => d.DefaultUsage).HasMaxLength(200);
            e.Property(d => d.ContraindicationTags).HasMaxLength(500);
            e.Property(d => d.CostPriceRef).HasColumnType("DECIMAL(10,2)");
            e.Property(d => d.RetailPriceRef).HasColumnType("DECIMAL(10,2)");
        });

        // ── DrugInteraction ──
        modelBuilder.Entity<DrugInteraction>(e =>
        {
            e.HasIndex(d => new { d.DdinterIdA, d.DdinterIdB });
            e.HasIndex(d => d.DrugNameA);
            e.HasIndex(d => d.DrugNameB);
            e.Property(d => d.DrugNameA).HasMaxLength(100).IsRequired();
            e.Property(d => d.DrugNameB).HasMaxLength(100).IsRequired();
            e.Property(d => d.SourceAtcCode).HasMaxLength(20);
        });

        // ── DrugStock ──
        modelBuilder.Entity<DrugStock>(e =>
        {
            e.HasIndex(s => s.DrugId);
            e.HasIndex(s => s.ExpiryDate);
            e.Property(s => s.BatchNo).HasMaxLength(50).IsRequired();
            e.Property(s => s.Supplier).HasMaxLength(100);
            e.Property(s => s.CostPrice).HasColumnType("DECIMAL(10,2)");
            e.Property(s => s.QtyRemaining).HasColumnType("DECIMAL(10,2)");
        });

        // ── DrugIn ──
        modelBuilder.Entity<DrugIn>(e =>
        {
            e.HasIndex(d => d.DrugId);
            e.HasIndex(d => d.ReceivedAt);
            e.Property(d => d.BatchNo).HasMaxLength(50).IsRequired();
            e.Property(d => d.Supplier).HasMaxLength(100);
            e.Property(d => d.CostPrice).HasColumnType("DECIMAL(10,2)");
            e.Property(d => d.Qty).HasColumnType("DECIMAL(10,2)");
        });

        // ── DrugOut ──
        modelBuilder.Entity<DrugOut>(e =>
        {
            e.HasIndex(d => d.DrugId);
            e.HasIndex(d => d.PrescriptionId);
            e.Property(d => d.BatchNo).HasMaxLength(50).IsRequired();
            e.Property(d => d.Qty).HasColumnType("DECIMAL(10,2)");
        });

        // ── StockCheck ──
        modelBuilder.Entity<StockCheck>(e =>
        {
            e.HasIndex(s => s.OccurredAt);
        });

        // ── PaymentLog ──
        modelBuilder.Entity<PaymentLog>(e =>
        {
            e.HasIndex(p => p.PrescriptionId);
            e.HasIndex(p => p.OccurredAt);
            e.Property(p => p.Amount).HasColumnType("DECIMAL(10,2)");
            e.Property(p => p.PosSerialNo).HasMaxLength(50);
            e.Property(p => p.PosImagePath).HasMaxLength(500);
            e.Property(p => p.Note).HasMaxLength(500);
        });

        // ── Followup ──
        modelBuilder.Entity<Followup>(e =>
        {
            e.HasIndex(f => f.PatientId);
            e.HasIndex(f => f.PlanAt);
            e.Property(f => f.Note).HasMaxLength(500);
        });

        // ── AuditLog ──
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasIndex(a => a.OccurredAt);
            e.HasIndex(a => a.UserId);
            e.Property(a => a.Action).HasMaxLength(50).IsRequired();
            e.Property(a => a.Target).HasMaxLength(200);
            e.Property(a => a.PayloadHash).HasMaxLength(64);
            e.Property(a => a.PrevHash).HasMaxLength(64);
            e.Property(a => a.HashChain).HasMaxLength(64);
        });

        // ── SystemLog ──
        modelBuilder.Entity<SystemLog>(e =>
        {
            e.HasIndex(s => s.OccurredAt);
            e.HasIndex(s => s.UserId);
            e.Property(s => s.Action).HasMaxLength(50).IsRequired();
            e.Property(s => s.Target).HasMaxLength(200);
            e.Property(s => s.Note).HasMaxLength(500);
        });

        // ── Template ──
        modelBuilder.Entity<Template>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(100).IsRequired();
            e.Property(t => t.Version).HasMaxLength(20).IsRequired();
            e.Property(t => t.DefinitionJson).IsRequired();
        });

        // ── BackupManifest ──
        modelBuilder.Entity<BackupManifest>(e =>
        {
            e.HasIndex(b => b.TakenAt);
            e.Property(b => b.SourceDir).HasMaxLength(500).IsRequired();
            e.Property(b => b.Sha256).HasMaxLength(64).IsRequired();
            e.Property(b => b.FileListJson).IsRequired();
        });

        // ── 外键约束 ──
        // 显式定义外键关系，启用 PRAGMA foreign_keys=ON 后由 SQLite 强制引用完整性。
        // 使用 Restrict 删除行为：物理删除被引用的父记录时抛出异常，
        // 业务层通过软删除（DeletedAt）处理数据生命周期，不做级联删除。

        // MedicalRecord → Patient / SysUser
        modelBuilder.Entity<MedicalRecord>()
            .HasOne<Patient>()
            .WithMany()
            .HasForeignKey(m => m.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MedicalRecord>()
            .HasOne<SysUser>()
            .WithMany()
            .HasForeignKey(m => m.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Prescription → Patient / SysUser
        modelBuilder.Entity<Prescription>()
            .HasOne<Patient>()
            .WithMany()
            .HasForeignKey(p => p.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Prescription>()
            .HasOne<SysUser>()
            .WithMany()
            .HasForeignKey(p => p.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        // PrescriptionItem → Prescription / DrugMaster
        modelBuilder.Entity<PrescriptionItem>()
            .HasOne<Prescription>()
            .WithMany()
            .HasForeignKey(i => i.PrescriptionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PrescriptionItem>()
            .HasOne<DrugMaster>()
            .WithMany()
            .HasForeignKey(i => i.DrugId)
            .OnDelete(DeleteBehavior.Restrict);

        // DrugStock → DrugMaster
        modelBuilder.Entity<DrugStock>()
            .HasOne<DrugMaster>()
            .WithMany()
            .HasForeignKey(s => s.DrugId)
            .OnDelete(DeleteBehavior.Restrict);

        // DrugIn → DrugMaster
        modelBuilder.Entity<DrugIn>()
            .HasOne<DrugMaster>()
            .WithMany()
            .HasForeignKey(d => d.DrugId)
            .OnDelete(DeleteBehavior.Restrict);

        // DrugOut → DrugMaster / Prescription (nullable)
        modelBuilder.Entity<DrugOut>()
            .HasOne<DrugMaster>()
            .WithMany()
            .HasForeignKey(d => d.DrugId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DrugOut>()
            .HasOne<Prescription>()
            .WithMany()
            .HasForeignKey(d => d.PrescriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        // PaymentLog → Prescription
        modelBuilder.Entity<PaymentLog>()
            .HasOne<Prescription>()
            .WithMany()
            .HasForeignKey(p => p.PrescriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Followup → Patient / SysUser
        modelBuilder.Entity<Followup>()
            .HasOne<Patient>()
            .WithMany()
            .HasForeignKey(f => f.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Followup>()
            .HasOne<SysUser>()
            .WithMany()
            .HasForeignKey(f => f.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        // AuditLog → SysUser (nullable)
        modelBuilder.Entity<AuditLog>()
            .HasOne<SysUser>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // SystemLog → SysUser (nullable)
        modelBuilder.Entity<SystemLog>()
            .HasOne<SysUser>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// 将 PascalCase 转换为 snake_case。
    /// 例如: PhoneEncrypted → phone_encrypted, NoYearSeq → no_year_seq
    /// </summary>
    private static string ToSnakeCase(string name)
    {
        return Regex.Replace(
            Regex.Replace(name, "([A-Z]+)([A-Z][a-z])", "$1_$2"),
            "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
    }
}
