using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class DigitalConsentConfiguration : IEntityTypeConfiguration<DigitalConsent>
{
    public void Configure(EntityTypeBuilder<DigitalConsent> builder)
    {
        builder.ToTable("digital_consents");

        builder.Property(consent => consent.AthleteId)
            .HasColumnName("athlete_id")
            .IsRequired();

        builder.Property(consent => consent.IsSigned)
            .HasColumnName("is_signed")
            .IsRequired();

        builder.Property(consent => consent.ConsentDate)
            .HasColumnName("consent_date")
            .IsRequired();

        builder.Property(consent => consent.IsMinor)
            .HasColumnName("is_minor")
            .IsRequired();

        builder.Property(consent => consent.GuardianName)
            .HasColumnName("guardian_name")
            .HasMaxLength(150);

        builder.Property(consent => consent.GuardianEmail)
            .HasColumnName("guardian_email")
            .HasMaxLength(150);

        builder.Property(consent => consent.GuardianPhone)
            .HasColumnName("guardian_phone")
            .HasMaxLength(50);

        builder.Property(consent => consent.AllowsPhysicalTraining)
            .HasColumnName("allows_physical_training")
            .IsRequired();

        builder.Property(consent => consent.AllowsHabitsRecommendations)
            .HasColumnName("allows_habits_recommendations")
            .IsRequired();

        builder.HasIndex(consent => consent.AthleteId)
            .IsUnique();
    }
}
