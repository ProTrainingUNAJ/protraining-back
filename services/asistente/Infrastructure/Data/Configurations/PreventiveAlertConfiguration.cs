using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class PreventiveAlertConfiguration : IEntityTypeConfiguration<PreventiveAlert>
{
    public void Configure(EntityTypeBuilder<PreventiveAlert> builder)
    {
        builder.ToTable("preventive_alerts");

        builder.Property(a => a.AthleteId).HasColumnName("athlete_id").IsRequired();
        builder.Property(a => a.AthleteName).HasColumnName("athlete_name").HasMaxLength(PreventiveAlert.MaxAthleteNameLength).IsRequired();
        builder.Property(a => a.CategoryId).HasColumnName("category_id");
        builder.Property(a => a.Severity)
            .HasColumnName("severity")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(a => a.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(a => a.Reason).HasColumnName("reason").HasMaxLength(PreventiveAlert.MaxReasonLength).IsRequired();
        builder.Property(a => a.Suggestion).HasColumnName("suggestion").HasMaxLength(PreventiveAlert.MaxSuggestionLength).IsRequired();
        builder.Property(a => a.SuggestedAdjustmentType)
            .HasColumnName("suggested_adjustment_type")
            .HasConversion<string>()
            .HasMaxLength(50);
        builder.Property(a => a.SuggestedAdjustmentPercentage).HasColumnName("suggested_adjustment_percentage");
        builder.Property(a => a.SuggestedAdjustmentPayload).HasColumnName("suggested_adjustment_payload");
        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(a => a.GeneratedAtUtc).HasColumnName("generated_at_utc").IsRequired();
        builder.Property(a => a.ReviewedAtUtc).HasColumnName("reviewed_at_utc");
        builder.Property(a => a.AppliedAtUtc).HasColumnName("applied_at_utc");

        builder.Ignore(a => a.Reviewed);

        builder.HasIndex(a => a.AthleteId).HasDatabaseName("IX_preventive_alerts_athlete_id");
        builder.HasIndex(a => new { a.PfId, a.Status, a.GeneratedAtUtc }).HasDatabaseName("IX_preventive_alerts_pf_status_generated");
    }
}

