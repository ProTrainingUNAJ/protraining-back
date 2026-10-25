namespace Infrastructure.Data.Configurations;

using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class RoutineDraftConfiguration : IEntityTypeConfiguration<RoutineDraft>
{
    public void Configure(EntityTypeBuilder<RoutineDraft> builder)
    {
        builder.ToTable("routine_drafts");

        builder.Property(d => d.AthleteId).HasColumnName("athlete_id").IsRequired();
        builder.Property(d => d.Goal).HasColumnName("goal").HasMaxLength(500).IsRequired();
        builder.Property(d => d.ProposedTitle).HasColumnName("proposed_title").HasMaxLength(200).IsRequired();
        builder.Property(d => d.JustificationSummary).HasColumnName("justification_summary").IsRequired();
        builder.Property(d => d.ProviderUsed).HasColumnName("provider_used").HasMaxLength(50).IsRequired();
        builder.Property(d => d.ExercisesJson).HasColumnName("exercises_json").IsRequired();

        builder.HasIndex(d => d.AthleteId).HasDatabaseName("IX_routine_drafts_athlete_id");
        builder.HasIndex(d => d.CreatedAt).HasDatabaseName("IX_routine_drafts_created_at");
    }
}
