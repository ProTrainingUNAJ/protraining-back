using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class RecordProposalConfiguration : IEntityTypeConfiguration<RecordProposal>
{
    public void Configure(EntityTypeBuilder<RecordProposal> builder)
    {
        builder.ToTable("record_proposals");

        builder.Property(p => p.AthleteId)
            .HasColumnName("athlete_id")
            .IsRequired();

        builder.Property(p => p.ExerciseId)
            .HasColumnName("exercise_id")
            .IsRequired();

        builder.Property(p => p.CurrentWeight)
            .HasColumnName("current_weight")
            .HasPrecision(6, 2)
            .IsRequired();

        builder.Property(p => p.ProposedWeight)
            .HasColumnName("proposed_weight")
            .HasPrecision(6, 2)
            .IsRequired();

        builder.Property(p => p.PerformedWeight)
            .HasColumnName("performed_weight")
            .HasPrecision(6, 2)
            .IsRequired();

        builder.Property(p => p.PerformedReps)
            .HasColumnName("performed_reps")
            .IsRequired();

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne(p => p.Exercise)
            .WithMany()
            .HasForeignKey(p => p.ExerciseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.AthleteId, p.ExerciseId, p.Status });
    }
}

