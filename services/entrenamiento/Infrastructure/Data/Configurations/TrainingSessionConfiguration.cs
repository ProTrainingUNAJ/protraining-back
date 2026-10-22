using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public class TrainingSessionConfiguration : IEntityTypeConfiguration<TrainingSession>
{
    public void Configure(EntityTypeBuilder<TrainingSession> builder)
    {
        builder.ToTable("training_sessions");

        builder.Property(ts => ts.TrainingWeekId)
            .HasColumnName("training_week_id")
            .IsRequired();

        builder.Property(ts => ts.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(ts => ts.Order)
            .HasColumnName("order_index")
            .IsRequired();

        builder.Property(ts => ts.DayOfWeek)
            .HasColumnName("day_of_week")
            .IsRequired();

        builder.Property(ts => ts.CoachNotes)
            .HasColumnName("coach_notes")
            .HasMaxLength(1000);

        builder.HasIndex(ts => new { ts.TrainingWeekId, ts.Order })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.HasMany(ts => ts.PrescribedExercises)
            .WithOne(pe => pe.TrainingSession)
            .HasForeignKey(pe => pe.TrainingSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
