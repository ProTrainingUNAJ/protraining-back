using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class ExerciseSubstituteConfiguration : IEntityTypeConfiguration<ExerciseSubstitute>
{
    public void Configure(EntityTypeBuilder<ExerciseSubstitute> builder)
    {
        builder.ToTable("exercise_substitutes");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(s => s.RestrictionId)
            .HasColumnName("restriction_id")
            .IsRequired();

        builder.Property(s => s.SubstituteExerciseId)
            .HasColumnName("substitute_exercise_id")
            .IsRequired();

        builder.Property(s => s.Order)
            .HasColumnName("order")
            .IsRequired();

        builder.HasOne(s => s.Restriction)
            .WithMany(r => r.Substitutes)
            .HasForeignKey(s => s.RestrictionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.SubstituteExercise)
            .WithMany()
            .HasForeignKey(s => s.SubstituteExerciseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.RestrictionId, s.SubstituteExerciseId }).IsUnique();
    }
}
