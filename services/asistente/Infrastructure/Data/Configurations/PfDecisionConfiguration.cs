using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class PfDecisionConfiguration : IEntityTypeConfiguration<PfDecision>
{
    public void Configure(EntityTypeBuilder<PfDecision> builder)
    {
        builder.ToTable("pf_decisions");

        builder.Property(d => d.AthleteId).HasColumnName("athlete_id");
        builder.Property(d => d.Date).HasColumnName("date").IsRequired();
        builder.Property(d => d.Source)
            .HasColumnName("source")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(d => d.SourceId).HasColumnName("source_id").IsRequired();
        builder.Property(d => d.Zone).HasColumnName("zone").HasMaxLength(200);
        builder.Property(d => d.SuggestedExerciseId).HasColumnName("suggested_exercise_id").IsRequired();
        builder.Property(d => d.Decision)
            .HasColumnName("decision")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(d => d.FinalExerciseId).HasColumnName("final_exercise_id");
        builder.Property(d => d.FinalPercentage).HasColumnName("final_percentage").HasPrecision(5, 2);
        builder.Property(d => d.Reason).HasColumnName("reason").HasMaxLength(500);

        builder.HasIndex(d => d.AthleteId).HasDatabaseName("IX_pf_decisions_athlete_id");
        builder.HasIndex(d => d.SourceId).HasDatabaseName("IX_pf_decisions_source_id");
    }
}
