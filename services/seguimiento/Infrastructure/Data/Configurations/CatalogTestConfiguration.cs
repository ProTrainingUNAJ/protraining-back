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

        // HasConversion<string>() persiste el nombre del enum (p. ej. "Strength") como contrato
        // de datos con el front-end y la BD: los nombres de MotorCapacity no deben renombrarse a futuro.
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

        // Mapeo test → ejercicio de Entrenamiento. Ambas columnas son nullable:
        // solo los tests de fuerza en kg tienen objetivo, y el GUID se cachea en la primera
        // confirmación (los ids de Entrenamiento no son estables entre entornos).
        builder.Property(e => e.TargetExerciseName)
            .HasColumnName("target_exercise_name")
            .HasMaxLength(150);

        builder.Property(e => e.ExerciseId)
            .HasColumnName("exercise_id");

        // El índice simple por PfId ya lo crea ConfigureBaseEntities en AppDbContext.
        builder.HasIndex(e => new { e.PfId, e.Active });
    }
}
