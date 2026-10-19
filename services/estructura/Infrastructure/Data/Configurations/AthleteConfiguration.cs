using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class AthleteConfiguration : IEntityTypeConfiguration<Athlete>
{
    public void Configure(EntityTypeBuilder<Athlete> builder)
    {
        builder.ToTable("athletes");

        builder.Property(athlete => athlete.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(athlete => athlete.LastName)
            .HasColumnName("last_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(athlete => athlete.Email)
            .HasColumnName("email")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(athlete => athlete.Phone)
            .HasColumnName("phone")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(athlete => athlete.IsMinor)
            .HasColumnName("is_minor")
            .IsRequired();

        builder.Property(athlete => athlete.EmergencyContact)
            .HasColumnName("emergency_contact")
            .HasMaxLength(150);

        builder.Property(athlete => athlete.Active)
            .HasColumnName("active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(athlete => athlete.AccountId)
            .HasColumnName("account_id");

        builder.HasOne(athlete => athlete.DigitalConsent)
            .WithOne()
            .HasForeignKey<DigitalConsent>(consent => consent.AthleteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(athlete => athlete.MedicalRecord)
            .WithOne()
            .HasForeignKey<MedicalRecord>(record => record.AthleteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
