using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Common;

namespace Infrastructure.Persistence.Configurations;

public sealed class CatalogTestConfiguration : IEntityTypeConfiguration<CatalogTest>
{
    public void Configure(EntityTypeBuilder<CatalogTest> builder)
    {
        builder.ToTable("catalogo_tests");

        builder.Property(e => e.Name)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(e => e.MotorCapacity)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.MeasurementUnit)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.DifficultyLevel)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(e => e.Active)
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasIndex(e => e.PfId);
        builder.HasIndex(e => new { e.PfId, e.Active });
    }
}