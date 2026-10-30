using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class PerformedSetConfiguration : IEntityTypeConfiguration<PerformedSet>
{
    public void Configure(EntityTypeBuilder<PerformedSet> builder)
    {
        builder.ToTable("performed_sets");

        builder.Property(p => p.AthleteId)
            .HasColumnName("athlete_id")
            .IsRequired();

        builder.Property(p => p.SessionId)
            .HasColumnName("session_id")
            .IsRequired();

        builder.Property(p => p.ExerciseId)
            .HasColumnName("exercise_id")
            .IsRequired();

        builder.Property(p => p.Date)
            .HasColumnName("date")
            .IsRequired();

        builder.Property(p => p.Weight)
            .HasColumnName("weight")
            .HasPrecision(6, 2)
            .IsRequired();

        builder.Property(p => p.Reps)
            .HasColumnName("reps")
            .IsRequired();

        builder.HasOne(p => p.Session)
            .WithMany()
            .HasForeignKey(p => p.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Exercise)
            .WithMany()
            .HasForeignKey(p => p.ExerciseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.AthleteId, p.Date });
    }
}

