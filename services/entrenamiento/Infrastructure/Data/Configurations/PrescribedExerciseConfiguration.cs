using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public class PrescribedExerciseConfiguration : IEntityTypeConfiguration<PrescribedExercise>
{
    public void Configure(EntityTypeBuilder<PrescribedExercise> builder)
    {
        builder.ToTable("prescribed_exercises");

        builder.Property(x => x.Order)
            .HasColumnName("order_index")
            .IsRequired();

        builder.Property(x => x.Sets)
            .HasColumnName("sets")
            .IsRequired();

        builder.Property(x => x.Reps)
            .HasColumnName("reps")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.TargetPercentage)
            .HasColumnName("target_percentage")
            .HasPrecision(5, 2);

        builder.Property(x => x.TargetDistanceMeters)
            .HasColumnName("target_distance_meters");

        builder.Property(x => x.TargetTimeSeconds)
            .HasColumnName("target_time_seconds")
            .HasPrecision(6, 2);

        builder.Property(x => x.TrainingBlockId)
            .HasColumnName("training_block_id")
            .IsRequired();

        builder.Property(x => x.TrainingSessionId)
            .HasColumnName("training_session_id")
            .IsRequired(false);

        builder.Property(x => x.ExerciseId)
            .HasColumnName("exercise_id")
            .IsRequired();

        builder.Property(x => x.IntensityType)
            .HasColumnName("intensity_type")
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired(false);

        builder.Property(x => x.TargetRpe)
            .HasColumnName("target_rpe")
            .HasPrecision(4, 2)
            .IsRequired(false);

        builder.Property(x => x.TargetRir)
            .HasColumnName("target_rir")
            .IsRequired(false);

        builder.Property(x => x.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000)
            .IsRequired(false);

        builder.HasIndex(x => new { x.TrainingBlockId, x.Order })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.HasOne(x => x.TrainingBlock)
            .WithMany(tb => tb.PrescribedExercises)
            .HasForeignKey(x => x.TrainingBlockId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.TrainingSession)
            .WithMany(ts => ts.PrescribedExercises)
            .HasForeignKey(x => x.TrainingSessionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Exercise)
            .WithMany()
            .HasForeignKey(x => x.ExerciseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
