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
            .IsRequired();

        builder.Property(x => x.TargetPercentage)
            .HasColumnName("target_percentage")
            .HasPrecision(5, 2); 

        builder.Property(x => x.TrainingSessionId)
            .HasColumnName("training_session_id")
            .IsRequired();

        builder.Property(x => x.ExerciseId)
            .HasColumnName("exercise_id")
            .IsRequired();

        builder.HasOne(x => x.Exercise)
            .WithMany() 
            .HasForeignKey(x => x.ExerciseId)
            .OnDelete(DeleteBehavior.Restrict); 
    }
}