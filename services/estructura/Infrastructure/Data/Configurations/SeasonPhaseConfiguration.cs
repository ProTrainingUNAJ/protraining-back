using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class SeasonPhaseConfiguration : IEntityTypeConfiguration<SeasonPhase>
{
    public void Configure(EntityTypeBuilder<SeasonPhase> builder)
    {
        builder.ToTable("season_phases", table =>
        {
            table.HasCheckConstraint("ck_season_phases_date_range", "end_date >= start_date");
        });

        builder.Property(p => p.CategoryId)
            .HasColumnName("category_id")
            .IsRequired();

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasMaxLength(SeasonPhase.MaxNameLength)
            .IsRequired();

        builder.Property(p => p.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.StartDate)
            .HasColumnName("start_date")
            .IsRequired();

        builder.Property(p => p.EndDate)
            .HasColumnName("end_date")
            .IsRequired();

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.CategoryId, p.StartDate });
    }
}
