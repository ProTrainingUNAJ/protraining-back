using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class AthleteRecommendationConfiguration : IEntityTypeConfiguration<AthleteRecommendation>
{
    public void Configure(EntityTypeBuilder<AthleteRecommendation> builder)
    {
        builder.ToTable("athlete_recommendations");

        builder.Property(r => r.AthleteId).HasColumnName("athlete_id").IsRequired();
        builder.Property(r => r.CategoryId).HasColumnName("category_id");
        builder.Property(r => r.CategoryName)
            .HasColumnName("category_name")
            .HasMaxLength(AthleteRecommendation.MaxCategoryNameLength);
        builder.Property(r => r.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(r => r.Title)
            .HasColumnName("title")
            .HasMaxLength(AthleteRecommendation.MaxTitleLength)
            .IsRequired();
        builder.Property(r => r.Reason)
            .HasColumnName("reason")
            .HasMaxLength(AthleteRecommendation.MaxReasonLength)
            .IsRequired();
        builder.Property(r => r.RecommendationText)
            .HasColumnName("recommendation_text")
            .HasMaxLength(AthleteRecommendation.MaxRecommendationTextLength)
            .IsRequired();
        builder.Property(r => r.MetricsSnapshotJson)
            .HasColumnName("metrics_snapshot_json")
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(r => r.Provider)
            .HasColumnName("provider")
            .HasMaxLength(AthleteRecommendation.MaxProviderLength)
            .IsRequired();
        builder.Property(r => r.ReviewedAtUtc).HasColumnName("reviewed_at_utc");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(r => new { r.AthleteId, r.Status })
            .HasDatabaseName("IX_athlete_recommendations_athlete_status");
        builder.HasIndex(r => new { r.CategoryId, r.Status })
            .HasDatabaseName("IX_athlete_recommendations_category_status");
    }
}
