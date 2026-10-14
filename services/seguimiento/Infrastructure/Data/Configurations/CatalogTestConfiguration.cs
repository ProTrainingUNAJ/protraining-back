using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class CatalogTestConfiguration : IEntityTypeConfiguration<CatalogTest>
{
    public void Configure(EntityTypeBuilder<CatalogTest> builder)
    {
        builder.ToTable("catalog_tests");

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(e => e.MotorCapacity)
            .HasColumnName("motor_capacity")
            .HasConversion<string>()
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.MeasurementUnit)
            .HasColumnName("measurement_unit")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.DifficultyLevel)
            .HasColumnName("difficulty_level")
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(e => e.Active)
            .HasColumnName("active")
            .IsRequired()
            .HasDefaultValue(true);

        // El índice simple por PfId ya lo crea ConfigureBaseEntities en AppDbContext.
        builder.HasIndex(e => new { e.PfId, e.Active });
    }
}
