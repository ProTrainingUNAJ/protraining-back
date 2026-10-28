using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class PlanDraftConfiguration : IEntityTypeConfiguration<PlanDraft>
{
    public void Configure(EntityTypeBuilder<PlanDraft> builder)
    {
        builder.ToTable("plan_drafts");

        builder.Property(d => d.CategoryId).HasColumnName("category_id");
        builder.Property(d => d.AthleteId).HasColumnName("athlete_id");
        builder.Property(d => d.Goal).HasColumnName("goal").HasMaxLength(PlanDraft.MaxGoalLength).IsRequired();
        builder.Property(d => d.StartDate).HasColumnName("start_date").IsRequired();
        builder.Property(d => d.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(d => d.Content).HasColumnName("content").HasColumnType("jsonb").IsRequired();
        builder.Property(d => d.Provider).HasColumnName("provider").HasMaxLength(50).IsRequired();
        builder.Property(d => d.ApprovedPlanId).HasColumnName("approved_plan_id");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(d => d.CategoryId).HasDatabaseName("IX_plan_drafts_category_id");
        builder.HasIndex(d => d.AthleteId).HasDatabaseName("IX_plan_drafts_athlete_id");
    }
}
