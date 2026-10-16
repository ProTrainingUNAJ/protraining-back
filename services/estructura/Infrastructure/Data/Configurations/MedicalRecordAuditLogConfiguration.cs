using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class MedicalRecordAuditLogConfiguration : IEntityTypeConfiguration<MedicalRecordAuditLog>
{
    public void Configure(EntityTypeBuilder<MedicalRecordAuditLog> builder)
    {
        builder.ToTable("medical_record_audit_logs");

        builder.Property(audit => audit.AthleteId)
            .HasColumnName("athlete_id")
            .IsRequired();

        builder.Property(audit => audit.AccessedByUserId)
            .HasColumnName("accessed_by_user_id")
            .IsRequired();

        builder.Property(audit => audit.Action)
            .HasColumnName("action")
            .HasMaxLength(MedicalRecordAuditLog.MaxActionLength)
            .IsRequired();

        builder.Property(audit => audit.Justification)
            .HasColumnName("justification")
            .HasMaxLength(MedicalRecordAuditLog.MaxJustificationLength)
            .IsRequired();

        builder.HasIndex(audit => audit.AthleteId);
        builder.HasIndex(audit => new { audit.AthleteId, audit.CreatedAt });
    }
}
