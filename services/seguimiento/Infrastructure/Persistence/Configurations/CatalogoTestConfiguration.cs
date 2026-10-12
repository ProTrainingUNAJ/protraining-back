using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Common;

namespace Infrastructure.Persistence.Configurations;

public sealed class CatalogoTestConfiguration : IEntityTypeConfiguration<CatalogoTest>
{
    public void Configure(EntityTypeBuilder<CatalogoTest> builder)
    {
        builder.ToTable("catalogo_tests");

        builder.Property(e => e.Nombre)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(e => e.CapacidadMotora)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.UnidadMedida)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.NivelDificultad)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(e => e.Activo)
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasIndex(e => e.PfId);
        builder.HasIndex(e => new { e.PfId, e.Activo });
    }
}