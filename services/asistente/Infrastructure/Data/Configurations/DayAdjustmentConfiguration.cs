using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class DayAdjustmentConfiguration : IEntityTypeConfiguration<DayAdjustment>
{
    public void Configure(EntityTypeBuilder<DayAdjustment> builder)
    {
        builder.ToTable("day_adjustments");

        builder.Property(d => d.AthleteId).HasColumnName("athlete_id").IsRequired();
        builder.Property(d => d.Date).HasColumnName("date").IsRequired();
        builder.Property(d => d.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(d => d.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(d => d.Items).HasColumnName("items").HasColumnType("jsonb").IsRequired();
        builder.Property(d => d.Justification)
            .HasColumnName("justification")
            .HasMaxLength(DayAdjustment.MaxJustificationLength)
            .IsRequired();
        builder.Property(d => d.Provider).HasColumnName("provider").HasMaxLength(50).IsRequired();
        builder.Property(d => d.RejectionReason)
            .HasColumnName("rejection_reason")
            .HasMaxLength(DayAdjustment.MaxRejectionReasonLength);
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(d => new { d.AthleteId, d.Date }).HasDatabaseName("IX_day_adjustments_athlete_date");
    }
}
