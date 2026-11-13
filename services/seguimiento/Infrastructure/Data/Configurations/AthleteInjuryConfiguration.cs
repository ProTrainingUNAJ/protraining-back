using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class AthleteInjuryConfiguration : IEntityTypeConfiguration<AthleteInjury>
{
    public void Configure(EntityTypeBuilder<AthleteInjury> builder)
    {
        builder.ToTable("athlete_injuries");
        builder.Property(i => i.AthleteId).HasColumnName("athlete_id").IsRequired();
        builder.Property(i => i.Zone).HasColumnName("zone").IsRequired();
        builder.Property(i => i.Description).HasColumnName("description").IsRequired();
        builder.Property(i => i.InjuryDate).HasColumnName("injury_date").HasColumnType("date").IsRequired();
        builder.Property(i => i.EstimatedReturnDate).HasColumnName("estimated_return_date").HasColumnType("date").IsRequired();
        builder.Property(i => i.Status).HasColumnName("status").IsRequired();
        builder.Property(i => i.ClearedAt).HasColumnName("cleared_at");

        builder.HasIndex(i => new { i.AthleteId, i.Status });
    }
}
