using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class AthleteCategoryConfiguration : IEntityTypeConfiguration<AthleteCategory>
{
    public void Configure(EntityTypeBuilder<AthleteCategory> builder)
    {
        builder.ToTable("athlete_categories");

        builder.Property(link => link.AthleteId)
            .HasColumnName("athlete_id")
            .IsRequired();

        builder.Property(link => link.CategoryId)
            .HasColumnName("category_id")
            .IsRequired();

        builder.HasOne<Athlete>()
            .WithMany()
            .HasForeignKey(link => link.AthleteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(link => link.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // An athlete can belong to a category only once (soft-deleted rows ignored).
        builder.HasIndex(link => new { link.AthleteId, link.CategoryId })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(link => link.CategoryId);
    }
}
