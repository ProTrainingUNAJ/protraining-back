using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class AthleteSessionAdjustmentConfiguration : IEntityTypeConfiguration<AthleteSessionAdjustment>
{
    public void Configure(EntityTypeBuilder<AthleteSessionAdjustment> builder)
    {
        builder.ToTable("athlete_session_adjustments");

        builder.Property(a => a.TrainingSessionId).HasColumnName("training_session_id").IsRequired();
        builder.Property(a => a.AthleteId).HasColumnName("athlete_id").IsRequired();
        builder.Property(a => a.TrainingPlanId).HasColumnName("training_plan_id").IsRequired();
        builder.Property(a => a.Date).HasColumnName("date").IsRequired();
        builder.Property(a => a.Note).HasColumnName("note").HasMaxLength(1000);
        builder.Property(a => a.Items).HasColumnName("items").HasColumnType("jsonb").IsRequired();
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(a => new { a.TrainingSessionId, a.AthleteId, a.Date })
            .HasDatabaseName("IX_athlete_session_adjustments_session_athlete_date");

        builder.HasIndex(a => new { a.TrainingPlanId, a.AthleteId })
            .HasDatabaseName("IX_athlete_session_adjustments_plan_athlete");
    }
}
