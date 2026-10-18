using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class MedicalRecordConfiguration : IEntityTypeConfiguration<MedicalRecord>
{
    public void Configure(EntityTypeBuilder<MedicalRecord> builder)
    {
        builder.ToTable("medical_records");

        builder.Property(record => record.AthleteId)
            .HasColumnName("athlete_id")
            .IsRequired();

        builder.Property(record => record.MedicalHistory)
            .HasColumnName("medical_history")
            .HasMaxLength(MedicalRecord.MaxTextLength);

        builder.Property(record => record.Surgeries)
            .HasColumnName("surgeries")
            .HasMaxLength(MedicalRecord.MaxTextLength);

        builder.Property(record => record.Pathologies)
            .HasColumnName("pathologies")
            .HasMaxLength(MedicalRecord.MaxTextLength);

        builder.Property(record => record.HasPhysicalFitnessCertificate)
            .HasColumnName("has_physical_fitness_certificate")
            .IsRequired();

        builder.Property(record => record.PhysicalFitnessIssuedAt)
            .HasColumnName("physical_fitness_issued_at");

        builder.Property(record => record.PhysicalFitnessExpiresAt)
            .HasColumnName("physical_fitness_expires_at");

        builder.Property(record => record.PhysicalFitnessNotes)
            .HasColumnName("physical_fitness_notes")
            .HasMaxLength(MedicalRecord.MaxNotesLength);

        builder.Property(record => record.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(record => record.AthleteId)
            .IsUnique();
    }
}
